using System;
using System.Collections.Generic;
using System.Linq;

namespace XivSyncManager;

public enum IndexRetention { None, Days30, Days60, Days90, Days180, OneYear }

public sealed class IndexedCharacterActivity
{
    internal IndexedCharacterActivity Copy() => (IndexedCharacterActivity)MemberwiseClone();
    public DateTime TrackingStartedUtc { get; set; }
    public DateTime? LastSeenOnlineUtc { get; set; }
}

public sealed class CachedDuplicateCharacter
{
    internal CachedDuplicateCharacter Copy()
    {
        var copy = (CachedDuplicateCharacter)MemberwiseClone();
        copy.Routes = new(Routes, StringComparer.Ordinal);
        return copy;
    }
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<string, SyncProvider> Routes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ObservedCharacter
{
    internal ObservedCharacter Copy() => (ObservedCharacter)MemberwiseClone();
    public string Identity { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Ident { get; set; } = string.Empty;
}

internal static class CharacterIndexRetention
{
    internal static (bool IndexChanged, bool ActivityChanged, HashSet<string> Online) UpdateActivity(
        Dictionary<string, IndexedCharacterActivity> activities, IEnumerable<string> indexedIdentities,
        IEnumerable<(string Identity, bool Online)> presence, DateTime now)
    {
        var indexed = indexedIdentities.Where(i => !string.IsNullOrEmpty(i)).ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var identity in indexed)
        {
            if (!activities.TryGetValue(identity, out var activity))
            {
                activities[identity] = new() { TrackingStartedUtc = now };
                changed = true;
            }
            else if (activity.TrackingStartedUtc == default)
            {
                activity.TrackingStartedUtc = now;
                changed = true;
            }
        }
        foreach (var identity in activities.Keys.Where(i => !indexed.Contains(i)).ToArray())
        {
            activities.Remove(identity);
            changed = true;
        }
        var online = presence.Where(p => p.Online && indexed.Contains(p.Identity))
            .Select(p => p.Identity).ToHashSet(StringComparer.Ordinal);
        var activityChanged = false;
        foreach (var identity in online)
        {
            var activity = activities[identity];
            if (activity.LastSeenOnlineUtc == null || now > activity.LastSeenOnlineUtc)
            {
                activity.LastSeenOnlineUtc = now;
                activityChanged = true;
            }
        }
        return (changed, activityChanged, online);
    }

    internal static HashSet<string> RemoveExpired(Dictionary<string, IndexedCharacterActivity> activities,
        Dictionary<string, ObservedCharacter> observations, Dictionary<string, CachedDuplicateCharacter> duplicates,
        IndexRetention retention, DateTime now)
    {
        var expired = activities.Where(entry => IsExpired(entry.Value, retention, now))
            .Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in observations.Where(entry => expired.Contains(entry.Value.Identity))
                     .Select(entry => entry.Key).ToArray())
            observations.Remove(key);
        foreach (var identity in expired)
        {
            duplicates.Remove(identity);
            activities.Remove(identity);
        }
        return expired;
    }

    internal static bool IsExpired(IndexedCharacterActivity activity, IndexRetention retention, DateTime now)
    {
        var since = activity.LastSeenOnlineUtc ?? activity.TrackingStartedUtc;
        // Entries from before activity tracking get a baseline before they can expire.
        if (since == default || now <= since) return false;
        return retention switch
        {
            IndexRetention.Days30 => now - since > TimeSpan.FromDays(30),
            IndexRetention.Days60 => now - since > TimeSpan.FromDays(60),
            IndexRetention.Days90 => now - since > TimeSpan.FromDays(90),
            IndexRetention.Days180 => now - since > TimeSpan.FromDays(180),
            IndexRetention.OneYear => since.Year < 9999 && now > since.AddYears(1),
            _ => false,
        };
    }
}
