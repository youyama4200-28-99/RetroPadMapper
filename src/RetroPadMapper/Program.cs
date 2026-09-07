namespace RetroPadMapper;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (Environment.GetCommandLineArgs().Contains("--self-test"))
            return SelfTest.Run();
        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        Application.Run(context);
        return 0;
    }
}
