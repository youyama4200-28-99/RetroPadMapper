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
        var oneMillisecondQueue = new ConcurrentQueue<Arrival>();
        using var start = new ManualResetEventSlim(false);
        var pollingUs = new double[SampleCount];
        var oneMillisecondUs = new double[SampleCount];
        Array.Fill(pollingUs, double.NaN);
        Array.Fill(oneMillisecondUs, double.NaN);
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

        var oneMillisecondPoller = new Thread(() =>
        {
            start.Wait();
            using var timer = new HighResolutionPeriodicTimer(1);
            while (Volatile.Read(ref producerDone) == 0 || !oneMillisecondQueue.IsEmpty)
            {
                timer.WaitHandle.WaitOne();
                Drain(oneMillisecondQueue, oneMillisecondUs);
            }
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "1 ms high-resolution polling candidate" };

        poller.Start();
        oneMillisecondPoller.Start();
        start.Set();
        var intervalTicks = Math.Max(1L, Stopwatch.Frequency / 4000L);
        var next = Stopwatch.GetTimestamp();
        for (var i = 0; i < SampleCount; i++)
        {
            while (Stopwatch.GetTimestamp() < next) Thread.SpinWait(16);
            var arrival = new Arrival(i, Stopwatch.GetTimestamp());
            pollingQueue.Enqueue(arrival);
            oneMillisecondQueue.Enqueue(arrival);
            next += intervalTicks;
        }
        Volatile.Write(ref producerDone, 1);
        poller.Join(5000);
        oneMillisecondPoller.Join(5000);

        var pollStats = Stats(pollingUs);
        var oneMillisecondStats = Stats(oneMillisecondUs);
        var passed = pollStats.Count == SampleCount && oneMillisecondStats.Count == SampleCount && oneMillisecondStats.P95 < pollStats.P95;
        WriteCsv(Path.Combine(outputDirectory, "latency-samples.csv"), pollingUs, oneMillisecondUs);
        WriteReport(Path.Combine(outputDirectory, "RESULTS.md"), pollStats, oneMillisecondStats, passed);
        Console.WriteLine($"8 ms polling p95={pollStats.P95:F3} us; 1 ms high-resolution polling p95={oneMillisecondStats.P95:F3} us; pass={passed}");
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

    private static void WriteCsv(string path, double[] polling, double[] oneMillisecond)
    {
        using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
        writer.WriteLine("sample_id,polling_8ms_us,polling_high_resolution_1ms_us");
        for (var i = 0; i < SampleCount; i++)
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{i},{polling[i]:F3},{oneMillisecond[i]:F3}"));
    }

    private static void WriteReport(string path, (int Count, double P50, double P95, double P99, double Max) polling,
        (int Count, double P50, double P95, double P99, double Max) oneMillisecond, bool passed)
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
            $"| 1 ms high-resolution polling | {oneMillisecond.Count} | {oneMillisecond.P50:F3} | {oneMillisecond.P95:F3} | {oneMillisecond.P99:F3} | {oneMillisecond.Max:F3} |",
            "",
            $"**Predeclared check:** 1 ms high-resolution polling p95 < 8 ms polling p95 and both paths captured all samples — **{(passed ? "PASS" : "FAIL")}**.",
            "",
            "This isolates scheduler/dispatch delay; it does not measure Bluetooth radio latency, controller firmware, SDL's HID backend, or a target game's input sampling. Raw paired observations are in `latency-samples.csv`.",
        };
        File.WriteAllLines(path, lines);
    }
}
