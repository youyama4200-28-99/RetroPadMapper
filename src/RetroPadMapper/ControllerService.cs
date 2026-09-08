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
    private List<ControllerOption> _controllers = [];
    private bool _enabled;
    private bool _disposed;
    private nint _gamepad;
    private uint _instanceId;
    private string _connectedKey = "";
    private string _preferredController = "";
    private string _name = "未接続";
    private int _pressedButtons;
    private long _reconnectUntilUtcTicks;
    private int _reconnectCompletionSent;
    private Task? _loop;
    private GCHandle _selfHandle;

    public ControllerService() => _eventFilter = EventWatch;

    public event Action<string>? StatusChanged;
    public event Action? ControllersChanged;
    public event Action<string>? ReconnectStateChanged;
    public string ControllerName { get { lock (_gate) return _name; } }
    public string ConnectedControllerId { get { lock (_gate) return _connectedKey; } }
    public IReadOnlyList<ControllerOption> AvailableControllers { get { lock (_gate) return _controllers.ToArray(); } }
    public int PressedButtons => Volatile.Read(ref _pressedButtons);
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
            if (_preferredController == settings.PreferredController) return;
            _preferredController = settings.PreferredController;
            if (_gamepad != 0 && _preferredController.Length > 0 && _connectedKey != _preferredController)
                DisconnectLocked();
        }
        _scanSignal.Set();
    }

    public void SelectController(string id)
    {
        lock (_gate)
        {
            _preferredController = id;
            if (_gamepad != 0 && id.Length > 0 && _connectedKey != id) DisconnectLocked();
        }
        _scanSignal.Set();
    }

    public void RefreshControllers() => _scanSignal.Set();

    // SDL requires OS device events to be pumped on the thread that owns the UI loop.
    // Button snapshots remain on the dedicated 1 ms worker.
    public void PumpHotplugEvents()
    {
        if (_disposed || _loop is null) return;
        SdlNative.PumpEvents();
    }

    public Task RequestReconnectAsync()
    {
        lock (_gate) DisconnectLocked();
        Volatile.Write(ref _reconnectUntilUtcTicks, DateTime.UtcNow.AddSeconds(15).Ticks);
        Interlocked.Exchange(ref _reconnectCompletionSent, 0);
        ReconnectStateChanged?.Invoke("再接続待機中 — コントローラーのボタンを1秒ほど押してください");
        _scanSignal.Set();
        return Task.Run(() =>
        {
            var originalPriority = Thread.CurrentThread.Priority;
            try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; BluetoothDiscovery.Probe(); }
            catch { }
            finally { Thread.CurrentThread.Priority = originalPriority; _scanSignal.Set(); }
        });
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
                var reconnecting = DateTime.UtcNow.Ticks < Volatile.Read(ref _reconnectUntilUtcTicks);
                if (!reconnecting && Interlocked.CompareExchange(ref _reconnectCompletionSent, 1, 0) == 0 &&
                    Volatile.Read(ref _reconnectUntilUtcTicks) != 0 && ConnectedControllerId.Length == 0)
                    ReconnectStateChanged?.Invoke("未検出です。HOMEまたはSTARTを押してから、もう一度お試しください");
                _scanSignal.WaitOne(reconnecting ? 250 : 2000);
            }
            catch { lock (_gate) DisconnectLocked(); _scanSignal.WaitOne(500); }
        }
    }

    private unsafe void Scan()
    {
        var ids = SdlNative.GetGamepads(out var count);
        if (ids == 0)
        {
            UpdateControllerList([]);
            return;
        }
        try
        {
            var options = new List<ControllerOption>(count);
            for (var i = 0; i < count; i++)
            {
                var instanceId = ((uint*)ids)[i];
                var name = SdlNative.Utf8(SdlNative.GetGamepadNameForId(instanceId));
                if (string.IsNullOrWhiteSpace(name)) name = $"ゲームパッド {i + 1}";
                var path = SdlNative.Utf8(SdlNative.GetGamepadPathForId(instanceId));
                var vendor = SdlNative.GetGamepadVendorForId(instanceId);
                var product = SdlNative.GetGamepadProductForId(instanceId);
                var key = string.IsNullOrWhiteSpace(path) ? $"{vendor:X4}:{product:X4}:{name}:{instanceId}" : path;
                options.Add(new ControllerOption(key, instanceId, count > 1 ? $"{name}  [{vendor:X4}:{product:X4}]" : name));
            }
            UpdateControllerList(options);

            lock (_gate) if (_gamepad != 0) return;
            var selected = Choose(options);
            if (selected is null) return;
            var gamepad = SdlNative.OpenGamepad(selected.InstanceId);
            if (gamepad != 0) Connect(gamepad, selected);
        }
        finally { SdlNative.Free(ids); }
    }

    private ControllerOption? Choose(List<ControllerOption> options)
    {
        string preferred;
        lock (_gate) preferred = _preferredController;
        if (preferred.Length > 0) return options.FirstOrDefault(x => x.Id == preferred);
        return options.FirstOrDefault(x => IsRetroNintendo(x.Name)) ?? options.FirstOrDefault();
    }

    private void UpdateControllerList(List<ControllerOption> options)
    {
        bool changed;
        lock (_gate)
        {
            changed = _controllers.Count != options.Count || !_controllers.Select(x => (x.Id, x.Name)).SequenceEqual(options.Select(x => (x.Id, x.Name)));
            if (changed) _controllers = options;
        }
        if (changed) ControllersChanged?.Invoke();
    }

    private static bool IsRetroNintendo(string name) =>
        name.Contains("NES", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Famicom", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase);

    private void Connect(nint gamepad, ControllerOption option)
    {
        lock (_gate)
        {
            if (_gamepad != 0) { SdlNative.CloseGamepad(gamepad); return; }
            SdlNative.UpdateGamepads();
            _gamepad = gamepad;
            _instanceId = SdlNative.GetGamepadId(gamepad);
            _connectedKey = option.Id;
            _name = option.Name;
        }
        Volatile.Write(ref _reconnectUntilUtcTicks, 0);
        Interlocked.Exchange(ref _reconnectCompletionSent, 1);
        StatusChanged?.Invoke(ControllerName);
        ReconnectStateChanged?.Invoke("接続しました");
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
            if (_gamepad == 0 || instanceId != _instanceId) return;
            UpdatePressed(buttonValue, pressed);
            if (!_enabled || !_bindings.TryGetValue((PadButton)buttonValue, out output) || output is null) return;
            var before = SdlNative.GetTicksNs();
            _emitter.Set(output, pressed);
            _latency.Record(eventTimestamp, before, SdlNative.GetTicksNs());
        }
    }

    private void UpdatePressed(byte button, bool pressed)
    {
        if (button >= 31) return;
        var bit = 1 << button;
        if (pressed) Interlocked.Or(ref _pressedButtons, bit);
        else Interlocked.And(ref _pressedButtons, ~bit);
    }

    private void DisconnectLocked()
    {
        _emitter.ReleaseAll();
        Volatile.Write(ref _pressedButtons, 0);
        if (_gamepad != 0) SdlNative.CloseGamepad(_gamepad);
        _gamepad = 0;
        _instanceId = 0;
        _connectedKey = "";
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
