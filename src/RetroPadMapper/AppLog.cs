using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace RetroPadMapper;

internal static class AppLog
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 2048);
    private static readonly object Gate = new();
    private static Task? _writer;
    private static volatile bool _enabled;
    internal static string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Assembly.GetEntryAssembly()?.GetName().Name ?? "RetroPadMapper", "Logs");
    internal static void Configure(bool enabled) { _enabled = enabled; if (!enabled) return; lock (Gate) _writer ??= Task.Factory.StartNew(WriteLoop, TaskCreationOptions.LongRunning); Info("debug logging enabled"); }
    internal static void Info(string message) => Write("INFO", message);
    internal static void Debug(string message) => Write("DEBUG", message);
    internal static void Error(string message, Exception? exception = null) => Write("ERROR", exception is null ? message : $"{message}: {exception}");
    internal static void OpenDirectory() { Directory.CreateDirectory(DirectoryPath); Process.Start(new ProcessStartInfo(DirectoryPath) { UseShellExecute = true }); }
    private static void Write(string level, string message) { if (_enabled) Queue.TryAdd($"{DateTimeOffset.Now:O} [{level}] [T{Environment.CurrentManagedThreadId}] {message}"); }
    private static void WriteLoop() { Directory.CreateDirectory(DirectoryPath); foreach (var line in Queue.GetConsumingEnumerable()) try { File.AppendAllText(Path.Combine(DirectoryPath, $"RetroPadMapper-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine, new UTF8Encoding(false)); } catch { } }
    internal static void Shutdown() { if (_writer is null) return; Queue.CompleteAdding(); try { _writer.Wait(1000); } catch { } }
}
