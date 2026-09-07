using System.Runtime.InteropServices;

namespace RetroPadMapper;

internal sealed unsafe class ControllerService : IDisposable
{
    private readonly InputEmitter _emitter = new();
    private readonly LatencyRecorder _latency = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _scanSignal = new(false);
    private readonly object _gate = new();
    private readonly SdlNative.EventFilter _eventFilter;
    private Dictionary<PadButton, OutputBinding> _bindings = [];
    private bool _enabled;
    private bool _disposed;
    private nint _gamepad;
    private uint _instanceId;
    private string _name = "未接続";
    private Task? _loop;
    private GCHandle _selfHandle;

    public ControllerService() => _eventFilter = EventWatch;

    public event Action<string>? StatusChanged;
    public string ControllerName { get { lock (_gate) return _name; } }
    public string LatencySummary => _latency.Summary();

    public bool Start()
    {
        if (!SdlNative.Init(SdlNative.InitGamepad)) return false;
        SdlNative.SetGamepadEventsEnabled(true);
        _selfHandle = GCHandle.Alloc(this);
        if (!SdlNative.AddEventWatch(_eventFilter, GCHandle.ToIntPtr(_selfHandle)))
        {
            _selfHandle.Free();
            SdlNative.Quit();
            return false;
        }
        _loop = Task.Factory.StartNew(ScanLoop, _stop.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return true;
    }

    public void Configure(AppSettings settings)
    {
        lock (_gate)
        {
            _enabled = settings.MappingEnabled;
            _bindings = new(settings.Bindings);
            if (!_enabled) _emitter.ReleaseAll();
        }
    }

    private void ScanLoop()
    {
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                lock (_gate)
                {
                    if (_gamepad != 0 && !SdlNative.GamepadConnected(_gamepad)) DisconnectLocked();
                }
                Scan();
                _scanSignal.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch { lock (_gate) DisconnectLocked(); _scanSignal.WaitOne(500); }
        }
    }

    private unsafe void Scan()
    {
        lock (_gate) if (_gamepad != 0) return;
        var ids = SdlNative.GetGamepads(out var count);
        if (ids == 0) return;
        try
        {
            nint fallback = 0;
            string fallbackName = "";
            for (var i = 0; i < count; i++)
            {
                var candidate = SdlNative.OpenGamepad(((uint*)ids)[i]);
                if (candidate == 0) continue;
                var name = SdlNative.Utf8(SdlNative.GetGamepadName(candidate));
                if (IsRetroNintendo(name))
                {
                    Connect(candidate, name);
                    if (fallback != 0) SdlNative.CloseGamepad(fallback);
                    return;
                }
                if (fallback == 0) { fallback = candidate; fallbackName = name; }
                else SdlNative.CloseGamepad(candidate);
            }
            if (fallback != 0) Connect(fallback, fallbackName);
        }
        finally { SdlNative.Free(ids); }
    }

    private static bool IsRetroNintendo(string name) =>
        name.Contains("NES", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Famicom", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase);

    private void Connect(nint gamepad, string name)
    {
        lock (_gate)
        {
            if (_gamepad != 0) { SdlNative.CloseGamepad(gamepad); return; }
            SdlNative.UpdateGamepads();
            _gamepad = gamepad;
            _instanceId = SdlNative.GetGamepadId(gamepad);
            _name = string.IsNullOrWhiteSpace(name) ? "ゲームパッド" : name;
        }
        StatusChanged?.Invoke(ControllerName);
    }

    private static unsafe bool EventWatch(nint userdata, SdlNative.SdlEvent* evt)
    {
        if (evt == null) return true;
        var handle = GCHandle.FromIntPtr(userdata);
        if (handle.Target is not ControllerService service || service._disposed) return true;
        if (evt->Type is SdlNative.EventGamepadAdded or SdlNative.EventGamepadRemoved)
        {
            service._scanSignal.Set();
            return true;
        }
        if (evt->Type is not (SdlNative.EventGamepadButtonDown or SdlNative.EventGamepadButtonUp)) return true;
        service.DispatchButton(evt->Which, evt->Button, evt->Type == SdlNative.EventGamepadButtonDown, evt->Timestamp);
        return true;
    }

    private void DispatchButton(uint instanceId, byte buttonValue, bool pressed, ulong eventTimestamp)
    {
        OutputBinding? output;
        lock (_gate)
        {
            if (_gamepad == 0 || instanceId != _instanceId || !_enabled ||
                !_bindings.TryGetValue((PadButton)buttonValue, out output) || output is null) return;
            var before = SdlNative.GetTicksNs();
            _emitter.Set(output, pressed);
            _latency.Record(eventTimestamp, before, SdlNative.GetTicksNs());
        }
    }

    private void DisconnectLocked()
    {
        _emitter.ReleaseAll();
        if (_gamepad != 0) SdlNative.CloseGamepad(_gamepad);
        _gamepad = 0;
        _instanceId = 0;
        _name = "未接続";
        StatusChanged?.Invoke(_name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _scanSignal.Set();
        try { _loop?.Wait(1000); } catch { }
        if (_selfHandle.IsAllocated)
        {
            SdlNative.RemoveEventWatch(_eventFilter, GCHandle.ToIntPtr(_selfHandle));
            _selfHandle.Free();
        }
        lock (_gate) DisconnectLocked();
        SdlNative.Quit();
        _scanSignal.Dispose();
        _stop.Dispose();
    }
}
