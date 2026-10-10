using System;
using System.Collections.Generic;
using System.Linq;

namespace XivSyncManager;

public sealed partial class SyncCoordinator
{
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
        if (pair.Adapter.LocalHolds || mediaOperations.ContainsKey(pair.Key)
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
            owned = new() { CharacterIdentity = change.CharacterIdentity, RestoreRequested = true };
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
        errors.Remove(pair.Key);
        retryAfter.Remove(pair.Key);
    }
}
