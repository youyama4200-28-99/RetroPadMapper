namespace RetroPadMapper;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ControllerService _controller = new();
    private readonly MainForm _form;

    public TrayApplicationContext()
    {
        var store = new SettingsStore(); var settings = store.Load();
        AppLog.Configure(settings.DebugMode);
        AppLog.Info("application starting");
        _controller.Configure(settings);
        _form = new MainForm(settings, store, _controller);
        var menu = new ContextMenuStrip();
        menu.Items.Add("設定を開く", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Exit());
        _tray = new NotifyIcon { Text = "RetroPad Mapper", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowWindow();
        if (!_controller.Start())
            MessageBox.Show($"SDL3の初期化に失敗しました: {SdlNative.Utf8(SdlNative.GetError())}", "RetroPad Mapper", MessageBoxButtons.OK, MessageBoxIcon.Error);
        _form.ApplyStartupIndicator();
        var minimized = settings.StartMinimized || Environment.GetCommandLineArgs().Contains("--minimized");
        if (!minimized) ShowWindow();
    }

    private void ShowWindow() { _form.Show(); _form.WindowState = FormWindowState.Normal; _form.Activate(); }
    private void Exit() { _tray.Visible = false; _form.AllowClose(); ExitThread(); }
    protected override void Dispose(bool disposing) { if (disposing) { _tray.Dispose(); _controller.Dispose(); _form.Dispose(); AppLog.Info("application stopped"); AppLog.Shutdown(); } base.Dispose(disposing); }
}
