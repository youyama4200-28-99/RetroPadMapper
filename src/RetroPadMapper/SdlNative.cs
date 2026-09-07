using System.Runtime.InteropServices;

namespace RetroPadMapper;

internal static partial class SdlNative
{
    internal const uint InitGamepad = 0x00002000;

    [LibraryImport("SDL3", EntryPoint = "SDL_Init")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool Init(uint flags);

    [LibraryImport("SDL3", EntryPoint = "SDL_Quit")]
    internal static partial void Quit();

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepads")]
    internal static partial nint GetGamepads(out int count);

    [LibraryImport("SDL3", EntryPoint = "SDL_OpenGamepad")]
    internal static partial nint OpenGamepad(uint instanceId);

    [LibraryImport("SDL3", EntryPoint = "SDL_CloseGamepad")]
    internal static partial void CloseGamepad(nint gamepad);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadName")]
    internal static partial nint GetGamepadName(nint gamepad);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadButton")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool GetGamepadButton(nint gamepad, int button);

    [LibraryImport("SDL3", EntryPoint = "SDL_GamepadConnected")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool GamepadConnected(nint gamepad);

    [LibraryImport("SDL3", EntryPoint = "SDL_UpdateGamepads")]
    internal static partial void UpdateGamepads();

    [LibraryImport("SDL3", EntryPoint = "SDL_free")]
    internal static partial void Free(nint memory);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetError")]
    internal static partial nint GetError();

    internal static string Utf8(nint ptr) => ptr == 0 ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
}
