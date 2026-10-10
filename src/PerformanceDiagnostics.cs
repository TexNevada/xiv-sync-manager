using System;
using System.Diagnostics;

namespace XivSyncManager;

internal sealed class PerformanceDiagnostics
{
    private long nextWarning;

    internal bool ShouldReport(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromMilliseconds(10)) return false;
        var now = Stopwatch.GetTimestamp();
        if (now < nextWarning) return false;
        nextWarning = now + (long)(30 * Stopwatch.Frequency);
        return true;
    }

    internal static void Report(string operation, TimeSpan elapsed, long allocated, int gen0, int gen1, int gen2,
        string detail)
    {
        Plugin.Log.Warning("[Performance] {Operation} took {ElapsedMs:F2} ms; {AllocatedBytes} bytes allocated on this thread; " +
            "GC collections during the interval: {Gen0}/{Gen1}/{Gen2}. {Detail} " +
            "Elapsed time can include process-wide GC pauses or scheduling delays; it does not establish their cause.",
            operation, elapsed.TotalMilliseconds, allocated, gen0, gen1, gen2, detail);
    }
}
