using System;
using System.Collections.Generic;
using System.Linq;

namespace XivSyncManager;

public sealed partial class SyncCoordinator
{
    private readonly Dictionary<string, string> actionErrors = new(StringComparer.Ordinal);
    public string? ActionErrorFor(PairSnapshot pair) => actionErrors.GetValueOrDefault(pair.Key);

    public string? ReloadUnavailableReason(PairSnapshot pair) =>
        !Pairs.Contains(pair) || stopping ? "This pair is no longer current."
        : IsPending(pair) || connectionOperations.ContainsKey(pair.Provider) ? "Wait for pending changes."
        : !pair.Online || !pair.Visible ? "Reload last data requires a nearby, online player."
        : pair.OwnPaused || pair.OtherPaused || pair.ManagerHeld || pair.ExternalHold || IsManual(pair) || IsAutomatic(pair)
            ? "Resume this route before reloading its appearance."
        : !pair.Adapter.CanReapply(pair) ? "This version has no supported native reload action." : null;

    public void ReloadLastData(PairSnapshot pair)
    {
        lock (SyncRoot)
        {
            var reason = ReloadUnavailableReason(pair);
            if (reason != null) { actionErrors[pair.Key] = reason; return; }
            try
            {
                if (!pair.Adapter.Reapply(pair)) throw new NotSupportedException("Native reload is unavailable.");
                actionErrors.Remove(pair.Key);
            }
            catch (Exception exception) { actionErrors[pair.Key] = ReflectionAccess.Unwrap(exception).Message; }
        }
    }

    private readonly Dictionary<string, NativeCycle> cycles = new(StringComparer.Ordinal);
    private sealed record NativeCycle(object Pair)
    {
        internal DateTime? ConfirmedAt { get; set; }
    }

    public string? CycleUnavailableReason(PairSnapshot pair) =>
        !Pairs.Contains(pair) || stopping ? "This pair is no longer current."
        : IsPending(pair) || connectionOperations.ContainsKey(pair.Provider) ? "Wait for pending changes."
        : !pair.Online ? "Cycle pause requires an online player."
        : pair.OwnPaused || pair.OtherPaused || pair.ManagerHeld || pair.ExternalHold || IsManual(pair) || IsAutomatic(pair) || IsOwned(pair)
            ? "Cycling ends with a resume. Release existing pauses first; manager and native pauses are protected."
        : !pair.Media.CanEdit ? "This version has no supported native permission action." : null;

    public void CyclePauseState(PairSnapshot pair)
    {
        lock (SyncRoot)
        {
            var reason = CycleUnavailableReason(pair);
            if (reason != null) { actionErrors[pair.Key] = reason; return; }
            try
            {
                if (!CanStart(pair, true)) throw new InvalidOperationException("Wait for the current request budget or retry delay.");
                var owned = pair.Adapter.PrepareNativeCycle(pair);
                configuration.OwnedPauses[pair.Key] = owned;
                // Use the same native permission endpoint as the providers' cycle menus,
                // but serialize both stages through durable ownership. Older native cycle
                // helpers return CompletedTask while an untracked background write still runs.
                try { StartPause(pair); }
                catch { configuration.OwnedPauses.Remove(pair.Key); throw; }
                cycles[pair.Key] = new(pair.Pair);
                actionErrors.Remove(pair.Key);
            }
            catch (Exception exception) { actionErrors[pair.Key] = ReflectionAccess.Unwrap(exception).Message; }
            RefreshSoon();
        }
    }

    private void FinishCycles(DateTime now)
    {
        foreach (var (key, cycle) in cycles.ToArray())
        {
            var pair = Pairs.FirstOrDefault(p => p.Key == key && ReferenceEquals(p.Pair, cycle.Pair));
            if (pair == null)
            {
                // Retain durable responsibility for any pause; never queue a new cycle on reconnect.
                if (configuration.OwnedPauses.TryGetValue(key, out var interrupted))
                {
                    interrupted.RestoreRequested = true;
                    configuration.Save();
                }
                cycles.Remove(key);
                actionErrors[key] = "The pair or connection changed; any saved cycle pause will be restored, not replayed.";
                continue;
            }
            if (!configuration.OwnedPauses.TryGetValue(key, out var owned))
            {
                cycles.Remove(key);
                if (cycle.ConfirmedAt != null && !pair.OwnPaused) actionErrors.Remove(key);
                else actionErrors[key] = "The cycle stopped because native permissions changed. Check the original plugin.";
                continue;
            }
            if (operations.ContainsKey(key)) continue;
            if (!owned.Confirmed && !owned.RestoreRequested)
            {
                try { StartPause(pair); }
                catch (Exception exception) { RecordError(key, exception); }
                continue;
            }
            cycle.ConfirmedAt ??= now;
            if (now - cycle.ConfirmedAt.Value < TimeSpan.FromSeconds(1) || owned.RestoreRequested) continue;
            owned.RestoreRequested = true;
            configuration.Save();
        }
    }

    internal void PrepareShellPauseReasons(IReadOnlyList<object> targets, string reason, Action write)
    {
        lock (SyncRoot)
        {
            if (stopping) throw new InvalidOperationException("The manager is stopping.");
            var affected = targets.Select(target => Pairs.SingleOrDefault(p => p.Provider == SyncProvider.PlayerSync
                    && ReferenceEquals(p.Pair, target)) ?? throw new InvalidOperationException("Shell membership changed; refresh before pausing."))
                .ToArray();
            foreach (var pair in affected)
            {
                var current = pair.Adapter.ReadCurrent(pair);
                if (current.Permissions != pair.Permissions || current.PauseReason != pair.PauseReason)
                    throw new InvalidOperationException("Native member permissions or reasons changed; refresh before pausing this shell.");
                if (IsPending(pair) || configuration.LatePauseChanges.ContainsKey(pair.Key) || configuration.LateMediaChanges.ContainsKey(pair.Key))
                    throw new InvalidOperationException("Wait for member pause/media changes to settle before pausing this shell.");
                if (configuration.OwnedPauses.TryGetValue(pair.Key, out var owned)
                    && (!owned.Confirmed || owned.PendingShellPauseReason != null || owned.OriginalPauseReason != pair.PauseReason
                        || pair.Permissions != owned.PausedPermissions && pair.Permissions != owned.OriginalPermissions))
                    throw new InvalidOperationException("A member's pause changed; refresh before pausing this shell.");
            }
            var records = affected.Select(p => configuration.OwnedPauses.GetValueOrDefault(p.Key)).Where(o => o != null).ToArray();
            foreach (var owned in records) owned!.PendingShellPauseReason = reason;
            // Persist both accepted reasons before the native local write, including across reload/crash.
            // A failed save must stop the native action, and must not leave an unsent expectation in memory.
            try { if (records.Length > 0) configuration.Save(); }
            catch { foreach (var owned in records) owned!.PendingShellPauseReason = null; throw; }
            write();
        }
    }

    private void ReconcileShellPauseReason(PairSnapshot pair)
    {
        if (!configuration.OwnedPauses.TryGetValue(pair.Key, out var owned) || owned.PendingShellPauseReason is not { } expected
            || pair.Permissions != owned.PausedPermissions && pair.Permissions != owned.OriginalPermissions) return;
        var current = pair.Adapter.ReadCurrent(pair);
        if (current.PauseReason != pair.PauseReason || current.Permissions != pair.Permissions) return;
        if (pair.PauseReason == expected) owned.OriginalPauseReason = expected;
        else if (pair.PauseReason != owned.OriginalPauseReason) return;
        // The native reason setter is synchronous. An unchanged original reason on
        // a fresh read resolves an interrupted/unsent intent; never replay the action.
        owned.PendingShellPauseReason = null;
        configuration.Save();
    }

    private void RememberUnconfirmedPause(string key, OwnedPause owned)
    {
        if (owned.LocalHold || owned.PauseChange is not { } change) return;
        if (!configuration.LatePauseChanges.TryGetValue(key, out var history))
            configuration.LatePauseChanges[key] = history = [];
        // Each uncertain dispatch may still reply, including identical retries.
        history.Add(change);
        owned.PauseChange = null;
    }

    private void ReconcileLatePauses(PairSnapshot pair)
    {
        if (mediaOperations.ContainsKey(pair.Key)
            || operations.TryGetValue(pair.Key, out var operation) && !operation.Task.IsCompleted
            || !configuration.LatePauseChanges.TryGetValue(pair.Key, out var history)) return;
        var owned = configuration.OwnedPauses.GetValueOrDefault(pair.Key);
        if (owned != null && (owned.LocalHold || owned.PauseChange != null || owned.MediaChange != null
            || owned.OriginalPauseReason != pair.PauseReason
            || owned.PausedPermissions == pair.Permissions)) return;
        var change = history.LastOrDefault(c => c.PausedPermissions == pair.Permissions && c.PauseReason == pair.PauseReason);
        if (change == null) return;
        // Match the full account-scoped fingerprint and reason, never an arbitrary pause.
        // Do not consume older uncertain requests just because a newer pause succeeded:
        // another reply may still arrive after that pause is restored.
        if (operation != null)
        {
            _ = operation.Task.Exception;
            operations.Remove(pair.Key);
        }
        if (owned == null)
        {
            owned = new() { CharacterIdentity = change.CharacterIdentity, RestoreRequested = true, NativeCycle = change.NativeCycle };
            configuration.OwnedPauses[pair.Key] = owned;
        }
        owned.OriginalPermissions = change.OriginalPermissions;
        owned.PausedPermissions = change.PausedPermissions;
        owned.OriginalPauseReason = change.PauseReason;
        owned.Confirmed = true;
        history.Remove(change);
        if (history.Count == 0) configuration.LatePauseChanges.Remove(pair.Key);
        // StartRestore also saves before dispatch, so a failed recovery save cannot be bypassed.
        configuration.Save();
        ClearRetryState(pair.Key);
    }
}
