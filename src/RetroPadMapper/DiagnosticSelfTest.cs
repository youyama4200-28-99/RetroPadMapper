using System.Collections.Concurrent;
using System.Diagnostics;

namespace RetroPadMapper;

internal static class DiagnosticSelfTest
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Diagnostic self-test failed: {name}");
    }

    internal static int Run()
    {
        try
        {
            Check(ConnectionDiagnostics.EventAge(0, 10) == "unknown", "zero event timestamp");
            Check(ConnectionDiagnostics.EventAge(11, 10) == "unknown", "future event timestamp");
            Check(ConnectionDiagnostics.EventAge(1_000_000, 6_500_000) == "5.500", "event age units");
            var hvc = new ControllerOption("hvc", 3, "Nintendo HVC Controller (1)");
            var xbox = new ControllerOption("xbox", 1, "Xbox 360 Controller");
            Check(ConnectionDiagnostics.SelectionReason([], "", null) == "no_candidates", "empty enumeration");
            Check(ConnectionDiagnostics.SelectionReason([hvc], "hvc", hvc) == "exact_path", "exact match");
            Check(ConnectionDiagnostics.SelectionReason([hvc], "old", hvc) == "nintendo_name_fallback_unverified_identity", "fallback is not proven identity");
            Check(ConnectionDiagnostics.SelectionReason([xbox], "hvc", null) == "preferred_missing_or_ambiguous", "missing preference");
            Check(ConnectionDiagnostics.SelectionReason([xbox], "", xbox) == "automatic_first_device", "auto Xbox is not proof of virtual origin");

            var batches = new ConcurrentQueue<string>();
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var sink = new DiagnosticLogSink(batch =>
                {
                    entered.Set();
                    if (!release.Wait(3000)) throw new TimeoutException("test writer release");
                    batches.Enqueue(batch);
                }, capacity: 1);
                try
                {
                    Check(sink.TryWrite("first"), "initial enqueue");
                    Check(entered.Wait(3000), "writer started");
                    Check(sink.TryWrite("second"), "bounded queue fill");
                    Check(!sink.TryWrite("overflow"), "bounded queue rejects without blocking");
                    Check(sink.Dropped == 1, "overflow counted");
                }
                finally { release.Set(); sink.Dispose(); }
                Check(batches.Any(x => x.Contains("log-health") && x.Contains("dropped_total=1")), "loss visible");
                Check(!sink.TryWrite("after shutdown"), "shutdown rejects safely");
                sink.Dispose(); // Idempotent.
            }

            var calls = 0;
            using (var sink = new DiagnosticLogSink(batch =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new IOException("injected disk failure");
                batches.Enqueue(batch);
            }))
            {
                sink.TryWrite("disk-failure-test");
                Check(SpinWait.SpinUntil(() => sink.WriteFailures == 1, 3000), "failure counted");
                sink.TryWrite("recovered");
                Check(SpinWait.SpinUntil(() => batches.Any(x => x.Contains("write_failures_total=1")), 3000), "failure reported after recovery");
                Check(sink.LostLines >= 1, "lost lines counted");
            }

            using (var sink = new DiagnosticLogSink(_ => { }, 4))
            {
                Parallel.For(0, 500, i => { if (i == 250) sink.Dispose(); else sink.TryWrite("race"); });
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 20; }
    }
}
