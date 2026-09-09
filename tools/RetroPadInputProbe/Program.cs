using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RetroPadMapper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] != "--ui-snapshot") Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 1 && args[0] == "--hang-test")
        {
            Console.WriteLine("{\"kind\":\"injected_hang\"}"); Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite); return 99;
        }
        if (args.Length == 3 && args[0] == "--worker" && int.TryParse(args[2], out var seconds))
            return ProbeWorker.Run(args[1], seconds);
        if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
        if (args.Length == 3 && args[0] == "--run" && int.TryParse(args[2], out var runSeconds) && runSeconds is >= 1 and <= 180 && args[1] is "windows" or "hidapi")
        {
            using var run = new ProbeRun(args[1], runSeconds, Path.Combine(AppContext.BaseDirectory, "logs"));
            while (!run.Exited) { run.CheckTimeout(); Thread.Sleep(50); }
            while (run.TryRead(out var message)) Console.WriteLine(message);
            Console.WriteLine($"Log: {run.LogPath}");
            return run.StopReason is null ? run.ExitCode : 124;
        }
        if (args.Length != 0 && args[0] != "--ui-snapshot") return 2;
        ApplicationConfiguration.Initialize();
        using var form = new ProbeForm();
        if (args.Length == 2 && args[0] == "--ui-snapshot")
        {
            form.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args[1]); form.Close(); return 0;
        }
        Application.Run(form);
        return 0;
    }

    private static int SelfTest()
    {
        try
        {
            if (!ProbeWorker.IsCandidate(0x057e, 0x2007) || ProbeWorker.IsCandidate(0x045e, 0x028e)) return 20;
            if (string.IsNullOrEmpty(SdlNative.Utf8(SdlNative.GetRevision()))) return 21;
            using var run = new ProbeRun("test", 1, Path.Combine(AppContext.BaseDirectory, "test-logs"), silenceMs: 750, hangTest: true);
            var deadline = Environment.TickCount64 + 5000;
            while (!run.Exited && Environment.TickCount64 < deadline) { run.CheckTimeout(); Thread.Sleep(25); }
            if (!run.Exited || run.StopReason != "watchdog_no_progress") return 22;
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 23; }
    }
}

internal sealed class ProbeForm : Form
{
    private readonly Label _status = new() { AutoSize = true, Text = "待機中：本体アプリを終了してから開始してください。" };
    private readonly TextBox _history = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button _windows = new() { Text = "Windows標準で開始", AutoSize = true };
    private readonly Button _hidapi = new() { Text = "従来のSDL経路で開始", AutoSize = true };
    private readonly Button _stop = new() { Text = "診断を停止", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private ProbeRun? _run;

    internal ProbeForm()
    {
        Text = "RetroPad 入力専用診断";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(800, 500);
        MinimumSize = new Size(650, 420);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label {
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 12),
            Text = "キー送信・仮想コントローラー生成・ペアリング変更は行いません。\n本体アプリを終了し、HVCを起動してA/Bを押してください（観測120秒）。\nWindows標準は「入力の読み取り方式」です。出力のDirectInput設定とは別です。"
        });
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([_windows, _hidapi, _stop]); layout.Controls.Add(buttons);
        _status.Padding = new Padding(0, 8, 0, 10); layout.Controls.Add(_status);
        layout.Controls.Add(_history);
        var logs = new LinkLabel { Text = "診断ログのフォルダーを開く", AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        logs.LinkClicked += (_, _) => {
            var path = Path.Combine(AppContext.BaseDirectory, "logs");
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        };
        layout.Controls.Add(logs); Controls.Add(layout);
        _windows.Click += (_, _) => Start("windows");
        _hidapi.Click += (_, _) => Start("hidapi");
        _stop.Click += (_, _) => _run?.Stop();
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        FormClosed += (_, _) => { _timer.Stop(); _run?.Dispose(); _timer.Dispose(); };
    }

    private void Start(string backend)
    {
        if (_run is not null) return;
        foreach (var name in new[] { "RetroPadMapper", "RetroPadMapper-Universal", "RetroPadMapper-XInput", "RetroPadMapper-DirectInput" })
        {
            var processes = Process.GetProcessesByName(name);
            var found = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (found) { _status.Text = "本体アプリが起動中です。タスクトレイから終了してください。"; return; }
        }
        try
        {
            _run = new ProbeRun(backend, 120, Path.Combine(AppContext.BaseDirectory, "logs"));
            _windows.Enabled = _hidapi.Enabled = false; _stop.Enabled = true;
            _status.Text = "診断中：HVCのA/Bを押してください。応答停止は15秒で打ち切ります。";
            _history.Clear();
        }
        catch (Exception ex) { _status.Text = $"開始できませんでした：{ex.Message}"; }
    }

    private void Poll()
    {
        if (_run is null) return;
        for (var i = 0; i < 100 && _run.TryRead(out var message); i++)
        {
            if (message is null) continue;
            try
            {
                using var json = JsonDocument.Parse(message);
                var kind = json.RootElement.GetProperty("kind").GetString();
                var description = kind switch
                {
                    "initializing" => "入力機器を探索しています。",
                    "inventory" => $"一覧更新：{json.RootElement.GetProperty("data").GetArrayLength()}台",
                    "open_begin" => "HVC候補の入力を開いています。",
                    "open_end" => json.RootElement.GetProperty("data").GetProperty("success").GetBoolean()
                        ? "入力を開けました。A/Bを押して確認してください。"
                        : $"入力を開けません：{json.RootElement.GetProperty("data").GetProperty("error").GetString()}",
                    "input_change" => "● ボタン／十字キーの状態変化を受信しました。",
                    "disconnected" => "コントローラーの切断を検出しました。",
                    "result" => json.RootElement.GetProperty("data").GetProperty("observed").GetBoolean()
                        ? "診断終了：入力変化を受信。押したボタンと対応したか確認してください。"
                        : "診断終了：入力変化を確認できませんでした。",
                    "supervisor_stop" => "診断用の子プロセスだけを停止しました。",
                    "error" or "init_failed" or "enumeration_failed" => "診断エラー：詳細はログに記録しました。",
                    _ => null
                };
                if (description is not null) _history.AppendText(description + Environment.NewLine);
            }
            catch (JsonException) { _history.AppendText(message + Environment.NewLine); }
        }
        _run.CheckTimeout();
        if (_run.Exited)
        {
            _status.Text = _run.StopReason is not null
                ? "診断を打ち切りました。原因の確認にログを使えます。"
                : $"診断終了（結果コード {_run.ExitCode}）。入力の受信状況は下の履歴を確認してください。";
            _run.Dispose(); _run = null;
            _windows.Enabled = _hidapi.Enabled = true; _stop.Enabled = false;
        }
    }
}
