using System.Diagnostics;
using System.Globalization;

namespace RetroPadMapper;

// Shared by all editions. Polling stores only numeric counters; summaries run during scans.
internal sealed class ConnectionDiagnostics
{
    private long _lastPump, _lastPoll, _polls, _maxGap, _maxLock, _maxUpdate;
    private long _lastSummary;
    private int _inventoryEpoch, _decisionEpoch, _inputEpoch;
    private string _inventory = "", _decision = "";
    private bool _sawPress;
    private uint _pendingInputInstance;
    private long _pendingInputAt;
    internal string OutputMode { get; set; } = "KeyboardMouse";
    internal bool MappingEnabled { get; set; }
    internal static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    internal static string EventAge(ulong timestamp, ulong now) =>
        timestamp == 0 || timestamp > now ? "unknown" :
        ((now - timestamp) / 1_000_000.0).ToString("F3", CultureInfo.InvariantCulture);

    internal void PumpStarted()
    {
        if (!AppLog.Enabled) { _lastPump = 0; return; }
        var now = Stopwatch.GetTimestamp();
        if (_lastPump != 0 && Milliseconds(now - _lastPump) > 500)
            AppLog.Debug($"ui-pump gap_ms={Milliseconds(now - _lastPump):F3}; scheduling_or_lock_delay_not_radio_duration");
        _lastPump = now;
    }

    internal long BeginPoll()
    {
        if (!AppLog.Enabled) { _lastPoll = 0; return 0; }
        var now = Stopwatch.GetTimestamp();
        if (_lastPoll != 0) _maxGap = Math.Max(_maxGap, now - _lastPoll);
        _lastPoll = now;
        return now;
    }

    internal long LockAcquired(long started)
    {
        if (started == 0) return 0;
        var now = Stopwatch.GetTimestamp();
        _maxLock = Math.Max(_maxLock, now - started);
        return now;
    }

    internal void Updated(long started)
    {
        if (started == 0) return;
        _maxUpdate = Math.Max(_maxUpdate, Stopwatch.GetTimestamp() - started);
        _polls++;
    }

    internal void NewConnection() => _sawPress = false;

    internal void Sample(int mask, uint instance)
    {
        if (!AppLog.Enabled) return;
        if (_inputEpoch != AppLog.Epoch) { _inputEpoch = AppLog.Epoch; _sawPress = false; }
        if (_sawPress || mask == 0) return;
        _sawPress = true;
        _pendingInputInstance = instance;
        _pendingInputAt = Stopwatch.GetTimestamp();
    }

    internal void Inventory(IReadOnlyList<ControllerOption> options)
    {
        if (!AppLog.Enabled) return;
        var value = string.Join(" | ", options.Select(x => $"instance={x.InstanceId} name={x.Name} vid={x.VendorId:X4} pid={x.ProductId:X4} path={x.Id}"));
        if (_inventoryEpoch == AppLog.Epoch && value == _inventory) return;
        _inventoryEpoch = AppLog.Epoch; _inventory = value;
        AppLog.Debug($"inventory count={options.Count} devices=[{value}]; virtual_origin=unverified");
    }

    internal void Decision(string reason, string preferred, ControllerOption? selected, uint current = 0)
    {
        if (!AppLog.Enabled) return;
        var value = $"selection reason={reason} preferred={preferred} candidate_instance={selected?.InstanceId} candidate_path={selected?.Id} current_instance={current}";
        if (_decisionEpoch == AppLog.Epoch && _decision == value) return;
        _decisionEpoch = AppLog.Epoch; _decision = value;
        AppLog.Debug(value);
    }

    internal static string SelectionReason(IReadOnlyList<ControllerOption> options, string preferred, ControllerOption? selected) =>
        selected is null ? (options.Count == 0 ? "no_candidates" : "preferred_missing_or_ambiguous") :
        preferred.Length > 0 ? (selected.Id == preferred ? "exact_path" : "nintendo_name_fallback_unverified_identity") :
        ControllerService.IsRetroNintendo(selected.Name) ? "automatic_nintendo" : "automatic_first_device";

    internal void Flush(long scanStarted)
    {
        if (!AppLog.Enabled || scanStarted == 0) return;
        var now = Stopwatch.GetTimestamp();
        if (_pendingInputInstance != 0)
        {
            AppLog.Info($"input-evidence instance={_pendingInputInstance} observed_mono={_pendingInputAt} first_nonzero_button_snapshot=true; deferred_log_not_a_raw_report_or_output_confirmation");
            _pendingInputInstance = 0;
        }
        var scanMs = Milliseconds(now - scanStarted);
        if (scanMs >= 50) AppLog.Debug($"scan-slow duration_ms={scanMs:F3}; shares_input_worker=true");
        if (_lastSummary != 0 && Milliseconds(now - _lastSummary) < 5000) return;
        _lastSummary = now;
        AppLog.Debug($"input-worker-summary output_mode={OutputMode} mapping_enabled={MappingEnabled} polls={_polls} max_loop_gap_ms={Milliseconds(_maxGap):F3} max_gate_wait_ms={Milliseconds(_maxLock):F3} max_sdl_update_ms={Milliseconds(_maxUpdate):F3}; includes_scan_and_disconnected_waits; not_end_to_end_latency");
        _polls = _maxGap = _maxLock = _maxUpdate = 0;
    }
}
