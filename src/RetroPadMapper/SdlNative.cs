using System.Runtime.InteropServices;

namespace RetroPadMapper;

internal static partial class SdlNative
{
    internal const uint InitGamepad = 0x00002000;
    internal const uint EventGamepadAxisMotion = 0x650;
    internal const uint EventGamepadButtonDown = 0x651;
    internal const uint EventGamepadButtonUp = 0x652;
    internal const uint EventGamepadAdded = 0x653;
    internal const uint EventGamepadRemoved = 0x654;
    internal const uint EventGamepadRemapped = 0x655;

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct SdlEvent
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal ulong Timestamp;
        [FieldOffset(16)] internal uint Which;
        [FieldOffset(20)] internal byte Button;
        [FieldOffset(21)] internal byte Down;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal unsafe delegate bool EventFilter(nint userdata, SdlEvent* evt);

    [LibraryImport("SDL3", EntryPoint = "SDL_Init")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool Init(uint flags);

    [LibraryImport("SDL3", EntryPoint = "SDL_SetHint", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SetHint(string name, string value);

    [LibraryImport("SDL3", EntryPoint = "SDL_Quit")]
    internal static partial void Quit();

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepads")]
    internal static partial nint GetGamepads(out int count);

    [LibraryImport("SDL3", EntryPoint = "SDL_OpenGamepad")]
    internal static partial nint OpenGamepad(uint instanceId);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadID")]
    internal static partial uint GetGamepadId(nint gamepad);

    [LibraryImport("SDL3", EntryPoint = "SDL_CloseGamepad")]
    internal static partial void CloseGamepad(nint gamepad);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadName")]
    internal static partial nint GetGamepadName(nint gamepad);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadNameForID")]
    internal static partial nint GetGamepadNameForId(uint instanceId);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadPathForID")]
    internal static partial nint GetGamepadPathForId(uint instanceId);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadVendorForID")]
    internal static partial ushort GetGamepadVendorForId(uint instanceId);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadProductForID")]
    internal static partial ushort GetGamepadProductForId(uint instanceId);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetGamepadButton")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool GetGamepadButton(nint gamepad, int button);

    [LibraryImport("SDL3", EntryPoint = "SDL_GamepadConnected")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool GamepadConnected(nint gamepad);

    [LibraryImport("SDL3", EntryPoint = "SDL_UpdateGamepads")]
    internal static partial void UpdateGamepads();

    [LibraryImport("SDL3", EntryPoint = "SDL_PumpEvents")]
    internal static partial void PumpEvents();

    [LibraryImport("SDL3", EntryPoint = "SDL_PollEvent")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool PollEvent(out SdlEvent evt);

    [LibraryImport("SDL3", EntryPoint = "SDL_SetEventEnabled")]
    internal static partial void SetEventEnabled(uint type, [MarshalAs(UnmanagedType.I1)] bool enabled);

    [LibraryImport("SDL3", EntryPoint = "SDL_SetGamepadEventsEnabled")]
    internal static partial void SetGamepadEventsEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetTicksNS")]
    internal static partial ulong GetTicksNs();

    [DllImport("SDL3", EntryPoint = "SDL_AddEventWatch", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool AddEventWatch(EventFilter filter, nint userdata);

    [DllImport("SDL3", EntryPoint = "SDL_RemoveEventWatch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RemoveEventWatch(EventFilter filter, nint userdata);

    [LibraryImport("SDL3", EntryPoint = "SDL_free")]
    internal static partial void Free(nint memory);

    [LibraryImport("SDL3", EntryPoint = "SDL_GetError")]
    internal static partial nint GetError();

    internal static string Utf8(nint ptr) => ptr == 0 ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
}
