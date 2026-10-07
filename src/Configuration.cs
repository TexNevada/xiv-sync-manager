using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Newtonsoft.Json;

namespace XivSyncManager;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    internal static IReadOnlyList<SyncProvider> DefaultPriority { get; } =
        [SyncProvider.PlayerSync, SyncProvider.Lightless, SyncProvider.Snowcloak];

    public int Version { get; set; } = 3;
    public SyncTheme Theme { get; set; } = SyncTheme.RoseQuartz;
    public bool AutomaticManagement { get; set; }
    public bool KeepFallbackUntilReentry { get; set; } = true;
    public bool ShowIntegrationInfo { get; set; } = true;
    public bool HideReadyIntegrationInfo { get; set; } = true;
    public Dictionary<string, bool> SectionExpanded { get; set; } = new(StringComparer.Ordinal)
    {
        ["Nearby"] = true,
        ["Online"] = true,
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

    public bool IsManuallyPaused(PairSnapshot pair) => !AutomaticExceptions.Contains(pair.Key)
        && (ManualPauses.Contains(pair.Key) || (pair.CharacterIdentity != null && CharacterPauses.Contains(pair.CharacterIdentity)));

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

public sealed class CachedDuplicateCharacter
{
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<string, SyncProvider> Routes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ObservedCharacter
{
    public string Identity { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Ident { get; set; } = string.Empty;
}

// Saved before sending a request so a restart does not lose responsibility for a pause.
public sealed class OwnedPause
{
    public string CharacterIdentity { get; set; } = string.Empty;
    public string OriginalPermissions { get; set; } = string.Empty;
    public string PausedPermissions { get; set; } = string.Empty;
    public string OriginalPauseReason { get; set; } = string.Empty;
    public bool LocalHold { get; set; }
    public bool Confirmed { get; set; }
    public bool RestoreRequested { get; set; }
    public PendingMediaChange? MediaChange { get; set; }
}

// Keep both permission versions until a media request is observed, including across a reload.
public sealed class PendingMediaChange
{
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public string OriginalAfter { get; set; } = string.Empty;
    public DateTime Deadline { get; set; }
}
