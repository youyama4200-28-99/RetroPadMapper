using System.Collections.Concurrent;
using System.Diagnostics;

namespace RetroPadMapper;

// Bounded, non-blocking producers. Only this worker calls the disk sink.
internal sealed class DiagnosticLogSink : IDisposable
{
    private readonly BlockingCollection<string> _queue;
    private readonly Action<string> _write;
    private readonly Task _worker;
    private long _dropped, _writeFailures, _lostLines;
    private int _stopped;
    internal long Dropped => Interlocked.Read(ref _dropped);
    internal long WriteFailures => Interlocked.Read(ref _writeFailures);
    internal long LostLines => Interlocked.Read(ref _lostLines);

    internal DiagnosticLogSink(Action<string> write, int capacity = 2048)
    {
        _write = write;
        _queue = new(new ConcurrentQueue<string>(), capacity);
        _worker = Task.Factory.StartNew(Run, TaskCreationOptions.LongRunning);
    }

    internal bool TryWrite(string line)
    {
        if (Volatile.Read(ref _stopped) != 0) { Interlocked.Increment(ref _dropped); return false; }
        try { if (_queue.TryAdd(line)) return true; }
        catch (InvalidOperationException) { } // Shutdown raced a producer.
        Interlocked.Increment(ref _dropped);
        return false;
    }

    private void Run()
    {
        long reportedDropped = 0, reportedFailures = 0, reportedLost = 0;
        while (!_queue.IsCompleted)
        {
            var lines = new List<string>(64);
            if (_queue.TryTake(out var first, 250))
            {
                lines.Add(first);
                while (lines.Count < 64 && _queue.TryTake(out var next)) lines.Add(next);
            }
            var dropped = Dropped; var failures = WriteFailures; var lost = LostLines;
            if (dropped != reportedDropped || failures != reportedFailures || lost != reportedLost)
                lines.Insert(0, $"{DateTimeOffset.Now:O} [WARN] log-health dropped_total={dropped} write_failures_total={failures} lost_lines_total={lost}");
            if (lines.Count == 0) continue;
            try
            {
                _write(string.Join(Environment.NewLine, lines) + Environment.NewLine);
                reportedDropped = dropped; reportedFailures = failures; reportedLost = lost;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _writeFailures);
                Interlocked.Add(ref _lostLines, lines.Count);
                Debug.WriteLine($"Diagnostic log write failed: {ex.GetType().Name}");
                Thread.Sleep(100); // Disk failures never delay an input producer.
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0) _queue.CompleteAdding();
        try { _worker.Wait(1500); } catch { }
    }
}
