using System.Diagnostics;

namespace RetroPadMapper;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ControllerService _controller;
    private readonly IndicatorForm _indicator;
    private readonly Label _status = new();
    private readonly Label _latency = new();
    private readonly Label _reconnectStatus = new();
    private readonly ComboBox _controllers = new();
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _debugMode = new();
    private readonly CheckBox _showIndicator = new();
    private readonly CheckBox _indicatorTopMost = new();
    private readonly ComboBox _indicatorStyle = new();
    private readonly Label _indicatorImageName = new();
    private readonly TableLayoutPanel _mappingTable = new();
    private readonly Dictionary<PadButton, ComboBox> _mappingCombos = [];
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 100 };
    private bool _updatingControllers;
    private bool _updatingMappings;
    private bool _buildingUi = true;
    private bool _reallyClose;
    private string _controllerSignature = "";

    public MainForm(AppSettings settings, SettingsStore store, ControllerService controller)
    {
        _settings = settings; _store = store; _controller = controller;
        _indicator = new IndicatorForm(controller, settings);
        Text = "RetroPad Mapper";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(840, 720);
        MinimumSize = new Size(700, 600);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BuildUi();
        _buildingUi = false;

        _controller.StatusChanged += name => PostUi(() => SetStatus(name));
        _controller.ControllersChanged += () => PostUi(RefreshControllerList);
        _controller.ReconnectStateChanged += message => PostUi(() => _reconnectStatus.Text = message);
        _indicator.FormClosing += OnIndicatorClosing;
        SetStatus(_controller.ControllerName);
        RefreshControllerList();
        _statusTimer.Tick += (_, _) =>
        {
            _controller.PumpHotplugEvents();
            _latency.Text = _controller.LatencySummary;
            RefreshControllerList();
        };
        _statusTimer.Start();
        FormClosing += OnFormClosing;
    }

    public void ApplyStartupIndicator()
    {
        if (_settings.ShowIndicator && !_indicator.Visible) _indicator.Show();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label { Text = "RetroPad Mapper", Font = new Font(Font.FontFamily, 20, FontStyle.Bold), AutoSize = true });
        root.Controls.Add(BuildDevicePanel());

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 6) };
        tabs.TabPages.Add(BuildMappingPage());
        tabs.TabPages.Add(BuildSettingsPage());
        root.Controls.Add(tabs);
        root.Controls.Add(new Label
        {
            Text = "変更は自動保存されます。閉じるとタスクトレイに常駐します。",
            AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 8, 0, 0)
        });
        Controls.Add(root);
    }

    private Control BuildDevicePanel()
    {
        var group = new GroupBox { Text = "入力コントローラー", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _controllers.DropDownStyle = ComboBoxStyle.DropDownList;
        _controllers.Dock = DockStyle.Fill;
        _controllers.DropDownWidth = 520;
        _controllers.SelectedIndexChanged += ControllerSelectionChanged;
        var refresh = new Button { Text = "再検出", AutoSize = true };
        refresh.Click += (_, _) => _controller.RefreshControllers();
        var reconnect = new Button { Text = "再接続を試す", AutoSize = true };
        reconnect.Click += async (_, _) =>
        {
            reconnect.Enabled = false;
            try { await _controller.RequestReconnectAsync(); }
            finally { reconnect.Enabled = true; }
        };
        var bluetooth = new LinkLabel { Text = "WindowsのBluetooth設定を開く", AutoSize = true, Anchor = AnchorStyles.Left };
        bluetooth.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });

        _status.AutoSize = true; _status.Anchor = AnchorStyles.Left;
        _latency.AutoSize = true; _latency.ForeColor = SystemColors.GrayText; _latency.Text = _controller.LatencySummary;
        _reconnectStatus.AutoSize = true; _reconnectStatus.ForeColor = Color.FromArgb(150, 75, 20);
        layout.Controls.Add(new Label { Text = "使用する機器", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_controllers, 1, 0); layout.Controls.Add(refresh, 2, 0); layout.Controls.Add(reconnect, 3, 0);
        layout.Controls.Add(_status, 0, 1); layout.SetColumnSpan(_status, 2);
        layout.Controls.Add(bluetooth, 2, 1); layout.SetColumnSpan(bluetooth, 2);
        layout.Controls.Add(_latency, 0, 2); layout.SetColumnSpan(_latency, 2);
        layout.Controls.Add(_reconnectStatus, 2, 2); layout.SetColumnSpan(_reconnectStatus, 2);
        group.Controls.Add(layout);
        return group;
    }

    private TabPage BuildMappingPage()
    {
        var page = new TabPage("キーマップ") { Padding = new Padding(12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _enabled.Text = "マッピングを有効にする"; _enabled.AutoSize = true; _enabled.Checked = _settings.MappingEnabled;
        _enabled.CheckedChanged += (_, _) => { _settings.MappingEnabled = _enabled.Checked; ApplyAndSave(); };
        layout.Controls.Add(_enabled);

        _mappingTable.Dock = DockStyle.Fill; _mappingTable.AutoScroll = true; _mappingTable.ColumnCount = 2;
        _mappingTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        _mappingTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        AddHeader("ファミコン側", "出力先");
        foreach (var button in OrderedButtons()) AddMappingRow(button);
        layout.Controls.Add(_mappingTable);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var defaults = new Button { Text = "初期マップに戻す", AutoSize = true };
        defaults.Click += (_, _) =>
        {
            _settings.Bindings = AppSettings.Defaults();
            RefreshMappingSelections();
            ApplyAndSave();
        };
        var clear = new Button { Text = "すべて「なし」", AutoSize = true };
        clear.Click += (_, _) =>
        {
            _settings.Bindings = OrderedButtons().ToDictionary(x => x, _ => OutputBinding.None);
            RefreshMappingSelections();
            ApplyAndSave();
        };
        actions.Controls.Add(defaults); actions.Controls.Add(clear);
        layout.Controls.Add(actions);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildSettingsPage()
    {
        var page = new TabPage("表示・起動") { Padding = new Padding(18) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        _showIndicator.Text = "ファミコン型キーマップインジケータを表示";
        _showIndicator.AutoSize = true; _showIndicator.Checked = _settings.ShowIndicator;
        _showIndicator.CheckedChanged += (_, _) =>
        {
            _settings.ShowIndicator = _showIndicator.Checked;
            _indicatorTopMost.Enabled = _showIndicator.Checked;
            if (_showIndicator.Checked) _indicator.Show(); else _indicator.Hide();
            Save();
        };
        _indicatorTopMost.Text = "インジケータを常に最前面に表示";
        _indicatorTopMost.AutoSize = true; _indicatorTopMost.Checked = _settings.IndicatorTopMost; _indicatorTopMost.Enabled = _settings.ShowIndicator;
        _indicatorTopMost.CheckedChanged += (_, _) =>
        {
            _settings.IndicatorTopMost = _indicatorTopMost.Checked;
            _indicator.TopMost = _settings.IndicatorTopMost;
            Save();
        };
        _indicatorStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        _indicatorStyle.Width = 180;
        _indicatorStyle.Items.AddRange(["ファミコン", "NES", "汎用", "任意画像"]);
        _indicatorStyle.SelectedIndex = (int)_settings.IndicatorStyle;
        var chooseImage = new Button { Text = "画像を選択…", AutoSize = true, Enabled = _settings.IndicatorStyle == IndicatorStyle.CustomImage };
        _indicatorImageName.AutoSize = true; _indicatorImageName.Anchor = AnchorStyles.Left;
        _indicatorImageName.Text = string.IsNullOrWhiteSpace(_settings.IndicatorImagePath) ? "未選択" : Path.GetFileName(_settings.IndicatorImagePath);
        _indicatorStyle.SelectedIndexChanged += (_, _) =>
        {
            if (_indicatorStyle.SelectedIndex < 0) return;
            _settings.IndicatorStyle = (IndicatorStyle)_indicatorStyle.SelectedIndex;
            chooseImage.Enabled = _settings.IndicatorStyle == IndicatorStyle.CustomImage;
            _indicator.Indicator.AppearanceChanged(); Save();
        };
        chooseImage.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "インジケータ画像を選択" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _settings.IndicatorImagePath = _store.ImportIndicatorImage(dialog.FileName);
                _settings.IndicatorStyle = IndicatorStyle.CustomImage;
                _indicatorStyle.SelectedIndex = (int)IndicatorStyle.CustomImage;
                _indicatorImageName.Text = Path.GetFileName(_settings.IndicatorImagePath);
                _indicator.Indicator.AppearanceChanged(); Save();
            }
            catch (Exception ex) { AppLog.Error("custom indicator image import failed", ex); MessageBox.Show(ex.Message, "画像の読み込みに失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var appearance = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        appearance.Controls.Add(new Label { Text = "プレビュー外観", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 6, 3) });
        appearance.Controls.Add(_indicatorStyle); appearance.Controls.Add(chooseImage); appearance.Controls.Add(_indicatorImageName);
        _autoStart.Text = "Windowsログイン時に自動起動"; _autoStart.AutoSize = true; _autoStart.Checked = AutoStartService.IsEnabled();
        _autoStart.CheckedChanged += (_, _) =>
        {
            try { AutoStartService.SetEnabled(_autoStart.Checked); }
            catch (Exception ex) { AppLog.Error("auto-start setting failed", ex); MessageBox.Show(ex.Message, "自動起動の設定に失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        _debugMode.Text = "デバッグモード（診断ログを出力）";
        _debugMode.AutoSize = true; _debugMode.Checked = _settings.DebugMode;
        _debugMode.CheckedChanged += (_, _) =>
        {
            _settings.DebugMode = _debugMode.Checked;
            AppLog.Configure(_settings.DebugMode);
            Save();
        };
        var logs = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var openLogs = new Button { Text = "ログフォルダーを開く", AutoSize = true };
        openLogs.Click += (_, _) => AppLog.OpenDirectory();
        logs.Controls.Add(openLogs);
        logs.Controls.Add(new Label { Text = AppLog.DirectoryPath, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 7, 3, 3) });
        flow.Controls.Add(_showIndicator); flow.Controls.Add(_indicatorTopMost); flow.Controls.Add(appearance); flow.Controls.Add(_autoStart); flow.Controls.Add(_debugMode); flow.Controls.Add(logs);
        flow.Controls.Add(new Label
        {
            Text = "インジケータは入力処理とは別のUIタイマーで描画されるため、最前面表示を有効にしてもマッピング処理を待たせません。",
            MaximumSize = new Size(720, 0), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 16, 3, 3)
        });
        page.Controls.Add(flow);
        return page;
    }

    private void AddHeader(string left, string right)
    {
        _mappingTable.Controls.Add(new Label { Text = left, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 4, 0, 8) });
        _mappingTable.Controls.Add(new Label { Text = right, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 4, 0, 8) });
    }

    private void AddMappingRow(PadButton button)
    {
        var label = new Label { Text = DisplayName(button), AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 5, 0, 5) };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 300, MaxDropDownItems = 16 };
        combo.Items.AddRange(OutputOptions().Cast<object>().ToArray());
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_buildingUi || _updatingMappings) return;
            if (combo.SelectedItem is OutputBinding binding)
            {
                _settings.Bindings[button] = binding;
                ApplyAndSave();
            }
        };
        _mappingCombos[button] = combo;
        _mappingTable.Controls.Add(label); _mappingTable.Controls.Add(combo);
        SetMappingSelection(button, combo);
    }

    private void ControllerSelectionChanged(object? sender, EventArgs e)
    {
        if (_updatingControllers || _controllers.SelectedItem is not ControllerOption selected) return;
        _settings.PreferredController = selected.Id;
        _controller.SelectController(selected.Id);
        Save();
    }

    private void RefreshControllerList()
    {
        var available = _controller.AvailableControllers;
        var signature = string.Join('\n', available.Select(x => $"{x.Id}|{x.Name}")) + "|" + _settings.PreferredController;
        if (signature == _controllerSignature) return;
        _controllerSignature = signature;
        _updatingControllers = true;
        try
        {
            _controllers.Items.Clear();
            _controllers.Items.Add(new ControllerOption("", 0, "自動選択（Nintendo / Famicomを優先）"));
            foreach (var option in available) _controllers.Items.Add(option);
            var preferred = _settings.PreferredController;
            if (preferred.Length > 0 && available.All(x => x.Id != preferred))
                _controllers.Items.Add(new ControllerOption(preferred, 0, "選択したコントローラー（現在未接続）"));
            _controllers.SelectedItem = _controllers.Items.Cast<ControllerOption>().First(x => x.Id == preferred);
        }
        finally { _updatingControllers = false; }
    }

    private void RefreshMappingSelections()
    {
        _updatingMappings = true;
        try { foreach (var (button, combo) in _mappingCombos) SetMappingSelection(button, combo); }
        finally { _updatingMappings = false; }
    }

    private void SetMappingSelection(PadButton button, ComboBox combo)
    {
        var current = _settings.Bindings.GetValueOrDefault(button, OutputBinding.None);
        combo.SelectedItem = combo.Items.Cast<OutputBinding>().FirstOrDefault(x => x.Kind == current.Kind && x.Code == current.Code) ?? OutputBinding.None;
    }

    private void ApplyAndSave() { _controller.Configure(_settings); _indicator.Indicator.BindingsChanged(); Save(); }
    private void Save() => _store.Save(_settings);
    private void SetStatus(string name) => _status.Text = name == "未接続" ? "● 未接続" : $"● 接続中: {name}";
    private void PostUi(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }

    private void OnIndicatorClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyClose) return;
        e.Cancel = true;
        _indicator.Hide();
        _settings.ShowIndicator = false;
        _showIndicator.Checked = false;
        SaveIndicatorPosition(); Save();
    }

    private void SaveIndicatorPosition()
    {
        if (_indicator.WindowState == FormWindowState.Normal)
        {
            _settings.IndicatorX = _indicator.Left;
            _settings.IndicatorY = _indicator.Top;
        }
    }

    public void AllowClose()
    {
        _reallyClose = true;
        SaveIndicatorPosition(); Save();
        _indicator.Close();
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_reallyClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
    }

    private static PadButton[] OrderedButtons() => [PadButton.Up, PadButton.Down, PadButton.Left, PadButton.Right, PadButton.A, PadButton.B, PadButton.X, PadButton.Y, PadButton.Start, PadButton.Select, PadButton.L, PadButton.R, PadButton.Home];
    private static string DisplayName(PadButton b) => b switch { PadButton.Select => "SELECT / −", PadButton.Start => "START / ＋", PadButton.Home => "HOME", _ => b.ToString() };

    private static List<OutputBinding> OutputOptions()
    {
        var list = new List<OutputBinding> { OutputBinding.None };
        var keys = new[] { Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Enter, Keys.Space, Keys.Escape, Keys.Tab, Keys.Back, Keys.ControlKey, Keys.ShiftKey, Keys.Menu };
        keys = keys.Concat(Enumerable.Range('A', 26).Select(x => (Keys)x)).Concat(Enumerable.Range('0', 10).Select(x => (Keys)x)).Concat(Enumerable.Range((int)Keys.F1, 12).Select(x => (Keys)x)).Distinct().ToArray();
        list.AddRange(keys.Select(k => new OutputBinding(OutputKind.Key, (int)k, $"キー: {k}")));
        list.Add(new(OutputKind.MouseButton, 0, "マウス: 左クリック")); list.Add(new(OutputKind.MouseButton, 1, "マウス: 右クリック")); list.Add(new(OutputKind.MouseButton, 2, "マウス: 中クリック"));
        list.Add(new(OutputKind.MouseWheel, 120, "マウス: ホイール上")); list.Add(new(OutputKind.MouseWheel, -120, "マウス: ホイール下"));
        return list;
    }
}
