using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace RetroPadMapper;

internal sealed class ProbeRun : IDisposable
{
    private readonly Process _process = new();
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly object _logGate = new();
    private readonly StreamWriter _log;
    private readonly long _started = Environment.TickCount64;
    private long _progress = Environment.TickCount64;
    private readonly int _seconds, _silenceMs;
    private bool _disposed;
    private int _outputClosed, _errorClosed;
    internal string LogPath { get; }
    internal string? StopReason { get; private set; }
    // Process exit alone does not mean async pipe callbacks have delivered the final lines.
    internal bool Exited => _process.HasExited && Volatile.Read(ref _outputClosed) != 0 && Volatile.Read(ref _errorClosed) != 0;
    internal int ExitCode => _process.ExitCode;

    internal ProbeRun(string backend, int seconds, string directory, int silenceMs = 15000, bool hangTest = false, bool pipeTest = false)
    {
        _seconds = seconds; _silenceMs = silenceMs;
        Directory.CreateDirectory(directory);
        LogPath = Path.Combine(directory, $"probe-{backend}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        _log = new StreamWriter(new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        var host = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable");
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        if (hangTest) start.ArgumentList.Add("--hang-test");
        else if (pipeTest) start.ArgumentList.Add("--pipe-test");
        else
        {
            start.ArgumentList.Add("--worker"); start.ArgumentList.Add(backend); start.ArgumentList.Add(seconds.ToString());
        }
        _process.StartInfo = start;
        _process.OutputDataReceived += (_, e) => {
            if (e.Data is null) Volatile.Write(ref _outputClosed, 1);
            else Receive(e.Data);
        };
        _process.ErrorDataReceived += (_, e) => {
            if (e.Data is null) Volatile.Write(ref _errorClosed, 1);
            else Receive($"stderr: {e.Data}");
        };
        try
        {
            _process.Start();
            _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
        }
        catch { _log.Dispose(); _process.Dispose(); throw; }
    }

    private void Receive(string? value)
    {
        if (value is null) return;
        Interlocked.Exchange(ref _progress, Environment.TickCount64);
        lock (_logGate)
        {
            if (_disposed) return;
            try { _log.WriteLine(value); }
            catch (IOException) { if (_messages.Count < 2000) _messages.Enqueue("ログの保存に失敗しました"); }
        }
        if (_messages.Count < 2000) _messages.Enqueue(value);
    }

    internal bool TryRead(out string? message) => _messages.TryDequeue(out message);

    internal void CheckTimeout()
    {
        if (Exited || StopReason is not null) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _progress) > _silenceMs) Stop("watchdog_no_progress");
        else if (now - _started > (_seconds + 20L) * 1000) Stop("watchdog_total_limit");
    }

    internal void Stop(string reason = "user_stop")
    {
        if (Exited || StopReason is not null) return;
        StopReason = reason;
        Receive(System.Text.Json.JsonSerializer.Serialize(new { time = DateTimeOffset.Now, kind = "supervisor_stop", reason }));
        // This object owns exactly this diagnostic child. Never terminate another application.
        try { _process.Kill(); } catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!Exited) Stop("supervisor_closed");
        lock (_logGate) { _disposed = true; _log.Dispose(); }
        _process.Dispose();
    }
}
