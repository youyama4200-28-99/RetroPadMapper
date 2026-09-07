namespace RetroPadMapper;

internal sealed class LatencyRecorder
{
    private const int Capacity = 4096;
    private readonly long[] _dispatchNs = new long[Capacity];
    private readonly long[] _emitNs = new long[Capacity];
    private long _sequence;

    public void Record(ulong eventTimestamp, ulong beforeEmit, ulong afterEmit)
    {
        var index = Interlocked.Increment(ref _sequence) - 1;
        var slot = (int)(index % Capacity);
        var dispatch = beforeEmit >= eventTimestamp ? beforeEmit - eventTimestamp : 0;
        var emit = afterEmit >= beforeEmit ? afterEmit - beforeEmit : 0;
        Volatile.Write(ref _dispatchNs[slot], checked((long)Math.Min(dispatch, (ulong)long.MaxValue)));
        Volatile.Write(ref _emitNs[slot], checked((long)Math.Min(emit, (ulong)long.MaxValue)));
    }

    public string Summary()
    {
        var count = (int)Math.Min(Volatile.Read(ref _sequence), Capacity);
        if (count == 0) return "入力遅延: 測定待ち";
        var dispatch = _dispatchNs.Take(count).Select(x => Volatile.Read(ref x)).Where(x => x >= 0).Order().ToArray();
        var emit = _emitNs.Take(count).Select(x => Volatile.Read(ref x)).Where(x => x >= 0).Order().ToArray();
        return $"入力遅延 (直近{Math.Min(dispatch.Length, emit.Length)}件): SDL→処理 p50 {Us(dispatch, .50):F2} µs / p95 {Us(dispatch, .95):F2} µs、出力 p95 {Us(emit, .95):F2} µs";
    }

    private static double Us(long[] values, double percentile)
    {
        if (values.Length == 0) return 0;
        var index = Math.Clamp((int)Math.Ceiling(percentile * values.Length) - 1, 0, values.Length - 1);
        return values[index] / 1_000d;
    }
}
