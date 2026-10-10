using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Configuration;
using Newtonsoft.Json;

namespace XivSyncManager;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    private readonly record struct SaveRequest(Configuration Snapshot, bool Background);
    private ConfigurationSaveQueue<SaveRequest>? saveQueue;
    private readonly PerformanceDiagnostics saveDiagnostics = new();
    internal static IReadOnlyList<SyncProvider> DefaultPriority { get; } =
        [SyncProvider.PlayerSync, SyncProvider.Lightless, SyncProvider.Snowcloak];

    public int Version { get; set; } = 3;
    public SyncTheme Theme { get; set; } = SyncTheme.RoseQuartz;
    public bool AutomaticManagement { get; set; }
    public bool KeepFallbackUntilReentry { get; set; } = true;
    public bool ShowIntegrationInfo { get; set; } = true;
    public bool HideReadyIntegrationInfo { get; set; } = true;
    public IndexRetention StaleIndexRetention { get; set; } = IndexRetention.Days30;
    public Dictionary<string, IndexedCharacterActivity> CharacterIndexActivity { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> SectionExpanded { get; set; } = new(StringComparer.Ordinal)
    {
        ["Nearby"] = true,
        ["Online"] = false,
        ["Offline"] = false,
    };
    // Replace defaults on load: reusing this list would append the saved order after the defaults.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<SyncProvider> Priority { get; set; } = [.. DefaultPriority];
    public Dictionary<string, SyncProvider> PreferredProviders { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> ManualPauses { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> AutomaticExceptions { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ObservedCharacter> ObservedCharacters { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, OwnedPause> OwnedPauses { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CachedDuplicateCharacter> DuplicateCharacters { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> CharacterPauses { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<LateMediaChange>> LateMediaChanges { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<LatePauseChange>> LatePauseChanges { get; set; } = new(StringComparer.Ordinal);

    public bool IsManuallyPaused(PairSnapshot pair) => !AutomaticExceptions.Contains(pair.Key)
        && (ManualPauses.Contains(pair.Key) || (pair.CharacterIdentity != null && CharacterPauses.Contains(pair.CharacterIdentity)));

    private ConfigurationSaveQueue<SaveRequest> SaveQueue => saveQueue ??= new(WriteSnapshot,
        exception => Plugin.Log.Error(exception, "[Configuration] Background save failed; live settings are retained and the next save will retry."));

    public void Save() => SaveQueue.SaveNow(new(CreateSnapshot(), Background: false));

    internal void SaveBackground() => SaveQueue.SaveBackground(new(CreateSnapshot(), Background: true));

    internal Task FlushSavesAsync() => saveQueue?.FlushAsync() ?? Task.CompletedTask;

    private void WriteSnapshot(SaveRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        try { Plugin.PluginInterface.SavePluginConfig(request.Snapshot); }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (saveDiagnostics.ShouldReport(elapsed))
                PerformanceDiagnostics.Report(request.Background ? "Background configuration save" : "Synchronous configuration save",
                    elapsed, GC.GetAllocatedBytesForCurrentThread() - allocated,
                    GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2,
                    "Serialization and file-write wait are included.");
        }
    }

    internal Configuration CreateSnapshot()
    {
        var copy = (Configuration)MemberwiseClone();
        copy.saveQueue = null;
        copy.Priority = [.. Priority];
        copy.PreferredProviders = new(PreferredProviders, StringComparer.Ordinal);
        copy.SectionExpanded = new(SectionExpanded, StringComparer.Ordinal);
        copy.ManualPauses = new(ManualPauses, StringComparer.Ordinal);
        copy.AutomaticExceptions = new(AutomaticExceptions, StringComparer.Ordinal);
        copy.CharacterPauses = new(CharacterPauses, StringComparer.Ordinal);
        copy.ObservedCharacters = ObservedCharacters.ToDictionary(e => e.Key, e => e.Value.Copy(), StringComparer.Ordinal);
        copy.DuplicateCharacters = DuplicateCharacters.ToDictionary(e => e.Key, e => e.Value.Copy(), StringComparer.Ordinal);
        copy.CharacterIndexActivity = CharacterIndexActivity.ToDictionary(e => e.Key, e => e.Value.Copy(), StringComparer.Ordinal);
        copy.OwnedPauses = OwnedPauses.ToDictionary(e => e.Key, e => e.Value.Copy(), StringComparer.Ordinal);
        copy.LateMediaChanges = LateMediaChanges.ToDictionary(e => e.Key, e => e.Value.Select(c => c.Copy()).ToList(), StringComparer.Ordinal);
        copy.LatePauseChanges = LatePauseChanges.ToDictionary(e => e.Key, e => e.Value.Select(c => c.Copy()).ToList(), StringComparer.Ordinal);
        return copy;
    }
}

// Saved before sending a request so a restart does not lose responsibility for a pause.
public sealed class OwnedPause
{
    internal OwnedPause Copy()
    {
        var copy = (OwnedPause)MemberwiseClone();
        copy.MediaChange = MediaChange?.Copy();
        copy.PauseChange = PauseChange?.Copy();
        return copy;
    }
    public string CharacterIdentity { get; set; } = string.Empty;
    public string OriginalPermissions { get; set; } = string.Empty;
    public string PausedPermissions { get; set; } = string.Empty;
    public string OriginalPauseReason { get; set; } = string.Empty;
    public bool LocalHold { get; set; }
    public bool Confirmed { get; set; }
    public bool RestoreRequested { get; set; }
    public PendingMediaChange? MediaChange { get; set; }
    public LatePauseChange? PauseChange { get; set; }
}

// Saved before a server pause, then retained separately if its result is uncertain.
public sealed class LatePauseChange
{
    internal LatePauseChange Copy() => (LatePauseChange)MemberwiseClone();
    public string CharacterIdentity { get; set; } = string.Empty;
    public string OriginalPermissions { get; set; } = string.Empty;
    public string PausedPermissions { get; set; } = string.Empty;
    public string PauseReason { get; set; } = string.Empty;
}

// Keep both permission versions until a media request is observed, including across a reload.
public sealed class PendingMediaChange
{
    internal PendingMediaChange Copy() => (PendingMediaChange)MemberwiseClone();
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public string OriginalAfter { get; set; } = string.Empty;
    public DateTime Deadline { get; set; }
}

// A deadline does not prove a server request was never applied. Keep its expected
// pause-bearing result even after restoration completes, so a late callback remains recoverable.
public sealed class LateMediaChange
{
    internal LateMediaChange Copy() => (LateMediaChange)MemberwiseClone();
    public string CharacterIdentity { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public string OriginalAfter { get; set; } = string.Empty;
    public string PausedAfter { get; set; } = string.Empty;
    public string PauseReason { get; set; } = string.Empty;
}
