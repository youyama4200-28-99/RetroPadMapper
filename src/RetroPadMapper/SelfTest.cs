using System.Text.Json;

namespace RetroPadMapper;

internal static unsafe class SelfTest
{
    private static readonly SdlNative.EventFilter NoOpEventFilter = static (_, _) => true;

    public static int Run()
    {
        try
        {
            var original = new AppSettings();
            var json = JsonSerializer.Serialize(original, SettingsJsonContext.Default.AppSettings);
            var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
            if (restored?.Bindings.Count != original.Bindings.Count) return 2;
            if (!SdlNative.Init(SdlNative.InitGamepad)) return 3;
            if (System.Runtime.InteropServices.Marshal.SizeOf<SdlNative.SdlEvent>() != 128) return 4;
            if (!SdlNative.AddEventWatch(NoOpEventFilter, 0)) return 5;
            SdlNative.RemoveEventWatch(NoOpEventFilter, 0);
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
