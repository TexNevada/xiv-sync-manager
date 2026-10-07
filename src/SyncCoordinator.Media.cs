using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XivSyncManager;

public sealed partial class SyncCoordinator
{
    private readonly Dictionary<string, QueuedMedia> queuedMedia = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MediaOperation> mediaOperations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> mediaErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Ident, DateTime PlayedAt)> audioHistory = new(StringComparer.Ordinal);

    public string? MediaErrorFor(PairSnapshot pair) => mediaErrors.GetValueOrDefault(pair.Key);
    public IReadOnlyList<string> MediaErrors => mediaErrors.Select(e => $"{ParseProvider(e.Key)?.DisplayName()}: {e.Value}").ToArray();

    public bool CanChangeMedia(PairSnapshot pair) => !stopping && Pairs.Contains(pair) && pair.Media.CanEdit
        && !IsPending(pair) && !connectionOperations.ContainsKey(pair.Provider);

    public void SetMedia(IEnumerable<PairSnapshot> pairs, MediaKind kind, bool disabled)
    {
        lock (SyncRoot)
        {
            if (stopping || kind is not (MediaKind.Animations or MediaKind.Sounds or MediaKind.Vfx)) return;
            // Queue a batch so large global changes use the same bounded request budget as pauses.
            foreach (var pair in pairs.DistinctBy(p => p.Key))
            {
                if (!CanChangeMedia(pair)) continue;
                if (pair.Media.Disabled.HasFlag(kind) == disabled) continue;
                queuedMedia[pair.Key] = new(pair.Pair, kind, disabled, pair.Media.Disabled.HasFlag(kind));
                mediaErrors.Remove(pair.Key);
            }
            RefreshSoon();
        }
    }

    private void StartQueuedMedia()
    {
        foreach (var (key, request) in queuedMedia.ToArray())
        {
            if (operations.Count + mediaOperations.Count >= MaxConcurrentOperations) break;
            if (operations.ContainsKey(key) || mediaOperations.ContainsKey(key)) continue;
            var pair = Pairs.FirstOrDefault(p => p.Key == key);
            if (pair != null && configuration.OwnedPauses.GetValueOrDefault(key)?.MediaChange != null) continue;
            if (pair != null && connectionOperations.ContainsKey(pair.Provider)) continue;
            queuedMedia.Remove(key);
            try
            {
                if (pair == null || !ReferenceEquals(pair.Pair, request.Pair))
                    throw new InvalidOperationException("The pair or sync connection changed. Reopen Media and try again.");
                if (!pair.Media.CanEdit) throw new NotSupportedException(pair.Media.Description);
                if (pair.Media.Disabled.HasFlag(request.Kind) != request.Before)
                    throw new InvalidOperationException("This media permission changed in the sync plugin. Check its current state and try again.");
                var after = pair.Adapter.MediaPermissionsAfter(pair, request.Kind, request.Disabled);
                var deadline = DateTime.UtcNow.AddSeconds(20);
                if (configuration.OwnedPauses.TryGetValue(key, out var owned) && !owned.LocalHold)
                {
                    if (pair.PauseReason != owned.OriginalPauseReason
                        || pair.Permissions != owned.PausedPermissions && pair.Permissions != owned.OriginalPermissions)
                        throw new InvalidOperationException("The pair's pause changed. Wait for management to update before changing media.");
                    // Save before sending. Restoration must keep the new media choice and Sticky flag.
                    owned.MediaChange = new()
                    {
                        Before = pair.Media.Permissions, After = after,
                        OriginalAfter = pair.Adapter.MediaPermissionsAfter(pair, request.Kind, request.Disabled, owned.OriginalPermissions),
                        Deadline = deadline,
                    };
                    configuration.Save();
                }
                mediaOperations[key] = new(pair.Adapter.SetMedia(pair, request.Kind, request.Disabled), after, deadline);
            }
            catch (Exception exception)
            {
                // Retain the saved alternatives if a native invocation may already have sent the request.
                if (exception is not System.Reflection.TargetInvocationException
                    && configuration.OwnedPauses.TryGetValue(key, out var owned))
                {
                    owned.MediaChange = null;
                    configuration.Save();
                }
                RecordMediaError(key, exception);
            }
        }
    }

    private void ReconcileMediaChange(PairSnapshot pair, OwnedPause owned)
    {
        if (owned.MediaChange is not { } change) return;
        if (pair.Media.Permissions == change.After)
        {
            owned.OriginalPermissions = change.OriginalAfter;
            // Compute the paused version with this API, rather than copying provider flag values.
            owned.PausedPermissions = pair.Adapter.PausedPermissionsFrom(pair, change.After);
            owned.MediaChange = null;
            configuration.Save();
        }
        else if (pair.Media.Permissions != change.Before || DateTime.UtcNow >= change.Deadline)
        {
            owned.MediaChange = null;
            configuration.Save();
        }
    }

    private void FinishMediaOperations()
    {
        foreach (var (key, operation) in mediaOperations.ToArray())
        {
            var pair = Pairs.FirstOrDefault(p => p.Key == key);
            if (!operation.Task.IsCompleted)
            {
                if (DateTime.UtcNow >= operation.Deadline && !mediaErrors.ContainsKey(key))
                    RecordMediaError(key, new TimeoutException("The media request is still running. Further changes wait for it to finish."));
                continue;
            }
            var failed = operation.Task.IsFaulted || operation.Task.IsCanceled;
            var cause = operation.Task.Exception?.GetBaseException(); // Observe native task exceptions even if the state arrived.
            if (pair?.Media.Permissions == operation.After)
            {
                if (configuration.OwnedPauses.TryGetValue(key, out var owned)) ReconcileMediaChange(pair, owned);
                mediaOperations.Remove(key);
                mediaErrors.Remove(key);
                continue;
            }
            if (!failed && DateTime.UtcNow < operation.Deadline) continue;
            mediaOperations.Remove(key);
            if (pair != null && configuration.OwnedPauses.TryGetValue(key, out var unchanged))
            {
                // A failed request can still have a delayed callback. Keep alternatives until its deadline.
                ReconcileMediaChange(pair, unchanged);
            }
            RecordMediaError(key, cause ?? (failed ? new OperationCanceledException("The media request was canceled.")
                : new TimeoutException("The sync did not confirm the media change. Check its permissions before trying again.")));
        }
    }

    private void RecordMediaError(string key, Exception exception)
    {
        var cause = ReflectionAccess.Unwrap(exception);
        mediaErrors[key] = cause.Message;
        Plugin.Log.Warning(cause, "Could not change pair media permissions.");
    }

    private void UpdateAudioHistory()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in Pairs)
        {
            if (pair.Audio.Playing && !string.IsNullOrEmpty(pair.Ident)) audioHistory[pair.Key] = (pair.Ident, now);
            if (audioHistory.TryGetValue(pair.Key, out var previous) && previous.Ident == pair.Ident)
                pair.Audio = pair.Audio with { LastPlayedAt = previous.PlayedAt };
        }
        foreach (var key in audioHistory.Where(p => now - p.Value.PlayedAt > TimeSpan.FromSeconds(60)).Select(p => p.Key).ToArray())
            audioHistory.Remove(key);
    }

    private sealed record QueuedMedia(object Pair, MediaKind Kind, bool Disabled, bool Before);
    private sealed record MediaOperation(Task Task, string After, DateTime Deadline);
}
