using System.Text.Json;
using XivSyncManager;

var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
var checks = 0;

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var periods = new[]
{
    (IndexRetention.Days30, 30), (IndexRetention.Days60, 60),
    (IndexRetention.Days90, 90), (IndexRetention.Days180, 180),
};
foreach (var (retention, days) in periods)
{
    var activity = new IndexedCharacterActivity { TrackingStartedUtc = now.AddYears(-2), LastSeenOnlineUtc = now };
    Assert(!CharacterIndexRetention.IsExpired(activity, retention, now.AddDays(days)), $"{retention}: keep at exact boundary");
    Assert(CharacterIndexRetention.IsExpired(activity, retention, now.AddDays(days).AddTicks(1)), $"{retention}: delete after boundary");
    Assert(!CharacterIndexRetention.IsExpired(activity, retention, now.AddDays(-1)), $"{retention}: tolerate backward clock");
}

var oldActivity = new IndexedCharacterActivity { TrackingStartedUtc = now.AddYears(-2) };
Assert(!CharacterIndexRetention.IsExpired(oldActivity, IndexRetention.None, now), "None retains old entries");
Assert(!CharacterIndexRetention.IsExpired(oldActivity, (IndexRetention)999, now), "Unknown setting retains data");
var leapDay = new IndexedCharacterActivity { TrackingStartedUtc = new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc) };
var anniversary = new DateTime(2025, 2, 28, 12, 0, 0, DateTimeKind.Utc);
Assert(!CharacterIndexRetention.IsExpired(leapDay, IndexRetention.OneYear, anniversary), "Year keeps exact calendar anniversary");
Assert(CharacterIndexRetention.IsExpired(leapDay, IndexRetention.OneYear, anniversary.AddTicks(1)), "Year expires after leap-day anniversary");
Assert(!CharacterIndexRetention.IsExpired(new() { TrackingStartedUtc = DateTime.MaxValue.AddDays(-1) },
    IndexRetention.OneYear, DateTime.MaxValue), "Calendar limit does not overflow");

Dictionary<string, IndexedCharacterActivity> activities = [];
var update = CharacterIndexRetention.UpdateActivity(activities, ["Legacy@1"], [], now);
Assert(update.IndexChanged && activities["Legacy@1"].TrackingStartedUtc == now, "Legacy entry starts tracking on migration");
Assert(activities["Legacy@1"].LastSeenOnlineUtc == null, "Migration does not invent an online observation");
Assert(!CharacterIndexRetention.IsExpired(activities["Legacy@1"], IndexRetention.Days30, now), "Migration does not immediately delete old data");
CharacterIndexRetention.UpdateActivity(activities, ["Legacy@1"], [("Legacy@1", false)], now.AddDays(29));
Assert(activities["Legacy@1"].TrackingStartedUtc == now && activities["Legacy@1"].LastSeenOnlineUtc == null,
    "Offline observations do not renew the baseline");
Assert(CharacterIndexRetention.IsExpired(activities["Legacy@1"], IndexRetention.Days30, now.AddDays(31)),
    "Never-online entries expire from tracking start");

// Online presence does not require a nearby flag; any associated service can renew the entry.
var onlineAt = now.AddDays(31);
update = CharacterIndexRetention.UpdateActivity(activities, ["Legacy@1"],
    [("Legacy@1", false), ("Legacy@1", true), ("Unidentified@1", true)], onlineAt);
Assert(update.ActivityChanged && update.Online.SetEquals(["Legacy@1"]), "One online route renews the identified character");
Assert(activities["Legacy@1"].LastSeenOnlineUtc == onlineAt, "Online away from Nearby updates last seen");
Assert(!activities.ContainsKey("Unidentified@1"), "An unindexed account does not create a character association");
CharacterIndexRetention.UpdateActivity(activities, ["Legacy@1"], [("Legacy@1", false)], onlineAt.AddDays(29));
Assert(activities["Legacy@1"].LastSeenOnlineUtc == onlineAt, "Offline presence does not update last-online timestamp");
Assert(!CharacterIndexRetention.IsExpired(activities["Legacy@1"], IndexRetention.Days30, onlineAt.AddDays(29)),
    "Online observation resets expiry");
CharacterIndexRetention.UpdateActivity(activities, ["Legacy@1"], [("Legacy@1", true)], now);
Assert(activities["Legacy@1"].LastSeenOnlineUtc == onlineAt, "Backward clock does not move last seen back");

var persisted = JsonSerializer.Deserialize<Dictionary<string, IndexedCharacterActivity>>(JsonSerializer.Serialize(activities))!;
Assert(persisted["Legacy@1"].TrackingStartedUtc == now && persisted["Legacy@1"].LastSeenOnlineUtc == onlineAt,
    "Both activity timestamps survive serialization and reload");
Assert(CharacterIndexRetention.IsExpired(persisted["Legacy@1"], IndexRetention.Days30, onlineAt.AddDays(31)),
    "Elapsed time while the plugin is closed counts toward expiry");

activities["Recent@1"] = new() { TrackingStartedUtc = now, LastSeenOnlineUtc = now.AddDays(61) };
activities["Single@1"] = new() { TrackingStartedUtc = now };
Dictionary<string, ObservedCharacter> observations = new()
{
    ["PlayerSync|old"] = new() { Identity = "Legacy@1" },
    ["Lightless|old"] = new() { Identity = "Legacy@1" },
    ["PlayerSync|new"] = new() { Identity = "Recent@1" },
    ["PlayerSync|single"] = new() { Identity = "Single@1" },
};
Dictionary<string, CachedDuplicateCharacter> duplicates = new()
{
    ["Legacy@1"] = new() { DisplayName = "Legacy", Routes = new() { ["PlayerSync|old"] = SyncProvider.PlayerSync, ["Lightless|old"] = SyncProvider.Lightless } },
    ["Recent@1"] = new() { DisplayName = "Recent" },
};
var expired = CharacterIndexRetention.RemoveExpired(activities, observations, duplicates, IndexRetention.None, now.AddDays(62));
Assert(expired.Count == 0 && observations.Count == 4 && duplicates.Count == 2, "None deletes no index data");
expired = CharacterIndexRetention.RemoveExpired(activities, observations, duplicates, IndexRetention.Days30, now.AddDays(62));
Assert(expired.SetEquals(["Legacy@1", "Single@1"]), "Expire stale duplicates and single-service observations");
Assert(observations.Count == 1 && observations.ContainsKey("PlayerSync|new"), "Remove every expired association so history cannot rebuild it");
Assert(duplicates.Count == 1 && duplicates.ContainsKey("Recent@1"), "Keep recently-online duplicate");
Assert(activities.Count == 1 && activities.ContainsKey("Recent@1"), "Remove expired activity metadata");
CharacterIndexRetention.UpdateActivity(activities, observations.Values.Select(o => o.Identity).Concat(duplicates.Keys), [], now.AddDays(63));
Assert(!activities.ContainsKey("Legacy@1"), "Expired associations stay deleted on the next refresh");
CharacterIndexRetention.UpdateActivity(activities, ["Recent@1", "Legacy@1"], [("Legacy@1", true)], now.AddDays(64));
Assert(activities["Legacy@1"].LastSeenOnlineUtc == now.AddDays(64), "A newly verified online character can be indexed again");

Console.WriteLine($"Index retention checks passed: {checks}");
