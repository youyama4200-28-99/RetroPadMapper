using System.Text.Json;

namespace RetroPadMapper;

internal static unsafe class SelfTest
{
    public static int Run()
    {
        try
        {
            var diagnosticResult = DiagnosticSelfTest.Run();
            if (diagnosticResult != 0) return diagnosticResult;
            var original = new AppSettings();
            original.DebugMode = true;
            var json = JsonSerializer.Serialize(original, SettingsJsonContext.Default.AppSettings);
            var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
            if (restored?.Bindings.Count != original.Bindings.Count) return 2;
            if (!restored.DebugMode) return 10;
            var migrated = ControllerService.ChoosePreferred([new ControllerOption("new-path", 42, "HVC Controller (1)")], "stale-path");
            if (migrated?.InstanceId != 42) return 13;
            if (!ControllerService.IsHotplugEvent(SdlNative.EventGamepadAdded) || ControllerService.IsHotplugEvent(SdlNative.EventGamepadButtonDown)) return 14;
            if (!SdlNative.Init(SdlNative.InitGamepad)) return 3;
            if (System.Runtime.InteropServices.Marshal.SizeOf<SdlNative.SdlEvent>() != 128) return 4;
            if (!BluetoothDiscovery.LayoutIsValid()) return 6;
            if (!System.Runtime.InteropServices.NativeLibrary.TryLoad("SDL3", out var sdl)) return 7;
            try
            {
                var exports = new[] { "SDL_GetGamepadNameForID", "SDL_GetGamepadPathForID", "SDL_GetGamepadVendorForID", "SDL_GetGamepadProductForID", "SDL_PumpEvents", "SDL_PollEvent", "SDL_SetHint", "SDL_SetEventEnabled" };
                if (exports.Any(name => !System.Runtime.InteropServices.NativeLibrary.TryGetExport(sdl, name, out _))) return 8;
            }
            finally { System.Runtime.InteropServices.NativeLibrary.Free(sdl); }
            using (var timer = new HighResolutionPeriodicTimer(1))
                if (WaitHandle.WaitAny([timer.WaitHandle], 100) != 0) return 5;
            SdlNative.UpdateGamepads();
            SdlNative.Quit();
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
