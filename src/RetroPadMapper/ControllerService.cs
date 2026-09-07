using System.Runtime.InteropServices;

namespace RetroPadMapper;

internal sealed class ControllerService : IDisposable
{
    private readonly InputEmitter _emitter = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Dictionary<PadButton, OutputBinding> _bindings = [];
    private bool _enabled;
    private nint _gamepad;
    private string _name = "未接続";
    private readonly Dictionary<PadButton, bool> _previous = [];
    private Task? _loop;

    public event Action<string>? StatusChanged;
    public string ControllerName { get { lock (_gate) return _name; } }

    public bool Start()
    {
        if (!SdlNative.Init(SdlNative.InitGamepad)) return false;
        _loop = Task.Run(PollLoop);
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

    private async Task PollLoop()
    {
        var nextScan = DateTime.MinValue;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (_gamepad == 0 || DateTime.UtcNow >= nextScan)
                {
                    Scan();
                    nextScan = DateTime.UtcNow.AddSeconds(2);
                }
                if (_gamepad != 0) PollButtons();
                await Task.Delay(8, _stop.Token);
            }
            catch (OperationCanceledException) { break; }
            catch { Disconnect(); await Task.Delay(500); }
        }
    }

    private unsafe void Scan()
    {
        if (_gamepad != 0) return;
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
                if (IsRetroNintendo(name)) { Connect(candidate, name); if (fallback != 0) SdlNative.CloseGamepad(fallback); return; }
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
        _gamepad = gamepad;
        lock (_gate) _name = string.IsNullOrWhiteSpace(name) ? "ゲームパッド" : name;
        StatusChanged?.Invoke(ControllerName);
    }

    private void PollButtons()
    {
        SdlNative.UpdateGamepads();
        if (!SdlNative.GamepadConnected(_gamepad))
        {
            Disconnect();
            return;
        }
        foreach (var button in Enum.GetValues<PadButton>())
        {
            var pressed = SdlNative.GetGamepadButton(_gamepad, (int)button);
            var wasPressed = _previous.GetValueOrDefault(button);
            if (pressed == wasPressed) continue;
            _previous[button] = pressed;
            lock (_gate)
            {
                if (_enabled && _bindings.TryGetValue(button, out var output)) _emitter.Set(output, pressed);
            }
        }
    }

    private void Disconnect()
    {
        _emitter.ReleaseAll(); _previous.Clear();
        if (_gamepad != 0) { SdlNative.CloseGamepad(_gamepad); _gamepad = 0; }
        lock (_gate) _name = "未接続";
        StatusChanged?.Invoke("未接続");
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(1000); } catch { }
        Disconnect(); SdlNative.Quit(); _stop.Dispose();
    }
}
