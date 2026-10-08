using System.Reflection;
using XivSyncManager;

namespace CoordinatorTests;

internal static class RecoveryChecks
{
    private static object? Invoke(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException!; }
    }

    private static void SetPairs(SyncCoordinator manager, params PairSnapshot[] pairs) =>
        typeof(SyncCoordinator).GetProperty(nameof(SyncCoordinator.Pairs))!.SetValue(manager, pairs);

    internal static async Task Run(Action<bool, string> assert)
    {
        foreach (var failure in new[] { "fault", "cancel", "timeout", "invoke", "reload", "retry" })
        {
            Environment.Reset();
            var native = new Environment(SyncProvider.PlayerSync);
            var state = new Configuration();
            var manager = new SyncCoordinator(state);
            var initial = native.Snapshot();
            Permissions delayed = Permissions.None;
            Configuration? saved = null;
            Plugin.PluginInterface.Write = snapshot => saved = snapshot;
            native.Api.Request = request =>
            {
                delayed = request.Permissions;
                assert(saved?.OwnedPauses[initial.Key].PauseChange?.PausedPermissions == "1",
                    "Expected pause reply is durable before dispatch");
                if (failure == "invoke") throw new IOException("Invocation failed after possible dispatch");
                return failure == "cancel" ? Task.FromCanceled(new CancellationToken(true))
                    : failure is "timeout" or "reload" ? Task.CompletedTask
                    : Task.FromException(new TimeoutException("Transport failure after possible dispatch"));
            };
            Invoke(manager, "StartPause", initial);
            SetPairs(manager, initial);
            if (failure == "timeout")
            {
                // Let the real confirmation-timeout path run without a wall-clock wait.
                var operations = (System.Collections.IDictionary)typeof(SyncCoordinator)
                    .GetField("operations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
                var pending = operations[initial.Key]!;
                pending.GetType().GetProperty("Deadline")!.SetValue(pending, DateTime.UtcNow.AddSeconds(-1));
            }
            if (failure != "reload") Invoke(manager, "FinishOperations");
            else { state = saved!.CreateSnapshot(); manager = new(state); }
            assert(state.LatePauseChanges.ContainsKey(initial.Key) && state.OwnedPauses[initial.Key].PauseChange == null,
                $"{failure} retains a separate durable uncertain-pause record");
            native.Api.Request = null;
            manager.SetManualPause(initial, false);
            if (failure == "retry")
            {
                state.OwnedPauses[initial.Key].RestoreRequested = false;
                Invoke(manager, "StartPause", initial);
                SetPairs(manager, native.Snapshot());
                Invoke(manager, "FinishOperations");
                assert(state.LatePauseChanges.Count == 1, "A successful retry cannot discard an older uncertain reply");
            }
            var beforeRestore = native.Snapshot();
            Invoke(manager, "StartRestore", beforeRestore, state.OwnedPauses[initial.Key]);
            SetPairs(manager, native.Snapshot());
            Invoke(manager, "FinishOperations");
            assert(state.OwnedPauses.Count == 0 && state.LatePauseChanges.Count == 1, "Resume completes while uncertain pause history survives");
            state = state.CreateSnapshot();
            manager = new(state);
            manager.ClearCache();
            assert(state.LatePauseChanges.Count == 1, "Reload and cache clearing retain uncertain pause history");
            native.Pair.UserPair.OwnPermissions = delayed;
            var late = native.Snapshot();
            SetPairs(manager, late);
            Invoke(manager, "ObserveExternalChanges");
            assert(state.OwnedPauses[late.Key].RestoreRequested && state.LatePauseChanges.Count == 0,
                "Late expected pause recovers ownership and requests restoration");
            var requests = native.Api.Requests;
            Plugin.PluginInterface.Write = _ => throw new IOException("Recovery storage failure");
            try { Invoke(manager, "StartRestore", late, state.OwnedPauses[late.Key]); throw new Exception("Save failure ignored"); }
            catch (IOException) { }
            assert(native.Api.Requests == requests, "Recovered pause cannot be restored without successful persistence");
            Plugin.PluginInterface.Write = null;
            Invoke(manager, "StartRestore", late, state.OwnedPauses[late.Key]);
            SetPairs(manager, native.Snapshot());
            Invoke(manager, "FinishOperations");
            assert(state.OwnedPauses.Count == 0 && !native.Pair.UserPair.OwnPermissions.IsPaused(), "Late pause restoration is confirmed");
        }

        // Identical failed retries can each reply after separate successful restores.
        Environment.Reset();
        var repeated = new Environment(SyncProvider.PlayerSync);
        var repeatedState = new Configuration();
        var repeatedManager = new SyncCoordinator(repeatedState);
        var repeatedPair = repeated.Snapshot();
        repeated.Api.Request = _ => Task.FromException(new TimeoutException("Possible dispatch"));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            repeatedManager.SetManualPause(repeatedPair, false);
            if (repeatedState.OwnedPauses.TryGetValue(repeatedPair.Key, out var existing)) existing.RestoreRequested = false;
            Invoke(repeatedManager, "StartPause", repeatedPair);
            SetPairs(repeatedManager, repeatedPair);
            Invoke(repeatedManager, "FinishOperations");
        }
        assert(repeatedState.LatePauseChanges[repeatedPair.Key].Count == 2, "Identical uncertain retries retain distinct recovery responsibilities");
        repeated.Api.Request = null;
        repeatedManager.SetManualPause(repeatedPair, false);
        Invoke(repeatedManager, "StartRestore", repeatedPair, repeatedState.OwnedPauses[repeatedPair.Key]);
        SetPairs(repeatedManager, repeated.Snapshot());
        Invoke(repeatedManager, "FinishOperations");
        for (var reply = 0; reply < 2; reply++)
        {
            repeated.Pair.UserPair.OwnPermissions = Permissions.Paused;
            var response = repeated.Snapshot();
            SetPairs(repeatedManager, response);
            Invoke(repeatedManager, "ObserveExternalChanges");
            Invoke(repeatedManager, "StartRestore", response, repeatedState.OwnedPauses[response.Key]);
            SetPairs(repeatedManager, repeated.Snapshot());
            Invoke(repeatedManager, "FinishOperations");
            assert(!repeated.Pair.UserPair.OwnPermissions.IsPaused(), "Each delayed identical reply can be restored");
        }
        assert(repeatedState.LatePauseChanges.Count == 0, "Both separately observed delayed retries consume their recovery records");

        // Exact fingerprints, pause reasons, and running requests remain protection boundaries.
        Environment.Reset();
        var guarded = new Environment(SyncProvider.PlayerSync);
        var guards = new Configuration();
        var guardManager = new SyncCoordinator(guards);
        var guardPair = guarded.Snapshot();
        guards.LatePauseChanges[guardPair.Key] = [new() { OriginalPermissions = "0", PausedPermissions = "1", PauseReason = "" }];
        guarded.Pair.UserPair.OwnPermissions = Permissions.Paused | Permissions.Sounds;
        SetPairs(guardManager, guarded.Snapshot());
        Invoke(guardManager, "ObserveExternalChanges");
        assert(guards.OwnedPauses.Count == 0, "Different permissions are not claimed as a delayed manager pause");
        guarded.Pair.UserPair.OwnPermissions = Permissions.Paused;
        guarded.Pair.PauseReason = "Manual external pause";
        SetPairs(guardManager, guarded.Snapshot());
        Invoke(guardManager, "ObserveExternalChanges");
        assert(guards.OwnedPauses.Count == 0, "Changed external pause reasons are never claimed");
        guarded.Pair.PauseReason = "";
        guarded.Pair.UserPair.OwnPermissions = Permissions.None;
        var running = new TaskCompletionSource();
        guarded.Api.Request = _ => running.Task;
        guardPair = guarded.Snapshot();
        Invoke(guardManager, "StartPause", guardPair);
        guarded.Pair.UserPair.OwnPermissions = Permissions.Paused;
        SetPairs(guardManager, guarded.Snapshot());
        Invoke(guardManager, "ObserveExternalChanges");
        assert(guards.LatePauseChanges.Count == 1 && guards.OwnedPauses[guardPair.Key].PauseChange != null,
            "A running native request blocks late-history reinterpretation");
        running.SetResult();

        Environment.Reset();
        var unsent = new Environment(SyncProvider.PlayerSync);
        var unsentState = new Configuration();
        var unsentManager = new SyncCoordinator(unsentState);
        var unsentPair = unsent.Snapshot();
        unsent.Api.IsConnected = false;
        Invoke(unsentManager, "StartPause", unsentPair);
        assert(unsent.Api.Requests == 0 && unsentState.OwnedPauses.Count == 0 && unsentState.LatePauseChanges.Count == 0,
            "Pre-dispatch validation failures leave no fabricated late-pause history");

        Environment.Reset();
        var finalAttempt = new Environment(SyncProvider.PlayerSync);
        var finalState = new Configuration();
        var finalManager = new SyncCoordinator(finalState);
        Invoke(finalManager, "StartPause", finalAttempt.Snapshot());
        SetPairs(finalManager, finalAttempt.Snapshot());
        Invoke(finalManager, "FinishOperations");
        Invoke(finalManager, "RecordError", finalAttempt.Snapshot().Key, new IOException("Earlier transient failure"));
        await finalManager.StopAsync();
        assert(!finalAttempt.Pair.UserPair.OwnPermissions.IsPaused() && finalAttempt.Api.Requests == 2,
            "Shutdown makes a final durable restoration attempt despite an earlier retry delay");

        // Cleanup must release only the manager's source even with repeated storage failures.
        foreach (var reload in new[] { false, true })
        {
            Environment.Reset();
            var snow = new Environment(SyncProvider.Snowcloak);
            var backup = new Environment(SyncProvider.PlayerSync);
            var cleanupState = new Configuration();
            var cleanup = new SyncCoordinator(cleanupState);
            Invoke(cleanup, "StartPause", snow.Snapshot());
            Invoke(cleanup, "StartPause", backup.Snapshot());
            SetPairs(cleanup, snow.Snapshot(), backup.Snapshot());
            Invoke(cleanup, "FinishOperations");
            var nativeSnow = (Snowcloak.PlayerData.Pairs.Pair)snow.Pair;
            nativeSnow.HoldDownloads("Other plugin", 1);
            nativeSnow.HoldApplication("Other plugin", 1);
            if (reload) cleanup = new(cleanupState = cleanupState.CreateSnapshot());
            var requests = backup.Api.Requests;
            Plugin.PluginInterface.Write = _ => throw new IOException("Shutdown storage unavailable");
            await cleanup.StopAsync();
            await cleanupState.FlushSavesAsync();
            assert(nativeSnow.HoldApplicationReasons.SequenceEqual(["Other plugin"])
                && nativeSnow.HoldDownloadReasons.SequenceEqual(["Other plugin"]), "Failed shutdown saves still release only manager holds");
            assert(cleanupState.OwnedPauses.Count == 2, "Storage failure retains restoration records for reload");
            assert(backup.Api.Requests == requests && backup.Pair.UserPair.OwnPermissions.IsPaused(),
                "Shutdown never bypasses durable server-restoration saves");
            Plugin.PluginInterface.Write = null;
            var recovered = new SyncCoordinator(cleanupState.CreateSnapshot());
            foreach (var step in (IEnumerable<string>)Invoke(recovered, "Refresh")!) { }
            foreach (var step in (IEnumerable<string>)Invoke(recovered, "Refresh")!) { }
            assert(recovered.Configuration.OwnedPauses.Count == 0 && !backup.Pair.UserPair.OwnPermissions.IsPaused(),
                "Reload confirms released holds and restores retained server pauses");
        }
    }
}
