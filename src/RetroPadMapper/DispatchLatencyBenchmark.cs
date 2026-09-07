using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace RetroPadMapper;

internal static class DispatchLatencyBenchmark
{
    private const int SampleCount = 5000;
    private readonly record struct Arrival(int Id, long Ticks);

    internal static int Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var pollingQueue = new ConcurrentQueue<Arrival>();
        var eventQueue = new ConcurrentQueue<Arrival>();
        using var signal = new AutoResetEvent(false);
        using var start = new ManualResetEventSlim(false);
        var pollingUs = new double[SampleCount];
        var eventUs = new double[SampleCount];
        Array.Fill(pollingUs, double.NaN);
        Array.Fill(eventUs, double.NaN);
        var producerDone = 0;

        var poller = new Thread(() =>
        {
            start.Wait();
            while (Volatile.Read(ref producerDone) == 0 || !pollingQueue.IsEmpty)
            {
                Thread.Sleep(8);
                Drain(pollingQueue, pollingUs);
            }
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "8 ms polling control" };

        var watcher = new Thread(() =>
        {
            start.Wait();
            while (Volatile.Read(ref producerDone) == 0 || !eventQueue.IsEmpty)
            {
                signal.WaitOne(100);
                Drain(eventQueue, eventUs);
            }
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "event-driven candidate" };

        poller.Start();
        watcher.Start();
        start.Set();
        var intervalTicks = Math.Max(1L, Stopwatch.Frequency / 4000L);
        var next = Stopwatch.GetTimestamp();
        for (var i = 0; i < SampleCount; i++)
        {
            while (Stopwatch.GetTimestamp() < next) Thread.SpinWait(16);
            var arrival = new Arrival(i, Stopwatch.GetTimestamp());
            pollingQueue.Enqueue(arrival);
            eventQueue.Enqueue(arrival);
            signal.Set();
            next += intervalTicks;
        }
        Volatile.Write(ref producerDone, 1);
        signal.Set();
        poller.Join(5000);
        watcher.Join(5000);

        var pollStats = Stats(pollingUs);
        var eventStats = Stats(eventUs);
        var passed = pollStats.Count == SampleCount && eventStats.Count == SampleCount && eventStats.P95 < pollStats.P95;
        WriteCsv(Path.Combine(outputDirectory, "latency-samples.csv"), pollingUs, eventUs);
        WriteReport(Path.Combine(outputDirectory, "RESULTS.md"), pollStats, eventStats, passed);
        Console.WriteLine($"8 ms polling p95={pollStats.P95:F3} us; event p95={eventStats.P95:F3} us; pass={passed}");
        return passed ? 0 : 4;
    }

    private static void Drain(ConcurrentQueue<Arrival> queue, double[] samples)
    {
        while (queue.TryDequeue(out var arrival))
            samples[arrival.Id] = Stopwatch.GetElapsedTime(arrival.Ticks).TotalMicroseconds;
    }

    private static (int Count, double P50, double P95, double P99, double Max) Stats(double[] source)
    {
        var values = source.Where(double.IsFinite).Order().ToArray();
        double At(double p) => values.Length == 0 ? double.NaN : values[Math.Clamp((int)Math.Ceiling(p * values.Length) - 1, 0, values.Length - 1)];
        return (values.Length, At(.50), At(.95), At(.99), values.Length == 0 ? double.NaN : values[^1]);
    }

    private static void WriteCsv(string path, double[] polling, double[] eventDriven)
    {
        using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
        writer.WriteLine("sample_id,polling_8ms_us,event_driven_us");
        for (var i = 0; i < SampleCount; i++)
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{i},{polling[i]:F3},{eventDriven[i]:F3}"));
    }

    private static void WriteReport(string path, (int Count, double P50, double P95, double P99, double Max) polling,
        (int Count, double P50, double P95, double P99, double Max) eventDriven, bool passed)
    {
        var lines = new[]
        {
            "# Dispatch latency benchmark",
            "",
            $"- Generated (UTC): {DateTime.UtcNow:O}",
            $"- Host: {Environment.OSVersion}; {Environment.ProcessorCount} logical CPUs; .NET {Environment.Version}",
            $"- Samples: {SampleCount:N0}; synthetic arrivals at 4 kHz",
            "- Clock: `Stopwatch` monotonic high-resolution clock",
            "",
            "| Strategy | n | p50 (µs) | p95 (µs) | p99 (µs) | max (µs) |",
            "|---|---:|---:|---:|---:|---:|",
            $"| Previous 8 ms polling | {polling.Count} | {polling.P50:F3} | {polling.P95:F3} | {polling.P99:F3} | {polling.Max:F3} |",
            $"| Event signal | {eventDriven.Count} | {eventDriven.P50:F3} | {eventDriven.P95:F3} | {eventDriven.P99:F3} | {eventDriven.Max:F3} |",
            "",
            $"**Predeclared check:** event p95 < polling p95 and both paths captured all samples — **{(passed ? "PASS" : "FAIL")}**.",
            "",
            "This isolates scheduler/dispatch delay; it does not measure Bluetooth radio latency, controller firmware, SDL's HID backend, or a target game's input sampling. Raw paired observations are in `latency-samples.csv`.",
        };
        File.WriteAllLines(path, lines);
    }
}
