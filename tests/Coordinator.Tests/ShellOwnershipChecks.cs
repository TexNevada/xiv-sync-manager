using System.Reflection;
using XivSyncManager;

namespace CoordinatorTests;

internal static class ShellOwnershipChecks
{
    private static object? Invoke(SyncCoordinator manager, string name, params object[] args) =>
        typeof(SyncCoordinator).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, args);
    private static void Pairs(SyncCoordinator manager, PairSnapshot pair) =>
        typeof(SyncCoordinator).GetProperty(nameof(SyncCoordinator.Pairs))!.SetValue(manager, new[] { pair });

    internal static void Run(Action<bool, string> check)
    {
        Environment.Reset();
        var native = new Environment(SyncProvider.PlayerSync);
        var state = new Configuration();
        var coordinator = new SyncCoordinator(state);
        var pair = native.Snapshot();
        Pairs(coordinator, pair);
        Invoke(coordinator, "StartPause", pair);
        pair = native.Snapshot();
        Pairs(coordinator, pair);
        Invoke(coordinator, "FinishOperations");
        var owned = state.OwnedPauses[pair.Key];
        var shell = new ShellGroupInfo(new("owned-shell"));
        native.Groups.Add(shell.GID, shell);
        var pairManager = (PlayerSync.PlayerData.Pairs.PairManager)ReflectionAccess.Read(native.Adapter, "pairManager")!;
        pairManager.GroupPairs.Add(shell, [native.Pair]);
        native.Api.OnGroupsGetAll = () => Task.FromResult(native.Groups.Values.ToList());
        var preparation = typeof(SyncCoordinator).GetMethod("PrepareShellPauseReasons", BindingFlags.Instance | BindingFlags.NonPublic);
        check(preparation != null, "PlayerSync shell pause integrates with manager ownership before native reason writes");
        var tab = new Syncshells(() => Plugin.PluginInterface.InstalledPlugins,
            (targets, reason, write) => preparation!.Invoke(coordinator, [targets, reason, write]));
        tab.Refresh(TimeSpan.Zero);
        tab.Act(tab.VisibleRows.Single(), SyncshellControl.Pause, TimeSpan.Zero);
        check(native.Pair.PauseReason == "In a paused syncshell" && native.Api.ShellRequests.Count == 1,
            "PlayerSync shell pause reproduces native member reasons and exact shell request");
        pair = native.Snapshot();
        Pairs(coordinator, pair);
        Invoke(coordinator, "ObserveExternalChanges");
        check(ReferenceEquals(owned, state.OwnedPauses.GetValueOrDefault(pair.Key))
            && owned.OriginalPauseReason == pair.PauseReason && !state.AutomaticExceptions.Contains(pair.Key),
            "A confirmed shell reason change preserves ownership rather than reporting an external conflict");
        Invoke(coordinator, "StartRestore", pair, owned);
        Pairs(coordinator, native.Snapshot());
        Invoke(coordinator, "FinishOperations");
        check(state.OwnedPauses.Count == 0 && !native.Pair.UserPair.OwnPermissions.IsPaused(),
            "Character restoration still works after the shell's native reason change");

        pair = native.Snapshot();
        Pairs(coordinator, pair);
        Invoke(coordinator, "StartPause", pair);
        pair = native.Snapshot();
        Pairs(coordinator, pair);
        Invoke(coordinator, "FinishOperations");
        native.Pair.PauseReason = "Manual pause changed after snapshot";
        var wrote = false;
        try { preparation!.Invoke(coordinator, [new object[] { native.Pair }, "In a paused syncshell", (Action)(() => wrote = true)]); }
        catch (TargetInvocationException) { }
        check(!wrote, "Shell preparation rereads native reasons and cannot overwrite an external change after snapshot");
        native.Pair.PauseReason = pair.PauseReason;
        state.OwnedPauses[pair.Key].PendingShellPauseReason = "In a paused syncshell";
        Invoke(coordinator, "ObserveExternalChanges");
        check(state.OwnedPauses[pair.Key].PendingShellPauseReason == null,
            "An interrupted shell intent reconciles the unchanged original reason without replay or permanent blockage");
    }
}
