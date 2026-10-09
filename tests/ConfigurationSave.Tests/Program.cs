using System.Collections.Concurrent;
using System.Text.Json;
using XivSyncManager;

var checks = 0;
void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var defaults = new Configuration();
Assert(defaults.StaleIndexRetention == IndexRetention.Days30, "New settings default to 30-day expiry");
Assert(!defaults.SectionExpanded["Online"], "New settings keep Online collapsed");
var oldSettings = JsonSerializer.Deserialize<Configuration>("{}")!;
Assert(oldSettings.StaleIndexRetention == IndexRetention.Days30, "Settings without a saved retention choice use 30 days");
var savedSettings = JsonSerializer.Deserialize<Configuration>("{\"StaleIndexRetention\":0,\"SectionExpanded\":{\"Online\":true}}")!;
Assert(savedSettings.StaleIndexRetention == IndexRetention.None && savedSettings.SectionExpanded["Online"],
    "Existing retention and section choices remain unchanged");

var config = new Configuration
{
    AutomaticManagement = true,
    StaleIndexRetention = IndexRetention.Days60,
    PreferredProviders = new() { ["Character"] = SyncProvider.Lightless },
    SectionExpanded = new() { ["Online"] = true },
    ManualPauses = ["pair"],
    AutomaticExceptions = ["other"],
    CharacterPauses = ["Character"],
    LateMediaChanges = new() { ["pair"] = [new() { After = "late", OriginalAfter = "original" }] },
    LatePauseChanges = new() { ["pair"] = [new() { PausedPermissions = "late pause" }] },
    ObservedCharacters = new() { ["pair"] = new() { Identity = "Character", Ident = "verified" } },
    DuplicateCharacters = new() { ["Character"] = new() { Routes = new() { ["pair"] = SyncProvider.Lightless } } },
    CharacterIndexActivity = new() { ["Character"] = new() { TrackingStartedUtc = DateTime.UtcNow } },
    OwnedPauses = new() { ["pair"] = new()
    {
        CharacterIdentity = "Character", OriginalPermissions = "before", PausedPermissions = "paused",
        MediaChange = new() { Before = "before", After = "after", OriginalAfter = "restored media" },
        PauseChange = new() { PausedPermissions = "pending pause" },
    } },
};
var snapshot = config.CreateSnapshot();
config.AutomaticManagement = false;
config.StaleIndexRetention = IndexRetention.None;
config.Priority.Clear();
config.PreferredProviders.Clear();
config.SectionExpanded["Online"] = false;
config.ManualPauses.Clear();
config.AutomaticExceptions.Clear();
config.CharacterPauses.Clear();
config.ObservedCharacters["pair"].Ident = "changed";
config.DuplicateCharacters["Character"].Routes.Clear();
config.CharacterIndexActivity["Character"].LastSeenOnlineUtc = DateTime.UtcNow;
config.OwnedPauses["pair"].OriginalPermissions = "changed";
config.OwnedPauses["pair"].MediaChange!.After = "changed";
config.LateMediaChanges["pair"][0].After = "changed";
config.LateMediaChanges["pair"].Add(new());
config.LatePauseChanges["pair"][0].PausedPermissions = "changed";
config.LatePauseChanges["pair"].Add(new());
config.OwnedPauses["pair"].PauseChange!.PausedPermissions = "changed";
Assert(snapshot.AutomaticManagement && snapshot.StaleIndexRetention == IndexRetention.Days60, "Snapshot retains scalar settings");
Assert(snapshot.Priority.Count == 3 && snapshot.PreferredProviders["Character"] == SyncProvider.Lightless, "Priority and preferred syncs are independent");
Assert(snapshot.SectionExpanded["Online"] && snapshot.ManualPauses.Contains("pair")
    && snapshot.AutomaticExceptions.Contains("other") && snapshot.CharacterPauses.Contains("Character"), "Saved choices do not share mutable collections");
Assert(snapshot.ObservedCharacters["pair"].Ident == "verified", "Character observations are independent");
Assert(snapshot.DuplicateCharacters["Character"].Routes.Count == 1, "Nested duplicate routes are independent");
Assert(snapshot.CharacterIndexActivity["Character"].LastSeenOnlineUtc == null, "Online activity is independent");
Assert(snapshot.OwnedPauses["pair"].OriginalPermissions == "before"
    && snapshot.OwnedPauses["pair"].MediaChange!.After == "after", "Restoration and pending media records are independent");
Assert(snapshot.LateMediaChanges["pair"].Count == 1 && snapshot.LateMediaChanges["pair"][0].After == "late",
    "Late media recovery lists and records are independent");
Assert(snapshot.LatePauseChanges["pair"].Count == 1 && snapshot.LatePauseChanges["pair"][0].PausedPermissions == "late pause",
    "Late pause recovery lists and records are independent");
Assert(snapshot.OwnedPauses["pair"].PauseChange!.PausedPermissions == "pending pause", "Pending pause requests are independent");
foreach (var property in typeof(Configuration).GetProperties())
{
    var live = property.GetValue(config);
    if (live is System.Collections.IEnumerable && live is not string)
        Assert(!ReferenceEquals(live, property.GetValue(snapshot)), $"Snapshot copies {property.Name}");
}

// A stalled disk write must not hold the caller. Pending noncritical saves coalesce.
using (var started = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
{
    var written = new ConcurrentQueue<int>();
    var errors = new ConcurrentQueue<Exception>();
    var queue = new ConfigurationSaveQueue<int>(value =>
    {
        if (value == 1)
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test write was not released");
        }
        written.Enqueue(value);
    }, errors.Enqueue);
    try
    {
        await Task.Run(() => queue.SaveBackground(1)).WaitAsync(TimeSpan.FromSeconds(2));
        Assert(started.Wait(TimeSpan.FromSeconds(2)), "Background disk write started");
        Assert(!queue.FlushAsync().IsCompleted, "Flush waits for the in-flight write");
        queue.SaveBackground(2);
        queue.SaveBackground(3);
    }
    finally { release.Set(); }
    await queue.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2));
    Assert(written.SequenceEqual([1, 3]) && errors.IsEmpty, "Only the newest pending routine snapshot is written");
}

// A restoration save must be durable before the caller proceeds, and older background
// snapshots must not overwrite it after SaveNow returns.
using (var started = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
{
    var written = new ConcurrentQueue<int>();
    var queue = new ConfigurationSaveQueue<int>(value =>
    {
        if (value == 1)
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test write was not released");
        }
        written.Enqueue(value);
    }, exception => throw exception);
    Task critical = Task.CompletedTask;
    try
    {
        queue.SaveBackground(1);
        Assert(started.Wait(TimeSpan.FromSeconds(2)), "Old write is in flight");
        queue.SaveBackground(2);
        critical = Task.Run(() => queue.SaveNow(3));
        try
        {
            await critical.WaitAsync(TimeSpan.FromMilliseconds(100));
            throw new InvalidOperationException("Critical save returned before disk write completed");
        }
        catch (TimeoutException) { checks++; }
    }
    finally { release.Set(); }
    await critical.WaitAsync(TimeSpan.FromSeconds(2));
    await queue.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2));
    Assert(written.Last() == 3, "New restoration record remains the final saved snapshot");
}

{
    var failures = new ConcurrentQueue<Exception>();
    var written = new ConcurrentQueue<int>();
    var queue = new ConfigurationSaveQueue<int>(value =>
    {
        if (value is 1 or 3) throw new InvalidOperationException("Simulated write failure");
        written.Enqueue(value);
    }, failures.Enqueue);
    queue.SaveBackground(1);
    await queue.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2));
    Assert(failures.Count == 1, "Background save failures are reported");
    queue.SaveBackground(2);
    await queue.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2));
    Assert(written.Last() == 2, "Background writer recovers for the next save");
    try
    {
        queue.SaveNow(3);
        throw new Exception("Critical save swallowed the failure");
    }
    catch (InvalidOperationException) { checks++; }
    queue.SaveNow(4);
    Assert(written.Last() == 4, "Critical writer recovers after a failed save");
}

// Exercise the real Configuration.SaveBackground wrapper with the substituted game service.
using (var started = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
{
    Configuration? saved = null;
    Plugin.PluginInterface.Write = value =>
    {
        started.Set();
        if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        saved = value;
    };
    var live = new Configuration { AutomaticManagement = true };
    try
    {
        live.SaveBackground();
        Assert(started.Wait(TimeSpan.FromSeconds(2)), "Configuration wrapper runs save off the caller thread");
        live.AutomaticManagement = false;
    }
    finally { release.Set(); }
    await live.FlushSavesAsync().WaitAsync(TimeSpan.FromSeconds(2));
    Assert(saved!.AutomaticManagement, "Background save serializes its captured snapshot, not later live mutations");
    Plugin.PluginInterface.Write = null;
}

Console.WriteLine($"Configuration save checks passed: {checks}");
