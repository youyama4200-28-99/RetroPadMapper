namespace RetroPadMapper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--diagnostic-self-test"))
            return DiagnosticSelfTest.Run();
        if (args.Contains("--self-test"))
            return SelfTest.Run();
        if (args.Contains("--bluetooth-probe"))
        {
            try { BluetoothDiscovery.Probe(); return 0; }
            catch { return 8; }
        }
        var snapshotIndex = Array.IndexOf(args, "--ui-snapshot");
        if (snapshotIndex >= 0)
        {
            var output = snapshotIndex + 1 < args.Length ? args[snapshotIndex + 1] : "ui-snapshot";
            return UiSnapshot.Run(output);
        }
        var benchmarkIndex = Array.IndexOf(args, "--benchmark");
        if (benchmarkIndex >= 0)
        {
            var output = benchmarkIndex + 1 < args.Length ? args[benchmarkIndex + 1] : "benchmarks/latest";
            return DispatchLatencyBenchmark.Run(output);
        }
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => AppLog.Error("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.Error("Unobserved task exception", e.Exception); e.SetObserved(); };
        using var context = new TrayApplicationContext();
        Application.Run(context);
        return 0;
    }
}
