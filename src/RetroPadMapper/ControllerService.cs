namespace RetroPadMapper;

internal sealed unsafe class ControllerService : IDisposable
{
    private static readonly PadButton[] InputButtons =
    [
        PadButton.A, PadButton.B, PadButton.X, PadButton.Y, PadButton.Select, PadButton.Home,
        PadButton.Start, PadButton.L, PadButton.R, PadButton.Up, PadButton.Down, PadButton.Left, PadButton.Right
    ];

    private readonly InputEmitter _emitter = new();
    private readonly LatencyRecorder _latency = new();
    private readonly ConnectionDiagnostics _diagnostics = new() { OutputMode = "KeyboardMouse" };
    private long _openAttempt;
    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _scanSignal = new(false);
    private readonly object _gate = new();
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
        SdlNative.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        if (!SdlNative.Init(SdlNative.InitGamepad)) return false;
        // No SDL event loop is running in this WinForms application. Event watches only
        // receive gamepad input when that loop is pumped, so use SDL's thread-safe snapshot
        // API on a dedicated high-resolution worker instead.
        SdlNative.SetGamepadEventsEnabled(true);
        SdlNative.SetEventEnabled(SdlNative.EventGamepadAxisMotion, false);
        SdlNative.SetEventEnabled(SdlNative.EventGamepadButtonDown, false);
        SdlNative.SetEventEnabled(SdlNative.EventGamepadButtonUp, false);
        _loop = Task.Factory.StartNew(ScanLoop, _stop.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        AppLog.Info("controller service started; background events enabled");
        _scanSignal.Set();
        return true;
    }

    public void Configure(AppSettings settings)
    {
        if (AppLog.Enabled) AppLog.Debug($"configure mapping_enabled={settings.MappingEnabled} preferred={settings.PreferredController}");
        lock (_gate)
        {
            _enabled = settings.MappingEnabled;
            _diagnostics.MappingEnabled = settings.MappingEnabled;
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
        if (AppLog.Enabled) AppLog.Debug($"user-selection preferred={id}");
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
        _diagnostics.PumpStarted();
        var pumpStarted = AppLog.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var changed = false;
        while (SdlNative.PollEvent(out var evt))
        {
            if (IsHotplugEvent(evt.Type))
            {
                changed = true;
                if (AppLog.Enabled)
                    AppLog.Debug($"SDL device event type=0x{evt.Type:X} instance={evt.Which} native_ns={evt.Timestamp} age_ms={ConnectionDiagnostics.EventAge(evt.Timestamp, SdlNative.GetTicksNs())}; timestamp_is_sdl_not_radio");
            }
        }
        if (changed) _scanSignal.Set();
        if (pumpStarted != 0)
        {
            var ms = ConnectionDiagnostics.Milliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - pumpStarted);
            if (ms >= 50) AppLog.Debug($"ui-pump slow_ms={ms:F3}");
        }
    }

    public Task RequestReconnectAsync()
    {
        lock (_gate) DisconnectLocked();
        Volatile.Write(ref _reconnectUntilUtcTicks, DateTime.UtcNow.AddSeconds(15).Ticks);
        Interlocked.Exchange(ref _reconnectCompletionSent, 0);
        ReconnectStateChanged?.Invoke("再接続待機中 — コントローラーのボタンを1秒ほど押してください");
        AppLog.Info($"reconnect requested preferred={_preferredController}; success_currently_means_handle_open_not_input");
        _scanSignal.Set();
        return Task.Run(() =>
        {
            try
            {
                while (!_stop.IsCancellationRequested && DateTime.UtcNow.Ticks < Volatile.Read(ref _reconnectUntilUtcTicks) && ConnectedControllerId.Length == 0)
                {
                    BluetoothDiscovery.Probe();
                    _scanSignal.Set();
                    if (_stop.Token.WaitHandle.WaitOne(350)) break;
                }
            }
            catch (Exception ex) { AppLog.Error("Bluetooth reconnect probe failed", ex); }
            finally { _scanSignal.Set(); }
        });
    }

    private void ScanLoop()
    {
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        using var inputTimer = new HighResolutionPeriodicTimer(1);
        WaitHandle[] connectedWaits = [inputTimer.WaitHandle, _scanSignal];
        var nextScan = 0L;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var connected = PollGamepad();
                var now = Environment.TickCount64;
                if (!connected || now >= nextScan)
                {
                    Scan();
                    connected = ConnectedControllerId.Length != 0;
                    nextScan = now + 2000;
                }
                var reconnecting = DateTime.UtcNow.Ticks < Volatile.Read(ref _reconnectUntilUtcTicks);
                if (!reconnecting && Interlocked.CompareExchange(ref _reconnectCompletionSent, 1, 0) == 0 &&
                    Volatile.Read(ref _reconnectUntilUtcTicks) != 0 && ConnectedControllerId.Length == 0)
                    ReconnectStateChanged?.Invoke("未検出です。HOMEまたはSTARTを押してから、もう一度お試しください");
                if (connected)
                {
                    if (WaitHandle.WaitAny(connectedWaits) == 1) nextScan = 0;
                }
                else if (_scanSignal.WaitOne(reconnecting ? 50 : 250)) nextScan = 0;
            }
            catch (Exception ex) { AppLog.Error("controller scan loop failed", ex); lock (_gate) DisconnectLocked(); _scanSignal.WaitOne(250); nextScan = 0; }
        }
    }

    private bool PollGamepad()
    {
        var diagnosticStarted = _diagnostics.BeginPoll();
        lock (_gate)
        {
            if (_gamepad == 0) return false;
            var updateStarted = _diagnostics.LockAcquired(diagnosticStarted);
            SdlNative.UpdateGamepads();
            _diagnostics.Updated(updateStarted);
            if (!SdlNative.GamepadConnected(_gamepad))
            {
                DisconnectLocked();
                return false;
            }

            var sampledAt = SdlNative.GetTicksNs();
            var nextMask = 0;
            foreach (var button in InputButtons)
                if (SdlNative.GetGamepadButton(_gamepad, (int)button)) nextMask |= 1 << (int)button;

            _diagnostics.Sample(nextMask, _instanceId);
            var previousMask = Volatile.Read(ref _pressedButtons);
            var changed = previousMask ^ nextMask;
            if (changed == 0) return true;
            Volatile.Write(ref _pressedButtons, nextMask);

            foreach (var button in InputButtons)
            {
                var bit = 1 << (int)button;
                if ((changed & bit) == 0 || !_enabled || !_bindings.TryGetValue(button, out var output) || output is null) continue;
                var pressed = (nextMask & bit) != 0;
                var beforeEmit = SdlNative.GetTicksNs();
                _emitter.Set(output, pressed);
                _latency.Record(sampledAt, beforeEmit, SdlNative.GetTicksNs());
            }
            return true;
        }
    }

    private unsafe void Scan()
    {
        var scanStarted = AppLog.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        SdlNative.UpdateGamepads();
        var ids = SdlNative.GetGamepads(out var count);
        if (ids == 0)
        {
            AppLog.Error($"enumeration failed SDL_GetGamepads: {SdlNative.Utf8(SdlNative.GetError())}");
            UpdateControllerList([]);
            _diagnostics.Flush(scanStarted);
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
                options.Add(new ControllerOption(key, instanceId, count > 1 ? $"{name}  [{vendor:X4}:{product:X4}]" : name)
                    { VendorId = vendor, ProductId = product });
            }
            UpdateControllerList(options);

            lock (_gate)
            {
                if (_gamepad != 0)
                {
                    _diagnostics.Decision("already_connected_skip_selection", _preferredController, null, _instanceId);
                    return;
                }
            }
            var selected = Choose(options);
            if (selected is null) return;
            var attempt = ++_openAttempt;
            var openStarted = AppLog.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            if (AppLog.Enabled) AppLog.Debug($"open-begin attempt={attempt} instance={selected.InstanceId} path={selected.Id}");
            var gamepad = SdlNative.OpenGamepad(selected.InstanceId);
            // Capture the thread-local SDL error immediately, before any other SDL call.
            var error = gamepad == 0 ? SdlNative.Utf8(SdlNative.GetError()) : "";
            if (AppLog.Enabled)
                AppLog.Debug($"open-end attempt={attempt} instance={selected.InstanceId} success={gamepad != 0} duration_ms={ConnectionDiagnostics.Milliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - openStarted):F3} error={error}");
            if (gamepad != 0) Connect(gamepad, selected);
        }
        finally { SdlNative.Free(ids); _diagnostics.Flush(scanStarted); }
    }

    private ControllerOption? Choose(List<ControllerOption> options)
    {
        string preferred;
        lock (_gate) preferred = _preferredController;
        var selected = ChoosePreferred(options, preferred);
        _diagnostics.Decision(ConnectionDiagnostics.SelectionReason(options, preferred, selected), preferred, selected);
        return selected;
    }

    internal static ControllerOption? ChoosePreferred(List<ControllerOption> options, string preferred)
    {
        if (preferred.Length > 0)
        {
            var exact = options.FirstOrDefault(x => x.Id == preferred);
            if (exact is not null) return exact;
            var retro = options.Where(x => IsRetroNintendo(x.Name)).ToArray();
            if (retro.Length == 1)
            {
                // The diagnostic decision identifies this as a name-based, unverified fallback.
                return retro[0];
            }
            return null;
        }
        return options.FirstOrDefault(x => IsRetroNintendo(x.Name)) ?? options.FirstOrDefault();
    }

    internal static bool IsHotplugEvent(uint type) =>
        type is SdlNative.EventGamepadAdded or SdlNative.EventGamepadRemoved or SdlNative.EventGamepadRemapped;

    private void UpdateControllerList(List<ControllerOption> options)
    {
        bool changed;
        lock (_gate)
        {
            changed = _controllers.Count != options.Count || !_controllers.Select(x => (x.Id, x.Name)).SequenceEqual(options.Select(x => (x.Id, x.Name)));
            if (changed) _controllers = options;
        }
        _diagnostics.Inventory(options);
        if (changed) ControllersChanged?.Invoke();
        if (changed) AppLog.Debug($"controller list changed: {options.Count} device(s)");
    }

    internal static bool IsRetroNintendo(string name) =>
        name.Contains("NES", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Famicom", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("HVC Controller", StringComparison.OrdinalIgnoreCase) ||
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
            _diagnostics.NewConnection();
        }
        Volatile.Write(ref _reconnectUntilUtcTicks, 0);
        Interlocked.Exchange(ref _reconnectCompletionSent, 1);
        StatusChanged?.Invoke(ControllerName);
        ReconnectStateChanged?.Invoke("接続しました");
        AppLog.Info($"connected: {option.Name}; id={option.Id}; instance={_instanceId}; stage=handle_open input_confirmed=false");
    }

    private void DisconnectLocked()
    {
        var disconnectedName = _name;
        _emitter.ReleaseAll();
        Volatile.Write(ref _pressedButtons, 0);
        if (_gamepad != 0) SdlNative.CloseGamepad(_gamepad);
        _gamepad = 0;
        _instanceId = 0;
        _connectedKey = "";
        _name = "未接続";
        StatusChanged?.Invoke(_name);
        if (disconnectedName != "未接続") AppLog.Info($"disconnected: {disconnectedName}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _scanSignal.Set();
        try { _loop?.Wait(1000); } catch { }
        lock (_gate) DisconnectLocked();
        SdlNative.Quit();
        _scanSignal.Dispose();
        _stop.Dispose();
    }
}
