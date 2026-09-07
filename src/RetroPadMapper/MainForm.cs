namespace RetroPadMapper;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ControllerService _controller;
    private readonly Label _status = new();
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _autoStart = new();
    private readonly TableLayoutPanel _table = new();
    private bool _reallyClose;

    public MainForm(AppSettings settings, SettingsStore store, ControllerService controller)
    {
        _settings = settings; _store = store; _controller = controller;
        Text = "RetroPad Mapper"; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(580, 610); MinimumSize = new Size(520, 500);
        Font = new Font("Segoe UI", 10);
        BuildUi();
        _controller.StatusChanged += name => BeginInvoke(() => SetStatus(name));
        SetStatus(_controller.ControllerName);
        FormClosing += OnFormClosing;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 6, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = "RetroPad Mapper", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true };
        _status.AutoSize = true; _status.Padding = new Padding(0, 5, 0, 12);
        _enabled.Text = "マッピングを有効にする"; _enabled.AutoSize = true; _enabled.Checked = _settings.MappingEnabled;
        _enabled.CheckedChanged += (_, _) => { _settings.MappingEnabled = _enabled.Checked; ApplyAndSave(); };
        _table.Dock = DockStyle.Fill; _table.AutoScroll = true; _table.ColumnCount = 2;
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        AddHeader("コントローラー", "出力");
        foreach (var button in OrderedButtons()) AddMappingRow(button);
        _autoStart.Text = "Windowsログイン時に自動起動"; _autoStart.AutoSize = true; _autoStart.Checked = AutoStartService.IsEnabled();
        _autoStart.CheckedChanged += (_, _) => { try { AutoStartService.SetEnabled(_autoStart.Checked); } catch (Exception ex) { MessageBox.Show(ex.Message, "自動起動の設定に失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
        var hint = new Label { Text = "閉じるとタスクトレイに常駐します。変更は自動保存されます。", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 8, 0, 0) };
        root.Controls.Add(title); root.Controls.Add(_status); root.Controls.Add(_enabled); root.Controls.Add(_table); root.Controls.Add(_autoStart); root.Controls.Add(hint);
        Controls.Add(root);
    }

    private void AddHeader(string left, string right)
    {
        _table.Controls.Add(new Label { Text = left, Font = new Font(Font, FontStyle.Bold), AutoSize = true });
        _table.Controls.Add(new Label { Text = right, Font = new Font(Font, FontStyle.Bold), AutoSize = true });
    }

    private void AddMappingRow(PadButton button)
    {
        var label = new Label { Text = DisplayName(button), AutoSize = true, Anchor = AnchorStyles.Left };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        var options = OutputOptions(); combo.Items.AddRange(options.Cast<object>().ToArray());
        var current = _settings.Bindings.GetValueOrDefault(button, OutputBinding.None);
        combo.SelectedItem = options.FirstOrDefault(x => x.Kind == current.Kind && x.Code == current.Code) ?? options[0];
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is OutputBinding b) { _settings.Bindings[button] = b; ApplyAndSave(); } };
        _table.Controls.Add(label); _table.Controls.Add(combo);
    }

    private void ApplyAndSave() { _controller.Configure(_settings); _store.Save(_settings); }
    private void SetStatus(string name) => _status.Text = name == "未接続" ? "● コントローラー未接続" : $"● 接続中: {name}";
    public void AllowClose() { _reallyClose = true; Close(); }
    private void OnFormClosing(object? sender, FormClosingEventArgs e) { if (!_reallyClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } }

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
