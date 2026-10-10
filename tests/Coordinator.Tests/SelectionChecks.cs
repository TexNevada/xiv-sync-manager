using System.Diagnostics;
using XivSyncManager;

namespace CoordinatorTests;

internal static class SelectionChecks
{
    internal static void Run(Action<bool, string> assert)
    {
        var measurements = new List<(int Count, long Bytes, double Milliseconds)>();
        foreach (var count in new[] { 500, 1000 })
        {
            Environment.Reset();
            var primary = new Environment(SyncProvider.PlayerSync);
            var backup = new Environment(SyncProvider.Snowcloak);
            var template = primary.Snapshot();
            var backupTemplate = backup.Snapshot();
            var state = new Configuration { AutomaticManagement = true };
            var pairs = new List<PairSnapshot>();
            for (var index = 0; index < count; index++)
            {
                var identity = $"Scale{index}@1";
                foreach (var source in new[] { template, backupTemplate })
                {
                    var key = $"{source.Provider}|server|local|{index}";
                    var held = source.Provider == SyncProvider.Snowcloak;
                    pairs.Add(new()
                    {
                        Provider = source.Provider, Key = key, Uid = index.ToString(), Label = identity, Ident = identity,
                        CharacterIdentity = identity, Online = true, ManagerHeld = held, ManagerFullyHeld = held,
                        Permissions = held ? "" : "0", PauseReason = "", Adapter = source.Adapter,
                    });
                    if (held) state.OwnedPauses[key] = new() { LocalHold = true, CharacterIdentity = identity, Confirmed = true };
                }
            }
            var policy = new DuplicatePolicy();
            var statuses = new[] { primary.Adapter.Status, backup.Adapter.Status };
            var visible = new HashSet<string>();
            for (var warmup = 0; warmup < 5; warmup++) _ = policy.GetAutomaticPauses(pairs, state, statuses, visible);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            const int samples = 10;
            HashSet<string>? decision = null;
            for (var sample = 0; sample < samples; sample++) decision = policy.GetAutomaticPauses(pairs, state, statuses, visible);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds / samples;
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / samples;
            measurements.Add((count, bytes, elapsed));
            assert(decision!.Count == count && decision.All(k => k.StartsWith("Snowcloak|", StringComparison.Ordinal)),
                "Settled large-list selection preserves every backup pause");
            assert(bytes < count * 4000L, "Selection allocation stays bounded per character rather than per global pause");

            // The same lookup must preserve old backups while a new winner is being restored.
            state.Priority = [SyncProvider.Snowcloak, SyncProvider.PlayerSync, SyncProvider.Lightless];
            var switchDecision = policy.GetAutomaticPauses(pairs, state, statuses, visible);
            assert(switchDecision.Count == 0, "Choosing held backups releases them before pausing their active alternatives");
            state.AutomaticManagement = false;
            assert(policy.GetAutomaticPauses(pairs, state, statuses, visible).Count == 0,
                "Disabling automatic management clears all settled backup decisions");
        }
        assert(measurements[1].Bytes < measurements[0].Bytes * 2.7,
            "Doubling characters does not cause quadratic selection allocations");
        foreach (var measurement in measurements)
            Console.WriteLine($"Selection scale: {measurement.Count} characters; {measurement.Milliseconds:F2} ms; {measurement.Bytes} bytes per pass");
    }
}
