namespace RetroPadMapper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
            return SelfTest.Run();
        var benchmarkIndex = Array.IndexOf(args, "--benchmark");
        if (benchmarkIndex >= 0)
        {
            var output = benchmarkIndex + 1 < args.Length ? args[benchmarkIndex + 1] : "benchmarks/latest";
            return DispatchLatencyBenchmark.Run(output);
        }
        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        Application.Run(context);
        return 0;
    }
}
