using System.Runtime.InteropServices;
namespace RetroPadMapper;
internal static partial class SdlNative
{
    [LibraryImport("SDL3", EntryPoint = "SDL_SetHintWithPriority", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SetHintWithPriority(string name, string value, int priority);
    [LibraryImport("SDL3", EntryPoint = "SDL_GetRevision")]
    internal static partial nint GetRevision();
    [LibraryImport("SDL3", EntryPoint = "SDL_GetJoysticks")]
    internal static partial nint GetJoysticks(out int count);
    [LibraryImport("SDL3", EntryPoint = "SDL_GetJoystickNameForID")]
    internal static partial nint GetJoystickNameForId(uint id);
    [LibraryImport("SDL3", EntryPoint = "SDL_GetJoystickPathForID")]
    internal static partial nint GetJoystickPathForId(uint id);
    [LibraryImport("SDL3", EntryPoint = "SDL_GetJoystickVendorForID")]
    internal static partial ushort GetJoystickVendorForId(uint id);
    [LibraryImport("SDL3", EntryPoint = "SDL_GetJoystickProductForID")]
    internal static partial ushort GetJoystickProductForId(uint id);
    [LibraryImport("SDL3", EntryPoint = "SDL_OpenJoystick")]
    internal static partial nint OpenJoystick(uint id);
    [LibraryImport("SDL3", EntryPoint = "SDL_CloseJoystick")]
    internal static partial void CloseJoystick(nint joystick);
    [LibraryImport("SDL3", EntryPoint = "SDL_JoystickConnected")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool JoystickConnected(nint joystick);
    [LibraryImport("SDL3", EntryPoint = "SDL_UpdateJoysticks")]
    internal static partial void UpdateJoysticks();
    [LibraryImport("SDL3", EntryPoint = "SDL_GetNumJoystickButtons")]
    internal static partial int GetNumJoystickButtons(nint joystick);
    [LibraryImport("SDL3", EntryPoint = "SDL_GetNumJoystickHats")]
    internal static partial int GetNumJoystickHats(nint joystick);
    [LibraryImport("SDL3", EntryPoint = "SDL_GetJoystickButton")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool GetJoystickButton(nint joystick, int button);
    [LibraryImport("SDL3", EntryPoint = "SDL_GetJoystickHat")]
    internal static partial byte GetJoystickHat(nint joystick, int hat);
}
