using System.Reflection;
using XivSyncManager;

namespace CoordinatorTests;

internal static class RetryAndAppearanceChecks
{
    private static object? Invoke(SyncCoordinator manager, string name, params object[] args)
    {
        try { return typeof(SyncCoordinator).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException!; }
    }

    private static void SetPairs(SyncCoordinator manager, params PairSnapshot[] pairs) =>
        typeof(SyncCoordinator).GetProperty(nameof(SyncCoordinator.Pairs))!.SetValue(manager, pairs);

    private static void Refresh(SyncCoordinator manager)
    {
        foreach (var _ in (IEnumerable<string>)Invoke(manager, "Refresh")!) { }
    }

    internal static async Task Run(Action<bool, string> assert)
    {
        Environment.Reset();
        var native = new Environment(SyncProvider.PlayerSync);
        var state = new Configuration();
        var manager = new SyncCoordinator(state);
        var pair = native.Snapshot();
        SetPairs(manager, pair);
        native.Api.Request = _ => Task.FromException(new IOException("Simulated request failure"));
        var delays = (Dictionary<string, DateTime>)typeof(SyncCoordinator)
            .GetField("retryAfter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Invoke(manager, "StartPause", pair);
            Invoke(manager, "FinishOperations");
            assert(native.Api.Requests == attempt, "One request is sent per attempt");
            if (attempt <= 3)
            {
                assert(manager.IsRetrying(pair) && manager.ErrorFor(pair) == null && Plugin.AttentionNotifications == 0,
                    "Transient failure schedules an automatic retry without an attention warning");
                Invoke(manager, "StartPause", pair);
                assert(native.Api.Requests == attempt, "Retry waits for its delay");
                delays[pair.Key] = DateTime.UtcNow.AddSeconds(-1);
            }
        }
        assert(!manager.IsRetrying(pair) && manager.ErrorFor(pair) != null && Plugin.AttentionNotifications == 1,
            "After the initial attempt and three retries, report attention once");
        Invoke(manager, "StartPause", pair);
        assert(native.Api.Requests == 4 && Plugin.AttentionNotifications == 1, "Exhausted requests do not retry indefinitely");
        native.Api.Request = null;
        manager.UseAutomaticRules(pair);
        Invoke(manager, "StartPause", pair);
        SetPairs(manager, native.Snapshot());
        Invoke(manager, "FinishOperations");
        assert(manager.ErrorFor(pair) == null && native.Api.Requests == 5, "Manual Retry starts a fresh attempt and success clears the error");

        Environment.Reset();
        var direction = new Environment(SyncProvider.PlayerSync);
        var directionState = new Configuration();
        var directionManager = new SyncCoordinator(directionState);
        var directionPair = direction.Snapshot();
        SetPairs(directionManager, directionPair);
        direction.Api.Request = _ => Task.FromException(new IOException("Simulated request failure"));
        var directionDelays = (Dictionary<string, DateTime>)typeof(SyncCoordinator)
            .GetField("retryAfter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(directionManager)!;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Invoke(directionManager, "StartPause", directionPair);
            Invoke(directionManager, "FinishOperations");
            directionDelays[directionPair.Key] = DateTime.UtcNow.AddSeconds(-1);
        }
        direction.Api.Request = null;
        Invoke(directionManager, "StartRestore", directionPair, directionState.OwnedPauses[directionPair.Key]);
        SetPairs(directionManager, direction.Snapshot());
        Invoke(directionManager, "FinishOperations");
        assert(direction.Api.Requests == 5 && directionState.OwnedPauses.Count == 0,
            "An exhausted pause cannot block a restoration in the opposite direction");

        Environment.Reset();
        var restoring = new Environment(SyncProvider.PlayerSync);
        var restoringState = new Configuration();
        var restoringManager = new SyncCoordinator(restoringState);
        var restorePair = restoring.Snapshot();
        Invoke(restoringManager, "StartPause", restorePair);
        restorePair = restoring.Snapshot();
        SetPairs(restoringManager, restorePair);
        Invoke(restoringManager, "FinishOperations");
        var owned = restoringState.OwnedPauses[restorePair.Key];
        owned.RestoreRequested = true;
        restoring.Api.Request = _ => Task.FromException(new IOException("Restore failed"));
        var restoreDelays = (Dictionary<string, DateTime>)typeof(SyncCoordinator)
            .GetField("retryAfter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(restoringManager)!;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Invoke(restoringManager, "StartRestore", restorePair, owned);
            Invoke(restoringManager, "FinishOperations");
            Invoke(restoringManager, "StartPause", restorePair);
            restoreDelays[restorePair.Key] = DateTime.UtcNow.AddSeconds(-1);
        }
        Invoke(restoringManager, "StartRestore", restorePair, owned);
        assert(restoring.Api.Requests == 5 && restoringManager.ErrorFor(restorePair) != null,
            "A saved pause choice cannot reset the retry limit for an interrupted restore");

        Environment.Reset();
        var late = new Environment(SyncProvider.PlayerSync);
        var lateState = new Configuration();
        var lateManager = new SyncCoordinator(lateState);
        var latePair = late.Snapshot();
        late.Api.Request = _ => Task.FromException(new IOException("Pause failed"));
        Invoke(lateManager, "StartPause", latePair);
        SetPairs(lateManager, latePair);
        Invoke(lateManager, "FinishOperations");
        late.Pair.UserPair.OwnPermissions = Permissions.Paused;
        latePair = late.Snapshot();
        SetPairs(lateManager, latePair);
        Invoke(lateManager, "ObserveExternalChanges");
        assert(!lateManager.IsRetrying(latePair) && lateManager.ErrorFor(latePair) == null,
            "A late confirmed pause clears the retry state without a duplicate request");

        Environment.Reset();
        var local = new Environment(SyncProvider.Snowcloak);
        var localState = new Configuration();
        var localManager = new SyncCoordinator(localState);
        ((Snowcloak.PlayerData.Pairs.Pair)local.Pair).FailApplicationAfterHold = true;
        var localPair = local.Snapshot();
        Invoke(localManager, "StartPause", localPair);
        localPair = local.Snapshot();
        SetPairs(localManager, localPair);
        Invoke(localManager, "ObserveExternalChanges");
        assert(localState.OwnedPauses[localPair.Key].Confirmed && !localManager.IsRetrying(localPair),
            "A fully applied local hold clears retry state even if the native call threw");

        // A running native request must never be duplicated, even after its deadline.
        Environment.Reset();
        var stalled = new Environment(SyncProvider.PlayerSync);
        var stalledState = new Configuration();
        var stalledManager = new SyncCoordinator(stalledState);
        var stalledPair = stalled.Snapshot();
        var completion = new TaskCompletionSource();
        stalled.Api.Request = _ => completion.Task;
        Invoke(stalledManager, "StartPause", stalledPair);
        SetPairs(stalledManager, stalledPair);
        var operations = (System.Collections.IDictionary)typeof(SyncCoordinator)
            .GetField("operations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(stalledManager)!;
        operations[stalledPair.Key]!.GetType().GetProperty("Deadline")!.SetValue(operations[stalledPair.Key], DateTime.UtcNow.AddSeconds(-1));
        Invoke(stalledManager, "FinishOperations");
        Invoke(stalledManager, "FinishOperations");
        Invoke(stalledManager, "StartPause", stalledPair);
        assert(stalled.Api.Requests == 1 && Plugin.AttentionNotifications == 1 && stalledManager.ErrorFor(stalledPair) != null,
            "A stalled request is reported once and blocks conflicting retries");
        stalled.Pair.UserPair.OwnPermissions = Permissions.Paused;
        completion.SetResult();
        SetPairs(stalledManager, stalled.Snapshot());
        Invoke(stalledManager, "FinishOperations");
        assert(stalledManager.ErrorFor(stalledPair) == null, "A late confirmed request clears the stalled warning");

        // The current PlayerSync action is the same one as its native Reload last data menu.
        Environment.Reset();
        var primary = new Environment(SyncProvider.PlayerSync);
        var backup = new Environment(SyncProvider.Snowcloak);
        Plugin.ObjectTable.Add(new Dalamud.Game.ClientState.Objects.SubKinds.TestPlayer(1, "Example", 1));
        var appearanceState = new Configuration { AutomaticManagement = true, Priority = [SyncProvider.PlayerSync, SyncProvider.Snowcloak] };
        var appearance = new SyncCoordinator(appearanceState);
        Refresh(appearance);
        Refresh(appearance);
        assert(primary.Pair.ReapplyRequests == 1 && primary.Pair.ReapplyForced == true,
            "Confirmed backup pause requests the primary's native forced cached-data reload");
        Invoke(appearance, "ProcessReapplications", DateTime.UtcNow.AddSeconds(3));
        assert(primary.Pair.ReapplyRequests == 2, "A settled handoff gets one delayed cached-data reload");
        Invoke(appearance, "ProcessReapplications", DateTime.UtcNow.AddSeconds(30));
        assert(primary.Pair.ReapplyRequests == 2, "The delayed reload does not repeat indefinitely");

        // A manual pause or stale identity before the follow-up must cancel that action.
        var active = appearance.Pairs.Single(p => p.Provider == SyncProvider.PlayerSync);
        Invoke(appearance, "TryReapply", active);
        appearanceState.CharacterPauses.Add("Example@1");
        Invoke(appearance, "ProcessReapplications", DateTime.UtcNow.AddSeconds(3));
        assert(primary.Pair.ReapplyRequests == 3, "A newly paused character is not reloaded by a stale follow-up");
        appearanceState.CharacterPauses.Clear();
        Invoke(appearance, "TryReapply", active);
        appearance.SetPreferred("Example@1", SyncProvider.Snowcloak);
        Invoke(appearance, "ProcessReapplications", DateTime.UtcNow.AddSeconds(3));
        assert(primary.Pair.ReapplyRequests == 4, "A changed preference cancels the previous route's delayed reload");

        primary.Pair.Ident = "";
        primary.Pair.HashedCID = "new-native-identity";
        assert(primary.Snapshot().Ident == "new-native-identity", "PlayerSync's HashedCID is used when Ident is unavailable");
        await state.FlushSavesAsync();
        await directionState.FlushSavesAsync();
        await restoringState.FlushSavesAsync();
        await lateState.FlushSavesAsync();
        await localState.FlushSavesAsync();
        await stalledState.FlushSavesAsync();
        await appearanceState.FlushSavesAsync();
    }
}
