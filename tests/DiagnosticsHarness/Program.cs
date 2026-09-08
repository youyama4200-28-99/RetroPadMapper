using System.Diagnostics;
using RetroPadMapper;

// Isolated instrumentation cost only: no SDL, radio, driver, UI, disk, or game.
foreach (var enabled in new[] { false, true })
{
    AppLog.Enabled = enabled;
    var probe = new ConnectionDiagnostics();
    for (var i = 0; i < 20000; i++) Step(probe);
    const int count = 500000;
    var durations = new List<double>();
    long maxAllocated = 0;
    for (var round = 0; round < 7; round++)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++) Step(probe);
        var elapsed = Stopwatch.GetElapsedTime(started);
        maxAllocated = Math.Max(maxAllocated, GC.GetAllocatedBytesForCurrentThread() - allocated);
        durations.Add(elapsed.TotalMicroseconds / count);
    }
    durations.Sort();
    Console.WriteLine($"debug={enabled} warm_calls_per_round={count} rounds=7 median_us_per_call={durations[3]:F6} slowest_round_us_per_call={durations[^1]:F6} max_allocated_bytes_per_round={maxAllocated}");
    if (maxAllocated != 0) return 1;
}
Console.WriteLine("PASS: warmed instrumentation allocates no managed memory. NOT an end-to-end latency or Bluetooth reconnect test.");
return 0;

static void Step(ConnectionDiagnostics probe)
{
    var started = probe.BeginPoll();
    var acquired = probe.LockAcquired(started);
    probe.Updated(acquired);
    probe.Sample(1, 3);
}

namespace RetroPadMapper
{
    // Keep production probes linked above; replace only environment dependencies.
    internal static class AppLog
    {
        internal static volatile bool Enabled;
        internal static int Epoch => 1;
        internal static void Debug(string value) { }
        internal static void Info(string value) { }
    }
    internal sealed record ControllerOption(string Id, uint InstanceId, string Name)
    {
        internal ushort VendorId { get; init; }
        internal ushort ProductId { get; init; }
    }
    internal static class ControllerService
    {
        internal static bool IsRetroNintendo(string name) => name.Contains("Nintendo");
    }
}
