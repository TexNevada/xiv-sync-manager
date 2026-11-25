using System.Reflection;
using XivSyncManager;

namespace CoordinatorTests;

internal static class CycleChecks
{
    private static object? Invoke(SyncCoordinator manager, string name, params object[] args)
    {
        try { return typeof(SyncCoordinator).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException!; }
    }
    private static void SetPair(SyncCoordinator manager, PairSnapshot pair) =>
        typeof(SyncCoordinator).GetProperty(nameof(SyncCoordinator.Pairs))!.SetValue(manager, new[] { pair });

    internal static void Run(Action<bool, string> check)
    {
        foreach (var provider in new[] { SyncProvider.PlayerSync, SyncProvider.Snowcloak })
        {
            Environment.Reset();
            var native = new Environment(provider);
            native.Pair.UserPair.OwnPermissions = Permissions.Sounds | Permissions.Sticky;
            var state = new Configuration();
            var manager = new SyncCoordinator(state);
            var pair = native.Snapshot();
            SetPair(manager, pair);
            var completion = new TaskCompletionSource();
            native.Api.Request = _ => completion.Task;
            manager.CyclePauseState(pair);
            Invoke(manager, "FinishCycles", DateTime.UtcNow.AddMinutes(10));
            manager.CyclePauseState(pair);
            check(native.Api.Requests == 1 && manager.IsPending(pair) && state.OwnedPauses[pair.Key].NativeCycle,
                $"{provider}: a slow native request never loses cycle exclusion after an arbitrary timeout");
            native.Pair.UserPair.OwnPermissions |= Permissions.Paused;
            completion.SetResult();
            pair = native.Snapshot();
            SetPair(manager, pair);
            Invoke(manager, "FinishOperations");
            Invoke(manager, "FinishCycles", DateTime.UtcNow);
            check(native.Api.Requests == 1, $"{provider}: confirmed pause waits before restoring");
            native.Api.Request = null;
            Invoke(manager, "FinishCycles", DateTime.UtcNow.AddSeconds(2));
            Invoke(manager, "StartRestore", pair, state.OwnedPauses[pair.Key]);
            pair = native.Snapshot();
            SetPair(manager, pair);
            Invoke(manager, "FinishOperations");
            Invoke(manager, "FinishCycles", DateTime.UtcNow.AddSeconds(3));
            check(!manager.IsPending(pair) && state.OwnedPauses.Count == 0
                && native.Pair.UserPair.OwnPermissions == (Permissions.Sounds | Permissions.Sticky),
                $"{provider}: cycle completes through two confirmed native permission requests, preserving media and Sticky");

            manager.CyclePauseState(pair);
            var persisted = state.CreateSnapshot();
            var reloaded = new SyncCoordinator(persisted);
            pair = native.Snapshot();
            SetPair(reloaded, pair);
            Invoke(reloaded, "StartRestore", pair, persisted.OwnedPauses[pair.Key]);
            pair = native.Snapshot();
            SetPair(reloaded, pair);
            Invoke(reloaded, "FinishOperations");
            check(persisted.OwnedPauses.Count == 0 && !pair.OwnPaused,
                $"{provider}: restart restores an interrupted cycle with automatic management off");

            Environment.Reset();
            native = new Environment(provider);
            state = new Configuration();
            manager = new SyncCoordinator(state);
            pair = native.Snapshot();
            SetPair(manager, pair);
            native.Api.Request = _ => Task.FromException(new IOException("Cycle pause rejected"));
            manager.CyclePauseState(pair);
            foreach (var _ in (IEnumerable<string>)Invoke(manager, "Refresh")!) { }
            var delays = (Dictionary<string, DateTime>)typeof(SyncCoordinator)
                .GetField("retryAfter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
            delays[pair.Key] = DateTime.UtcNow.AddSeconds(-1);
            native.Pair.UserPair.OwnPermissions = Permissions.Vfx;
            native.Pair.PauseReason = "Changed in native menu";
            native.Api.Request = null;
            foreach (var _ in (IEnumerable<string>)Invoke(manager, "Refresh")!) { }
            check(native.Api.Requests == 1 && !native.Pair.UserPair.OwnPermissions.IsPaused(),
                $"{provider}: external native choices are reconciled before any cycle retry can dispatch");
        }
    }
}
