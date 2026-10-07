using System;
using System.Collections.Generic;
using System.Linq;

namespace XivSyncManager;

internal static class VramDiagnostics
{
    private const string AppliedCounter = "LastAppliedApproximateVRAMBytes";

    internal static VramUsage Read(object pairManager, IReadOnlyList<object> pairs, SyncProvider provider)
    {
        try
        {
            // Check the contract even for an empty list: unsupported versions must not look like 0 MiB.
            var pairType = pairManager.GetType().Assembly.GetType($"{pairManager.GetType().Namespace}.Pair");
            if (pairType?.GetProperty(AppliedCounter, ReflectionAccess.Members) == null)
                return VramUsage.Unavailable("This plugin version does not expose a supported VRAM estimate.");

            long total = 0;
            var visible = 0;
            var measured = 0;
            var unreadable = 0;
            foreach (var pair in pairs.Distinct(ReferenceEqualityComparer.Instance))
            {
                try
                {
                    if (ReflectionAccess.Read(pair, "IsVisible") is not bool inView)
                    {
                        unreadable++;
                        continue;
                    }
                    if (!inView) continue;
                    visible++;
                    var bytes = Counter(pair, AppliedCounter);
                    // Match the inspected native displays: Lightless prefers its optimized estimate;
                    // Snowcloak falls back to the remote report when an applied estimate is unavailable.
                    if (provider == SyncProvider.Lightless)
                        bytes = Counter(pair, "LastAppliedApproximateEffectiveVRAMBytes") ?? bytes;
                    else if (provider == SyncProvider.Snowcloak)
                        bytes ??= Counter(pair, "LastReportedApproximateVRAMBytes");
                    if (bytes == null) continue;
                    total = checked(total + bytes.Value);
                    measured++;
                }
                catch (OverflowException) { throw; }
                catch { unreadable++; }
            }

            var pending = visible - measured;
            if (measured == 0 && (visible > 0 || unreadable > 0))
                return VramUsage.Unavailable("Waiting for readable VRAM estimates from this plugin's visible synced players.");
            var description = visible == 0 ? "No visible synced players reported by this plugin."
                : provider switch
                {
                    SyncProvider.Lightless => "Visible players' optimized estimates, with original estimates used when needed.",
                    SyncProvider.Snowcloak => "Visible players' applied estimates, with reported estimates used when needed.",
                    _ => "Visible players' last applied VRAM estimates.",
                };
            if (pending > 0) description += $" Waiting for {pending} player estimate(s).";
            if (unreadable > 0) description += " Some player statistics could not be read.";
            return new(total, pending > 0 || unreadable > 0, description);
        }
        catch
        {
            // Statistics must not prevent compatible pair or connection controls from working.
            return VramUsage.Unavailable("VRAM statistics could not be read from this plugin right now.");
        }
    }

    private static long? Counter(object pair, string property) => ReflectionAccess.Read(pair, property) switch
    {
        long value when value >= 0 => value,
        _ => null,
    };
}
