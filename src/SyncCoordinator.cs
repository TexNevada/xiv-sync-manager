using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;

namespace XivSyncManager;

public sealed partial class SyncCoordinator
{
    private readonly Configuration configuration;
    private readonly ReflectionSyncAdapter[] adapters = Enum.GetValues<SyncProvider>().Select(p => new ReflectionSyncAdapter(p)).ToArray();
    private readonly Dictionary<string, PendingOperation> operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> retryAfter = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PairSnapshot> localHolds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> errors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> profileErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<SyncProvider, PendingConnection> connectionOperations = [];
    private readonly Dictionary<SyncProvider, string> connectionErrors = [];
    private readonly DuplicatePolicy duplicatePolicy = new();
    private HashSet<string> automaticPauses = new(StringComparer.Ordinal);
    private DateTime nextRefresh;
    private IEnumerator<string>? refresh;
    private readonly PerformanceDiagnostics updateDiagnostics = new();
    private readonly Dictionary<SyncProvider, (bool Available, bool Connected, string Description)> loggedProviderStates = [];
    private bool rebuildDuplicateCache = true;
    private DateTime nextIndexActivitySave;
    private DateTime nextIndexExpiryCheck;
    private bool indexActivityDirty;
    private HashSet<string> onlineIndexedCharacters = new(StringComparer.Ordinal);
    private HashSet<string> visibleCharacters = new(StringComparer.Ordinal);
    private bool stopping;
    private const int MaxConcurrentOperations = 8;
    private static readonly TimeSpan RefreshFrameBudget = TimeSpan.FromMilliseconds(2);
    private IReadOnlyList<PairSnapshot> pairs = [];
    private HashSet<string> pairKeys = new(StringComparer.Ordinal);
    private IReadOnlyList<DuplicateCharacter> characters = [];
    private bool charactersDirty = true;

    public object SyncRoot { get; } = new();
    public IReadOnlyList<PairSnapshot> Pairs
    {
        get => pairs;
        private set
        {
            pairs = value;
            pairKeys = value.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            charactersDirty = true;
        }
    }
    public IReadOnlyList<ProviderStatus> Providers => adapters.Select(a => a.Status).ToArray();
    public Configuration Configuration => configuration;
    public int UnresolvedPauses => configuration.OwnedPauses.Keys.Count(k => !pairKeys.Contains(k));
    public IReadOnlyList<DuplicateCharacter> Characters
    {
        get
        {
            if (charactersDirty) RebuildCharacterView();
            return characters;
        }
    }

    private void RebuildCharacterView()
    {
        // Keep captured LINQ variables in the rebuild method so the cached getter does
        // not allocate a closure on every draw, even when it returns early.
        var byCharacter = Pairs.Where(p => p.CharacterIdentity != null)
            .ToLookup(p => p.CharacterIdentity!, StringComparer.Ordinal);
        characters = configuration.DuplicateCharacters.Select(entry =>
        {
            var identified = byCharacter[entry.Key].ToArray();
            return new DuplicateCharacter(entry.Key, entry.Value.DisplayName,
                visibleCharacters.Contains(entry.Key) || identified.Any(p => p.Online || p.Visible),
                entry.Value.Routes.Values.Concat(identified.Select(p => p.Provider)).Distinct()
                    .OrderBy(p => configuration.Priority.IndexOf(p)).ToArray(), identified)
            {
                Nearby = visibleCharacters.Contains(entry.Key),
            };
        }).OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        charactersDirty = false;
    }

    public SyncCoordinator(Configuration configuration)
    {
        this.configuration = configuration;
        // An interrupted restoration must finish even when automatic management is enabled again.
        foreach (var (key, owned) in configuration.OwnedPauses)
        {
            RememberUnconfirmedPause(key, owned);
            owned.RestoreRequested = true;
        }
        configuration.Save();
    }

    public void RefreshSoon()
    {
        charactersDirty = true;
        refresh?.Dispose();
        refresh = null;
        nextRefresh = DateTime.MinValue;
    }

    internal void Update(IFramework framework)
    {
        // Cleanup may hold the gate from another thread. Wait for the next frame instead of
        // blocking the game's framework thread behind a configuration save or shutdown work.
        if (!Monitor.TryEnter(SyncRoot)) return;
        try
        {
            if (stopping || refresh == null && DateTime.UtcNow < nextRefresh) return;
            var started = Stopwatch.GetTimestamp();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            var longestStep = TimeSpan.Zero;
            var longestStepName = "Start refresh";
            var steps = 0;
            try
            {
                if (refresh == null)
                {
                    nextRefresh = DateTime.UtcNow.AddSeconds(1);
                    refresh = Refresh().GetEnumerator();
                }
                do
                {
                    var stepStarted = Stopwatch.GetTimestamp();
                    var more = refresh.MoveNext();
                    var stepElapsed = Stopwatch.GetElapsedTime(stepStarted);
                    if (stepElapsed > longestStep)
                    {
                        longestStep = stepElapsed;
                        longestStepName = more ? refresh.Current : "Finish refresh";
                    }
                    if (!more)
                    {
                        refresh.Dispose();
                        refresh = null;
                        break;
                    }
                    steps++;
                }
                while (steps < 32 && Stopwatch.GetElapsedTime(started) < RefreshFrameBudget);
            }
            catch (Exception exception)
            {
                refresh?.Dispose();
                refresh = null;
                nextRefresh = DateTime.UtcNow.AddSeconds(1);
                Plugin.Log.Error(exception, "[Refresh] Could not refresh sync management.");
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                if (updateDiagnostics.ShouldReport(elapsed))
                    PerformanceDiagnostics.Report("Framework update", elapsed, GC.GetAllocatedBytesForCurrentThread() - allocated,
                        GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2,
                        $"Longest step: {longestStepName} ({longestStep.TotalMilliseconds:F2} ms); steps: {steps}; " +
                        $"pairs: {Pairs.Count}; indexed duplicates: {configuration.DuplicateCharacters.Count}. The 2 ms budget cannot pre-empt a running step.");
            }
        }
        finally { Monitor.Exit(SyncRoot); }
    }

    private IEnumerable<string> Refresh()
    {
        var plugins = Plugin.PluginInterface.InstalledPlugins.ToArray();
        var pairs = new List<PairSnapshot>();
        foreach (var adapter in adapters)
        {
            var providerPairs = new List<PairSnapshot>();
            foreach (var step in adapter.RefreshIncrementally(plugins, providerPairs)) yield return step;
            LogProviderState(adapter.Status);
            pairs.AddRange(providerPairs);
            yield return "Combine provider pairs";
        }
        Pairs = pairs.ToArray();
        FinishConnections();
        yield return "Finish connection changes";
        AssociateCharacters();
        yield return "Associate characters and update index";
        FinishOperations();
        yield return "Confirm pause and resume changes";
        UpdateAudioHistory();
        yield return "Update recent audio";
        FinishMediaOperations();
        yield return "Confirm media changes";
        ObserveExternalChanges();
        yield return "Observe external permission changes";
        automaticPauses = duplicatePolicy.GetAutomaticPauses(Pairs, configuration, Providers, visibleCharacters);
        yield return "Select automatic pauses";

        // Restore the selected fallback before requesting new pauses on its alternatives.
        foreach (var pair in Pairs)
        {
            if (connectionOperations.ContainsKey(pair.Provider)) continue;
            var wantsPause = configuration.IsManuallyPaused(pair) || automaticPauses.Contains(pair.Key);
            if (configuration.OwnedPauses.TryGetValue(pair.Key, out var owned)
                && (owned.RestoreRequested || !wantsPause))
            {
                try { StartRestore(pair, owned); }
                catch (Exception exception) { RecordError(pair.Key, exception); }
                yield return "Request pause restoration";
            }
        }
        foreach (var pair in Pairs)
        {
            if (connectionOperations.ContainsKey(pair.Provider)) continue;
            if (!configuration.IsManuallyPaused(pair) && !automaticPauses.Contains(pair.Key)) continue;
            try { StartPause(pair); }
            catch (Exception exception) { RecordError(pair.Key, exception); }
            yield return "Request character pause";
        }
        // Restore/fail over before spending the remaining request budget on a large media batch.
        StartQueuedMedia();
        yield return "Start queued media changes";
    }

    private void LogProviderState(ProviderStatus status)
    {
        var state = (status.Available, status.Connected, status.Description);
        if (loggedProviderStates.TryGetValue(status.Provider, out var previous) && previous == state) return;
        loggedProviderStates[status.Provider] = state;
        if (status.Description.StartsWith("Integration unavailable", StringComparison.Ordinal))
            Plugin.Log.Warning("[Connections] {Provider}: {Description}", status.Provider.DisplayName(), status.Description);
        else Plugin.Log.Info("[Connections] {Provider}: {Description}", status.Provider.DisplayName(), status.Description);
    }

    private void AssociateCharacters()
    {
        charactersDirty = true;
        var players = Plugin.ObjectTable.OfType<IPlayerCharacter>()
            .Where(p => p.Address != nint.Zero && p.HomeWorld.RowId != 0).GroupBy(p => p.Address).ToDictionary(g => g.Key, g => g.First());
        visibleCharacters = players.Values.Select(p => $"{p.Name.TextValue}@{p.HomeWorld.RowId}").ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var pair in Pairs)
        {
            if (pair.Visible && pair.Address != nint.Zero && players.TryGetValue(pair.Address, out var player)
                && pair.PlayerName == player.Name.TextValue)
            {
                var name = player.Name.TextValue;
                var identity = $"{name}@{player.HomeWorld.RowId}";
                var display = $"{name}@{player.HomeWorld.Value.Name}";
                pair.CharacterIdentity = identity;
                pair.CharacterName = display;
                var hasObservation = configuration.ObservedCharacters.TryGetValue(pair.Key, out var known);
                // With expiry enabled, require Online to add a new association: Nearby alone must
                // not immediately rebuild an expired entry and restart its tracking baseline.
                if ((!hasObservation || known!.Identity != identity || known.Ident != pair.Ident || known.DisplayName != display)
                    && (pair.Online || configuration.StaleIndexRetention == IndexRetention.None
                        || hasObservation && known!.Identity == identity))
                {
                    // Even a route without an identifier can be remembered for display. Reuse for actions
                    // below still requires a matching nonempty identifier or our own restoration record.
                    configuration.ObservedCharacters[pair.Key] = new() { Identity = identity, DisplayName = display, Ident = pair.Ident };
                    changed = true;
                }
            }
            else if (configuration.ObservedCharacters.TryGetValue(pair.Key, out var known)
                     && !string.IsNullOrEmpty(pair.Ident) && pair.Ident == known.Ident)
            {
                pair.CharacterIdentity = known.Identity;
                pair.CharacterName = known.DisplayName;
            }
            else if (string.IsNullOrEmpty(pair.Ident) && configuration.OwnedPauses.TryGetValue(pair.Key, out var owned)
                     && !string.IsNullOrEmpty(owned.CharacterIdentity))
            {
                // Pausing can destroy the provider's handler. Retain the decision's identity until restoration.
                pair.CharacterIdentity = owned.CharacterIdentity;
                pair.CharacterName = configuration.ObservedCharacters.GetValueOrDefault(pair.Key)?.DisplayName;
            }
        }
        // Build history from verified observations, including those made before the duplicate cache existed.
        // A route that identifies a different character is moved rather than blindly reused for that character.
        if (changed || rebuildDuplicateCache)
        {
            changed |= UpdateDuplicateCache();
            rebuildDuplicateCache = false;
        }
        var now = DateTime.UtcNow;
        changed |= UpdateIndexActivity(now);
        if (now >= nextIndexExpiryCheck)
        {
            changed |= ExpireStaleIndex(now);
            nextIndexExpiryCheck = now.AddMinutes(1);
        }
        if (changed || indexActivityDirty && now >= nextIndexActivitySave)
        {
            configuration.SaveBackground();
            indexActivityDirty = false;
            nextIndexActivitySave = now.AddMinutes(1);
        }
    }

    private bool UpdateIndexActivity(DateTime now)
    {
        var update = CharacterIndexRetention.UpdateActivity(configuration.CharacterIndexActivity,
            configuration.ObservedCharacters.Values.Select(c => c.Identity).Concat(configuration.DuplicateCharacters.Keys),
            Pairs.Where(p => p.CharacterIdentity != null).Select(p => (p.CharacterIdentity!, p.Online)), now);
        indexActivityDirty |= update.ActivityChanged;
        // Save transitions immediately so going offline does not lose the last online observation.
        var changed = update.IndexChanged || !update.Online.SetEquals(onlineIndexedCharacters);
        onlineIndexedCharacters = update.Online;
        return changed;
    }

    private bool ExpireStaleIndex(DateTime now)
    {
        var expired = CharacterIndexRetention.RemoveExpired(configuration.CharacterIndexActivity,
            configuration.ObservedCharacters, configuration.DuplicateCharacters, configuration.StaleIndexRetention, now);
        if (expired.Count == 0) return false;
        foreach (var identity in expired) duplicatePolicy.ForgetSelection(identity);
        // Keep responsibility for existing pauses until the service confirms their restoration.
        foreach (var owned in configuration.OwnedPauses.Values.Where(o => expired.Contains(o.CharacterIdentity)))
            owned.RestoreRequested = true;
        foreach (var pair in Pairs.Where(p => p.CharacterIdentity != null && expired.Contains(p.CharacterIdentity)))
        {
            pair.CharacterIdentity = null;
            pair.CharacterName = null;
        }
        return true;
    }

    private bool UpdateDuplicateCache()
    {
        var changed = false;
        var observations = configuration.ObservedCharacters
            .Select(entry => (entry.Key, Character: entry.Value, Provider: ParseProvider(entry.Key)))
            .Where(entry => entry.Provider != null && !string.IsNullOrEmpty(entry.Character.Identity))
            .GroupBy(entry => entry.Character.Identity, StringComparer.Ordinal);
        foreach (var group in observations)
        {
            var routes = group.ToDictionary(entry => entry.Key, entry => entry.Provider!.Value, StringComparer.Ordinal);
            if (routes.Values.Distinct().Count() < 2) continue;
            var display = group.First().Character.DisplayName;
            if (!configuration.DuplicateCharacters.TryGetValue(group.Key, out var cached)
                || cached.DisplayName != display || cached.Routes.Count != routes.Count
                || routes.Any(route => !cached.Routes.TryGetValue(route.Key, out var provider) || provider != route.Value))
            {
                configuration.DuplicateCharacters[group.Key] = new() { DisplayName = display, Routes = routes };
                changed = true;
            }
        }
        foreach (var (identity, cached) in configuration.DuplicateCharacters.ToArray())
        {
            foreach (var key in cached.Routes.Keys.ToArray())
                if (configuration.ObservedCharacters.TryGetValue(key, out var observed) && observed.Identity != identity)
                {
                    cached.Routes.Remove(key);
                    changed = true;
                }
            if (cached.Routes.Values.Distinct().Count() < 2)
            {
                configuration.DuplicateCharacters.Remove(identity);
                changed = true;
            }
        }
        return changed;
    }

    private static SyncProvider? ParseProvider(string pairKey)
    {
        var separator = pairKey.IndexOf('|');
        return separator > 0 && Enum.TryParse<SyncProvider>(pairKey[..separator], out var provider)
            && Enum.IsDefined(provider) ? provider : null;
    }

    private void ObserveExternalChanges()
    {
        foreach (var pair in Pairs)
        {
            ReconcileLateMediaChanges(pair);
            ReconcileLatePauses(pair);
            if (operations.ContainsKey(pair.Key) || mediaOperations.ContainsKey(pair.Key)
                || !configuration.OwnedPauses.TryGetValue(pair.Key, out var owned)) continue;
            ReconcileMediaChange(pair, owned);
            if (owned.MediaChange != null) continue;
            if (owned.LocalHold) continue;
            if (owned.RestoreRequested && pair.Permissions == owned.OriginalPermissions && pair.PauseReason == owned.OriginalPauseReason)
            {
                // Send and confirm restoration even if a previous request's pause callback has not arrived yet.
                continue;
            }
            if (pair.Permissions == owned.PausedPermissions && pair.PauseReason == owned.OriginalPauseReason)
            {
                if (!owned.Confirmed) { owned.Confirmed = true; configuration.Save(); }
                continue;
            }
            if (!owned.Confirmed && pair.Permissions == owned.OriginalPermissions && pair.PauseReason == owned.OriginalPauseReason) continue;

            // Never overwrite permission or pause-reason changes made in the other plugin.
            configuration.OwnedPauses.Remove(pair.Key);
            configuration.ManualPauses.Remove(pair.Key);
            configuration.AutomaticExceptions.Add(pair.Key);
            errors[pair.Key] = "Changed in the sync plugin; automatic changes are suspended for this pair.";
            configuration.Save();
        }
    }

    private bool CanStart(PairSnapshot pair) => !operations.ContainsKey(pair.Key)
        && !mediaOperations.ContainsKey(pair.Key)
        && configuration.OwnedPauses.GetValueOrDefault(pair.Key)?.MediaChange == null
        && (stopping || operations.Count + mediaOperations.Count < MaxConcurrentOperations)
        && (stopping || !retryAfter.TryGetValue(pair.Key, out var until) || DateTime.UtcNow >= until);

    private void StartPause(PairSnapshot pair)
    {
        if (!CanStart(pair)) return;
        var created = false;
        if (configuration.OwnedPauses.TryGetValue(pair.Key, out var owned))
        {
            if (owned.RestoreRequested || (owned.LocalHold ? pair.ManagerFullyHeld : pair.Permissions == owned.PausedPermissions)) return;
        }
        else
        {
            if (pair.OwnPaused || pair.ExternalHold) return;
            owned = pair.Adapter.PreparePause(pair);
            configuration.OwnedPauses[pair.Key] = owned;
            created = true;
        }
        var previousChange = owned.PauseChange;
        if (!owned.LocalHold) owned.PauseChange = new()
        {
            CharacterIdentity = owned.CharacterIdentity, OriginalPermissions = owned.OriginalPermissions,
            PausedPermissions = owned.PausedPermissions, PauseReason = owned.OriginalPauseReason,
        };
        // An earlier save may have failed after inserting the in-memory record. Every
        // native pause attempt must persist its restoration record successfully first.
        try { configuration.Save(); }
        catch
        {
            // No native request was sent. Do not let a later external pause matching
            // our prepared fingerprint be mistaken for a pause we actually requested.
            if (created) configuration.OwnedPauses.Remove(pair.Key);
            else owned.PauseChange = previousChange;
            throw;
        }
        StartOperation(pair, true);
    }

    private void StartRestore(PairSnapshot pair, OwnedPause owned)
    {
        if (!CanStart(pair)) return;
        if (owned.LocalHold != pair.Adapter.LocalHolds)
        {
            RecordError(pair.Key, new NotSupportedException("The pause integration changed since this record was saved. Restore the pause in its original sync plugin; the saved record has been retained."));
            return;
        }
        if (!owned.LocalHold && (pair.PauseReason != owned.OriginalPauseReason
            || (pair.Permissions != owned.PausedPermissions && pair.Permissions != owned.OriginalPermissions))) return;
        owned.RestoreRequested = true;
        configuration.Save();
        StartOperation(pair, false);
    }

    private void StartOperation(PairSnapshot pair, bool paused)
    {
        try
        {
            if (pair.Adapter.LocalHolds) localHolds[pair.Key] = pair;
            var task = pair.Adapter.SetPaused(pair, paused);
            operations[pair.Key] = new(task, paused, DateTime.UtcNow.AddSeconds(20));
            errors.Remove(pair.Key);
        }
        catch (Exception exception)
        {
            if (paused && configuration.OwnedPauses.TryGetValue(pair.Key, out var uncertain) && !uncertain.LocalHold)
            {
                if (exception is System.Reflection.TargetInvocationException) RememberUnconfirmedPause(pair.Key, uncertain);
                else
                {
                    uncertain.PauseChange = null;
                    if (!uncertain.Confirmed) configuration.OwnedPauses.Remove(pair.Key);
                }
                configuration.Save();
            }
            RecordError(pair.Key, exception);
        }
    }

    private void FinishOperations()
    {
        foreach (var (key, operation) in operations.ToArray())
        {
            if (!operation.Task.IsCompleted)
            {
                if (DateTime.UtcNow >= operation.Deadline && !errors.ContainsKey(key))
                    RecordError(key, new TimeoutException("The sync request is still running. Further changes wait for it to finish."));
                continue;
            }
            if (operation.Task.IsFaulted || operation.Task.IsCanceled)
            {
                RetainUnconfirmedOperation(key, operation, operation.Task.Exception?.GetBaseException()
                    ?? new OperationCanceledException("Pause operation was canceled."));
                continue;
            }

            var pair = Pairs.FirstOrDefault(p => p.Key == key);
            if (pair != null && configuration.OwnedPauses.TryGetValue(key, out var owned))
            {
                var confirmed = owned.LocalHold ? (operation.Paused ? pair.ManagerFullyHeld : !pair.ManagerHeld)
                    : pair.Permissions == (operation.Paused ? owned.PausedPermissions : owned.OriginalPermissions);
                if (confirmed)
                {
                    operations.Remove(key);
                    if (operation.Paused)
                    {
                        owned.Confirmed = true;
                        owned.PauseChange = null;
                        ReapplyRemainingRoute(pair);
                    }
                    else
                    {
                        configuration.OwnedPauses.Remove(key);
                        localHolds.Remove(key);
                        if (!stopping && pair.Online && pair.Visible && !pair.OwnPaused && !pair.OtherPaused
                            && !pair.ManagerHeld && !pair.ExternalHold && !configuration.IsManuallyPaused(pair))
                            TryReapply(pair);
                    }
                    configuration.Save();
                    continue;
                }
            }
            if (DateTime.UtcNow < operation.Deadline) continue;
            RetainUnconfirmedOperation(key, operation,
                new TimeoutException("The sync plugin did not confirm the pause change. The saved restoration record was retained."));
        }
    }

    private void RetainUnconfirmedOperation(string key, PendingOperation operation, Exception exception)
    {
        operations.Remove(key);
        try
        {
            if (operation.Paused && configuration.OwnedPauses.TryGetValue(key, out var owned))
            {
                RememberUnconfirmedPause(key, owned);
                configuration.Save();
            }
        }
        // Observe native task failures and retain their retry delay even if this save fails.
        finally { RecordError(key, exception); }
    }

    private void ReapplyRemainingRoute(PairSnapshot suppressed)
    {
        if (suppressed.CharacterIdentity == null) return;
        var hasPreference = configuration.PreferredProviders.TryGetValue(suppressed.CharacterIdentity, out var preferred);
        var remaining = Pairs.Where(p => p.CharacterIdentity == suppressed.CharacterIdentity && p.Provider != suppressed.Provider
                && p.Online && p.Visible && !p.OwnPaused && !p.OtherPaused && !p.ManagerHeld && !p.ExternalHold
                && !configuration.IsManuallyPaused(p))
            .OrderBy(p => hasPreference && p.Provider == preferred ? -1 : configuration.Priority.IndexOf(p.Provider)).FirstOrDefault();
        if (remaining == null) return;
        TryReapply(remaining);
    }

    private static void TryReapply(PairSnapshot pair)
    {
        try { pair.Adapter.Reapply(pair); }
        catch (Exception exception) { Plugin.Log.Warning(ReflectionAccess.Unwrap(exception), "[Management] Could not reapply the remaining sync's appearance."); }
    }

    private void RecordError(string key, Exception exception)
    {
        var cause = ReflectionAccess.Unwrap(exception);
        errors[key] = cause.Message;
        retryAfter[key] = DateTime.UtcNow.AddSeconds(30);
        Plugin.Log.Warning(cause, "[Management] Could not change {Provider} pair state. Retrying after 30 seconds.",
            ParseProvider(key)?.DisplayName() ?? "unknown sync");
    }

    public string? ErrorFor(PairSnapshot pair) => errors.GetValueOrDefault(pair.Key);

    public string? ProfileErrorFor(PairSnapshot pair) => profileErrors.GetValueOrDefault(pair.Key);

    public string? OpenProfile(PairSnapshot pair)
    {
        lock (SyncRoot)
        {
            try
            {
                if (stopping || !Pairs.Contains(pair))
                    throw new InvalidOperationException("This pair changed. Wait for the next update before opening its profile.");
                if (connectionOperations.ContainsKey(pair.Provider))
                    throw new InvalidOperationException("Wait for this sync's connection change to finish before opening a profile.");
                pair.Adapter.OpenProfile(pair);
                profileErrors.Remove(pair.Key);
                RefreshSoon();
                return null;
            }
            catch (Exception exception)
            {
                var cause = ReflectionAccess.Unwrap(exception);
                profileErrors[pair.Key] = cause.Message;
                Plugin.Log.Warning(cause, "[Profiles] Could not open {Provider}'s profile viewer.", pair.Provider.DisplayName());
                RefreshSoon();
                return cause.Message;
            }
        }
    }

    public bool IsPending(PairSnapshot pair) => operations.ContainsKey(pair.Key)
        || mediaOperations.ContainsKey(pair.Key) || queuedMedia.ContainsKey(pair.Key)
        || configuration.OwnedPauses.GetValueOrDefault(pair.Key)?.MediaChange != null;
    public bool IsOwned(PairSnapshot pair) => configuration.OwnedPauses.ContainsKey(pair.Key);
    public bool IsManual(PairSnapshot pair) => configuration.IsManuallyPaused(pair);
    public bool IsAutomatic(PairSnapshot pair) => automaticPauses.Contains(pair.Key);
    public bool IsWaitingForReconnect(string identity) => duplicatePolicy.IsWaitingForReconnect(identity);
    public bool IsKeepingFallback(string identity) => duplicatePolicy.IsKeepingFallback(identity);
    public string? ConnectionProgressFor(SyncProvider provider)
    {
        if (!connectionOperations.TryGetValue(provider, out var operation)) return null;
        return operation.Connect ? "Reconnecting…" : "Disconnecting…";
    }
    public string? ConnectionErrorFor(SyncProvider provider) => connectionErrors.GetValueOrDefault(provider);

    public void SetProviderConnected(SyncProvider provider, bool connected)
    {
        lock (SyncRoot)
        {
            if (stopping || connectionOperations.ContainsKey(provider)) return;
            try
            {
                var adapter = adapters.First(a => a.Provider == provider);
                if (connected ? !adapter.Status.CanReconnect : !adapter.Status.CanDisconnect)
                    throw new InvalidOperationException("Connection controls are unavailable for this sync's current state.");
                connectionOperations[provider] = new(adapter, adapter.SetConnected(connected), connected,
                    DateTime.UtcNow.AddSeconds(connected ? 30 : 20));
                connectionErrors.Remove(provider);
                Plugin.Log.Info("[Connections] {Action} requested for {Provider}.", connected ? "Reconnect" : "Disconnect", provider.DisplayName());
            }
            catch (Exception exception) { RecordConnectionError(provider, exception); }
            RefreshSoon();
        }
    }

    private void FinishConnections()
    {
        foreach (var (provider, operation) in connectionOperations.ToArray())
        {
            if (operation.Task.IsFaulted || operation.Task.IsCanceled)
            {
                connectionOperations.Remove(provider);
                RecordConnectionError(provider, operation.Task.Exception?.GetBaseException()
                    ?? new OperationCanceledException("The sync plugin canceled the connection request."));
                continue;
            }
            try
            {
                if (operation.Task.IsCompleted && operation.Adapter.IsConnectionOpen == operation.Connect)
                {
                    connectionOperations.Remove(provider);
                    connectionErrors.Remove(provider);
                    Plugin.Log.Info("[Connections] {Provider} confirmed {State}.", provider.DisplayName(), operation.Connect ? "reconnection" : "disconnection");
                    continue;
                }
                if (DateTime.UtcNow < operation.Deadline) continue;
                if (operation.Task.IsCompleted)
                {
                    connectionOperations.Remove(provider);
                    RecordConnectionError(provider, new TimeoutException(operation.Connect
                        ? "The sync plugin did not confirm reconnection. Check its server status and account setup."
                        : "The sync plugin did not confirm disconnection. Check its connection status."));
                }
                else if (!connectionErrors.ContainsKey(provider))
                    RecordConnectionError(provider, new TimeoutException("The connection request is still running. Further connection requests wait for it to finish."));
            }
            catch (Exception exception)
            {
                // A running native request must finish before another connection request can be submitted.
                if (operation.Task.IsCompleted) connectionOperations.Remove(provider);
                if (!connectionErrors.ContainsKey(provider)) RecordConnectionError(provider, exception);
            }
        }
    }

    private void RecordConnectionError(SyncProvider provider, Exception exception)
    {
        var cause = ReflectionAccess.Unwrap(exception);
        connectionErrors[provider] = cause.Message;
        Plugin.Log.Warning(cause, "[Connections] Could not change the connection for {Provider}.", provider.DisplayName());
    }

    public void SetKeepFallbackUntilReentry(bool enabled)
    {
        configuration.KeepFallbackUntilReentry = enabled;
        duplicatePolicy.ResetSelections();
        configuration.Save();
        RefreshSoon();
    }

    public void SetStaleIndexRetention(IndexRetention retention)
    {
        if (!Enum.IsDefined(retention)) return;
        configuration.StaleIndexRetention = retention;
        Plugin.Log.Info("[Configuration] Stale index retention changed to {Retention}.", retention);
        nextIndexExpiryCheck = DateTime.MinValue;
        configuration.Save();
        RefreshSoon();
    }

    public void SetAutomaticManagement(bool enabled)
    {
        Plugin.Log.Info("[Management] Automatic management {State}.", enabled ? "enabled" : "disabled");
        configuration.AutomaticManagement = enabled;
        duplicatePolicy.ResetSelections();
        if (!enabled)
            foreach (var (key, owned) in configuration.OwnedPauses)
                if (configuration.AutomaticExceptions.Contains(key)
                    || (!configuration.ManualPauses.Contains(key) && !configuration.CharacterPauses.Contains(owned.CharacterIdentity)))
                    owned.RestoreRequested = true;
        configuration.Save();
        RefreshSoon();
    }

    public void SetManualPause(PairSnapshot pair, bool paused)
    {
        if (pair.CharacterIdentity != null) duplicatePolicy.ForgetSelection(pair.CharacterIdentity);
        if (paused) configuration.ManualPauses.Add(pair.Key);
        else
        {
            configuration.ManualPauses.Remove(pair.Key);
            if (configuration.OwnedPauses.TryGetValue(pair.Key, out var owned)) owned.RestoreRequested = true;
            if (pair.CharacterIdentity != null) configuration.PreferredProviders[pair.CharacterIdentity] = pair.Provider;
        }
        retryAfter.Remove(pair.Key);
        configuration.Save();
        RefreshSoon();
    }

    public bool IsCharacterPaused(DuplicateCharacter character) => configuration.CharacterPauses.Contains(character.Identity)
        || character.Pairs.Any(p => configuration.ManualPauses.Contains(p.Key))
        || configuration.DuplicateCharacters[character.Identity].Routes.Keys.Any(configuration.ManualPauses.Contains);

    public void SetCharacterPaused(DuplicateCharacter character, bool paused)
    {
        Plugin.Log.Info("[Management] Character {Action} requested across {ProviderCount} identified sync plugins.",
            paused ? "pause" : "resume", character.Providers.Count);
        duplicatePolicy.ForgetSelection(character.Identity);
        if (paused) configuration.CharacterPauses.Add(character.Identity);
        else configuration.CharacterPauses.Remove(character.Identity);
        var keys = configuration.DuplicateCharacters[character.Identity].Routes.Keys
            .Concat(character.Pairs.Select(p => p.Key))
            .Concat(configuration.OwnedPauses.Where(entry => entry.Value.CharacterIdentity == character.Identity).Select(entry => entry.Key))
            .Distinct().ToArray();
        foreach (var key in keys)
        {
            // Migrate old individual choices to the character choice and retry explicitly requested changes.
            configuration.ManualPauses.Remove(key);
            if (paused) configuration.AutomaticExceptions.Remove(key);
            else if (configuration.OwnedPauses.TryGetValue(key, out var owned)) owned.RestoreRequested = true;
            retryAfter.Remove(key);
            errors.Remove(key);
        }
        configuration.Save();
        RefreshSoon();
    }

    public void SetPreferred(string identity, SyncProvider? provider)
    {
        duplicatePolicy.ForgetSelection(identity);
        if (provider == null) configuration.PreferredProviders.Remove(identity);
        else configuration.PreferredProviders[identity] = provider.Value;
        configuration.Save();
        RefreshSoon();
    }

    public void UseAutomaticRules(PairSnapshot pair)
    {
        configuration.AutomaticExceptions.Remove(pair.Key);
        errors.Remove(pair.Key);
        retryAfter.Remove(pair.Key);
        configuration.Save();
        RefreshSoon();
    }

    public void ClearCache()
    {
        lock (SyncRoot)
        {
            // Keep restoration records and running requests until the sync confirms release.
            // Service priority and preferred sync choices are preferences, not cached observations.
            foreach (var owned in configuration.OwnedPauses.Values) owned.RestoreRequested = true;
            configuration.ObservedCharacters.Clear();
            configuration.DuplicateCharacters.Clear();
            configuration.CharacterIndexActivity.Clear();
            onlineIndexedCharacters.Clear();
            indexActivityDirty = false;
            configuration.ManualPauses.Clear();
            configuration.CharacterPauses.Clear();
            configuration.AutomaticExceptions.Clear();
            errors.Clear();
            profileErrors.Clear();
            queuedMedia.Clear();
            mediaErrors.Clear();
            audioHistory.Clear();
            connectionErrors.Clear();
            retryAfter.Clear();
            automaticPauses.Clear();
            duplicatePolicy.ResetSelections();
            configuration.Save();
            RefreshSoon();
        }
    }

    public void MovePriority(int index, int offset)
    {
        var target = index + offset;
        if (target < 0 || target >= configuration.Priority.Count) return;
        (configuration.Priority[index], configuration.Priority[target]) = (configuration.Priority[target], configuration.Priority[index]);
        duplicatePolicy.ResetSelections();
        configuration.Save();
        RefreshSoon();
    }

    internal async Task StopAsync()
    {
        Task[] pending;
        lock (SyncRoot)
        {
            stopping = true;
            refresh?.Dispose();
            refresh = null;
            queuedMedia.Clear();
            foreach (var owned in configuration.OwnedPauses.Values) owned.RestoreRequested = true;
            ShutdownStep(configuration.Save, "save restoration intent");
            pending = operations.Values.Select(o => o.Task).Concat(connectionOperations.Values.Select(o => o.Task))
                .Concat(mediaOperations.Values.Select(o => o.Task)).ToArray();
        }
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception exception) { Plugin.Log.Warning(exception, "[Lifecycle] Some sync requests are unfinished; pause restoration records have been retained."); }

        await Plugin.Framework.RunOnFrameworkThread(() =>
        {
            lock (SyncRoot)
            {
                ShutdownStep(() => Pairs = adapters.SelectMany(a => a.Refresh(Plugin.PluginInterface.InstalledPlugins)).ToArray(), "refresh syncs");
                ShutdownStep(FinishConnections, "confirm connections");
                ShutdownStep(FinishOperations, "confirm pause changes");
                ShutdownStep(FinishMediaOperations, "confirm media changes");
                ShutdownStep(ObserveExternalChanges, "reconcile permissions");
                // Releasing our source-scoped holds is safe even if storage is unavailable.
                // Include reloaded records and tracked old pair instances, then isolate failures.
                var held = localHolds.Values.Concat(Pairs.Where(p => p.Adapter.LocalHolds
                    && configuration.OwnedPauses.GetValueOrDefault(p.Key)?.LocalHold == true))
                    .DistinctBy(p => p.Pair, ReferenceEqualityComparer.Instance).ToArray();
                foreach (var pair in held)
                {
                    try
                    {
                        pair.Adapter.ReleaseLocalHold(pair);
                        operations.Remove(pair.Key);
                    }
                    catch (Exception exception) { RecordError(pair.Key, exception); }
                }
                foreach (var pair in Pairs)
                    if (configuration.OwnedPauses.TryGetValue(pair.Key, out var owned) && !owned.LocalHold)
                        ShutdownStep(() => StartRestore(pair, owned), "restore a server pause");
                ShutdownStep(configuration.Save, "save cleanup results");
                pending = operations.Values.Select(o => o.Task).Concat(connectionOperations.Values.Select(o => o.Task))
                    .Concat(mediaOperations.Values.Select(o => o.Task)).ToArray();
            }
        }).ConfigureAwait(false);
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception exception) { Plugin.Log.Warning(exception, "[Lifecycle] Restoration will be retried when the manager is loaded again."); }
        // Records remain until the next load verifies the server state or released local holds.
    }

    private static void ShutdownStep(Action action, string step)
    {
        try { action(); }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "[Lifecycle] Could not {Step}; continuing cleanup and retaining restoration records.", step);
        }
    }

    private sealed record PendingOperation(Task Task, bool Paused, DateTime Deadline);
    private sealed record PendingConnection(ReflectionSyncAdapter Adapter, Task Task, bool Connect, DateTime Deadline);
}
