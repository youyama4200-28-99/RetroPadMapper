using System.Text.Json;

namespace RetroPadMapper;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            var original = new AppSettings();
            var json = JsonSerializer.Serialize(original, SettingsJsonContext.Default.AppSettings);
            var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
            if (restored?.Bindings.Count != original.Bindings.Count) return 2;
            if (!SdlNative.Init(SdlNative.InitGamepad)) return 3;
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
