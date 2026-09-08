using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace RetroPadMapper;

internal static class AppLog
{
    private static readonly object Gate = new();
    private static DiagnosticLogSink? _sink;
    private static volatile bool _enabled;
    private static bool _stopped;
    private static int _epoch;
    internal static bool Enabled => _enabled;
    internal static int Epoch => Volatile.Read(ref _epoch);
    internal static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Assembly.GetEntryAssembly()?.GetName().Name ?? "RetroPadMapper", "Logs");

    internal static void Configure(bool enabled)
    {
        lock (Gate)
        {
            if (_stopped) return;
            if (enabled && !_enabled)
            {
                _sink ??= new DiagnosticLogSink(batch =>
                {
                    Directory.CreateDirectory(DirectoryPath);
                    File.AppendAllText(Path.Combine(DirectoryPath, $"RetroPadMapper-{DateTime.Now:yyyyMMdd}.log"), batch, new UTF8Encoding(false));
                });
                Interlocked.Increment(ref _epoch);
            }
            _enabled = enabled;
        }
        if (enabled)
            Info($"diagnostic-session version={Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} app={Assembly.GetEntryAssembly()?.GetName().Name} pid={Environment.ProcessId} os={Environment.OSVersion} runtime={Environment.Version} epoch={Epoch}; device paths may identify hardware; no typed keys recorded");
    }

    internal static void Info(string message) => Write("INFO", message);
    internal static void Debug(string message) => Write("DEBUG", message);
    internal static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    internal static void OpenDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
        Process.Start(new ProcessStartInfo(DirectoryPath) { UseShellExecute = true });
    }

    private static void Write(string level, string message)
    {
        if (!_enabled) return;
        _sink?.TryWrite($"{DateTimeOffset.Now:O} [mono={Stopwatch.GetTimestamp()} freq={Stopwatch.Frequency}] [{level}] [T{Environment.CurrentManagedThreadId}] {message}");
    }

    internal static void Shutdown()
    {
        lock (Gate) { _enabled = false; _stopped = true; }
        _sink?.Dispose();
    }
}
