using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace XivSyncManager;

public static class SyncProviderNames
{
    public static string DisplayName(this SyncProvider provider) => provider switch
    {
        SyncProvider.Lightless => "Lightless Sync",
        SyncProvider.Snowcloak => "Snowcloak",
        _ => "PlayerSync",
    };
}

public sealed class PairSnapshot
{
    public required SyncProvider Provider { get; init; }
    public required string Key { get; init; }
    public required string Uid { get; init; }
    public required string Label { get; init; }
    public required string Ident { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public string? CharacterIdentity { get; set; }
    public string? CharacterName { get; set; }
    public nint Address { get; init; }
    public bool Online { get; init; }
    public bool Visible { get; init; }
    public bool OwnPaused { get; init; }
    public bool OtherPaused { get; init; }
    public bool ExternalHold { get; init; }
    public bool ManagerHeld { get; init; }
    public bool ManagerFullyHeld { get; init; }
    public required string Permissions { get; init; }
    public required string PauseReason { get; init; }
    public ProfileStatus Profile { get; init; } = new(ProfileAvailability.Unknown, "Profile has not been checked.");
    public MediaStatus Media { get; init; } = new(false, "", MediaKind.None, MediaKind.None, "Media controls are unavailable.");
    public SoundActivity Audio { get; set; } = new(false, null, false, null, "Audio activity is unavailable.");
    internal object Pair { get; init; } = null!;
    internal ReflectionSyncAdapter Adapter { get; init; } = null!;
}

public enum ProfileAvailability { Available, Cached, NoProfile, Unknown, Unavailable, Paused }

public sealed record ProfileStatus(ProfileAvailability Availability, string Description,
    bool CanOpen = false, string OpenUnavailableReason = "")
{
    public string Label => Availability switch
    {
        ProfileAvailability.Available => "Has profile",
        ProfileAvailability.Cached => "Cached profile",
        ProfileAvailability.NoProfile => "No profile reported",
        ProfileAvailability.Unavailable => "Profile unavailable",
        ProfileAvailability.Paused => "Profile hidden while paused",
        _ => "Not checked",
    };
}

[Flags]
public enum MediaKind { None = 0, Animations = 1, Sounds = 2, Vfx = 4 }

public sealed record MediaStatus(bool CanEdit, string Permissions, MediaKind Disabled, MediaKind OtherDisabled, string Description);

public sealed record SoundActivity(bool Supported, DateTime? LastLoadedAt, bool Playing, DateTime? LastPlayedAt, string Description)
{
    public bool Recent => Playing || DateTime.UtcNow - (LastPlayedAt ?? LastLoadedAt ?? DateTime.MinValue) <= TimeSpan.FromSeconds(60);
}

public sealed class ProviderStatus
{
    public required SyncProvider Provider { get; init; }
    public required string Description { get; init; }
    public bool Connected { get; init; }
    public bool Available { get; init; }
    public bool LocalHolds { get; init; }
    public bool CanDisconnect { get; init; }
    public bool CanReconnect { get; init; }
    public string ConnectionUnavailableReason { get; init; } = string.Empty;
    public IntegrationReport Integrations { get; init; } = new([], "Fetching data.", Pending: true);
    public VramUsage Vram { get; init; } = VramUsage.Unavailable("Waiting for the first check.");
}

public sealed record VramUsage(long? Bytes, bool Partial, string Description)
{
    public static VramUsage Unavailable(string description) => new(null, false, description);
}

public enum IntegrationState { Ready, Missing, Disabled, Incompatible, NotReady, Conflict, Unknown }

public sealed record IntegrationResource(string Name, string ProjectUrl, string? RepositoryUrl, string SearchTerm);

public sealed record PluginIntegration(string Name, bool Required, string Feature,
    IntegrationState State, string Explanation, string SearchTerm)
{
    public IReadOnlyList<IntegrationResource> Resources { get; init; } = [];

    public bool NeedsAttention => State is not (IntegrationState.Ready or IntegrationState.Unknown);
    public string StatusLabel => State switch
    {
        IntegrationState.Ready => "Ready",
        IntegrationState.Missing => "Not installed",
        IntegrationState.Disabled => "Not enabled",
        IntegrationState.Incompatible => "Update needed",
        IntegrationState.NotReady => "Not ready",
        IntegrationState.Conflict => "Conflict",
        _ => "Unable to check",
    };
}

public sealed record IntegrationReport(IReadOnlyList<PluginIntegration> Plugins, string? Notice = null, bool Pending = false)
{
    public int RequiredProblems => Plugins.Count(p => p.Required && p.NeedsAttention);
    public int OptionalProblems => Plugins.Count(p => !p.Required && p.NeedsAttention);
    public bool Incomplete => Pending || Notice != null || Plugins.Any(p => p.State == IntegrationState.Unknown);
}

public sealed record DuplicateCharacter(string Identity, string DisplayName, bool Online,
    IReadOnlyList<SyncProvider> Providers, IReadOnlyList<PairSnapshot> Pairs)
{
    public bool Nearby { get; init; }
}

internal sealed class DuplicatePolicy
{
    private static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(5);
    private readonly Dictionary<SyncProvider, long> disconnectedSince = [];
    private readonly Dictionary<string, SyncProvider> selectedProviders = new(StringComparer.Ordinal);
    private readonly HashSet<string> waitingForReconnect = new(StringComparer.Ordinal);
    private readonly HashSet<string> pinnedFallbacks = new(StringComparer.Ordinal);
    private readonly HashSet<string> departedFallbacks = new(StringComparer.Ordinal);
    private HashSet<string> previousPauses = new(StringComparer.Ordinal);

    internal bool IsWaitingForReconnect(string identity) => waitingForReconnect.Contains(identity);
    internal bool IsKeepingFallback(string identity) => pinnedFallbacks.Contains(identity);

    internal void ForgetSelection(string identity)
    {
        selectedProviders.Remove(identity);
        waitingForReconnect.Remove(identity);
        pinnedFallbacks.Remove(identity);
        departedFallbacks.Remove(identity);
    }

    internal void ResetSelections()
    {
        selectedProviders.Clear();
        waitingForReconnect.Clear();
        pinnedFallbacks.Clear();
        departedFallbacks.Clear();
    }

    internal HashSet<string> GetAutomaticPauses(
        IReadOnlyList<PairSnapshot> pairs, Configuration configuration,
        IReadOnlyList<ProviderStatus> providers, IReadOnlySet<string> visibleCharacters)
    {
        var desired = new HashSet<string>(StringComparer.Ordinal);
        var now = Stopwatch.GetTimestamp();
        var connected = providers.Where(p => p.Connected).Select(p => p.Provider).ToHashSet();
        foreach (var provider in providers)
        {
            if (provider.Connected) disconnectedSince.Remove(provider.Provider);
            else disconnectedSince.TryAdd(provider.Provider, now);
        }
        waitingForReconnect.Clear();
        if (!configuration.AutomaticManagement)
        {
            ResetSelections();
            previousPauses.Clear();
            return desired;
        }

        if (!configuration.KeepFallbackUntilReentry)
        {
            pinnedFallbacks.Clear();
            departedFallbacks.Clear();
        }
        else
        {
            // Game-object presence is independent of a sync handler being paused or disconnected.
            // Require an observed departure followed by a return before using preferences again.
            foreach (var identity in pinnedFallbacks.ToArray())
            {
                if (!visibleCharacters.Contains(identity)) departedFallbacks.Add(identity);
                else if (departedFallbacks.Contains(identity)) ForgetSelection(identity);
            }
        }

        // Index settled backup pauses once. A character must not scan every other
        // character's pauses, or allocate a provider prefix for every global key.
        var previousByCharacter = previousPauses.Where(key => configuration.OwnedPauses.TryGetValue(key, out var owned)
                && !owned.RestoreRequested && !configuration.ManualPauses.Contains(key)
                && !configuration.AutomaticExceptions.Contains(key))
            .ToLookup(key => configuration.OwnedPauses[key].CharacterIdentity, StringComparer.Ordinal);
        var groups = pairs.Where(p => p.CharacterIdentity != null)
            .GroupBy(p => p.CharacterIdentity!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        foreach (var identity in groups.Keys.Concat(selectedProviders.Keys).Distinct(StringComparer.Ordinal).ToArray())
        {
            var group = groups.GetValueOrDefault(identity) ?? [];
            if (configuration.CharacterPauses.Contains(identity))
            {
                ForgetSelection(identity);
                continue;
            }

            // Keep existing backup pauses during a brief outage; repeated refreshes do not restart the timer.
            var hasSelection = selectedProviders.TryGetValue(identity, out var selected);
            var selectedDisconnected = hasSelection && !connected.Contains(selected);
            if (selectedDisconnected
                && disconnectedSince.TryGetValue(selected, out var since)
                && Stopwatch.GetElapsedTime(since, now) <= DisconnectGrace)
            {
                waitingForReconnect.Add(identity);
                PreservePauses(identity, selected);
                continue;
            }

            var candidates = group.Where(p => p.Adapter.Connected && !p.OtherPaused && !p.ExternalHold
                && !configuration.IsManuallyPaused(p)
                && !configuration.AutomaticExceptions.Contains(p.Key)
                && (p.Online || p.Visible || (visibleCharacters.Contains(identity) && IsRestorable(p)))
                && (!p.OwnPaused || !p.Adapter.LocalHolds && IsRestorable(p))).ToList();
            if (candidates.Count == 0)
            {
                // Retain an outage decision until a fallback becomes available, and retain a pin
                // while a character is absent so their next observed return can reset it.
                if (!selectedDisconnected && !pinnedFallbacks.Contains(identity)) ForgetSelection(identity);
                continue;
            }

            var hasPreference = configuration.PreferredProviders.TryGetValue(identity, out var preferred);
            var keepCurrent = configuration.KeepFallbackUntilReentry && pinnedFallbacks.Contains(identity) && hasSelection;
            var winner = candidates.OrderBy(p => keepCurrent && p.Provider == selected ? -2
                    : hasPreference && p.Provider == preferred ? -1 : Rank(p.Provider, configuration.Priority))
                .ThenBy(p => p.Provider).ThenBy(p => p.Key, StringComparer.Ordinal).First();
            if (configuration.KeepFallbackUntilReentry && selectedDisconnected && winner.Provider != selected)
            {
                pinnedFallbacks.Add(identity);
                if (!visibleCharacters.Contains(identity)) departedFallbacks.Add(identity);
            }
            selectedProviders[identity] = winner.Provider;

            // Release the winner's pause while keeping existing backups held until restoration completes.
            PreservePauses(identity, winner.Provider);
            if (winner.OwnPaused || winner.ManagerHeld
                || (configuration.OwnedPauses.TryGetValue(winner.Key, out var restoring) && restoring.RestoreRequested)) continue;

            foreach (var pair in group)
            {
                if (pair.Provider != winner.Provider && !configuration.IsManuallyPaused(pair)
                    && !configuration.AutomaticExceptions.Contains(pair.Key)
                    && (pair.Online || configuration.OwnedPauses.ContainsKey(pair.Key)))
                    desired.Add(pair.Key);
            }
        }
        previousPauses = desired;
        return desired;

        bool IsRestorable(PairSnapshot pair) => configuration.OwnedPauses.TryGetValue(pair.Key, out var owned)
            && owned.LocalHold == pair.Adapter.LocalHolds && (!owned.LocalHold || pair.ManagerHeld);

        void PreservePauses(string identity, SyncProvider winner)
        {
            var winnerPrefix = $"{winner}|";
            foreach (var key in previousByCharacter[identity])
                if (!key.StartsWith(winnerPrefix, StringComparison.Ordinal)) desired.Add(key);
        }
    }

    private static int Rank(SyncProvider provider, List<SyncProvider> priority)
    {
        var index = priority.IndexOf(provider);
        return index < 0 ? int.MaxValue : index;
    }
}
