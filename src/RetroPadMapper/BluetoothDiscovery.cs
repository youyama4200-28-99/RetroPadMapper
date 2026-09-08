using System.Runtime.InteropServices;

namespace RetroPadMapper;

internal static class BluetoothDiscovery
{
    private const int BluetoothMaxNameSize = 248;

    // A short, read-only Classic Bluetooth inquiry. It does not alter pairing or services;
    // it helps Windows notice an awake remembered HID before SDL rescans.
    internal static void Probe()
    {
        if (!OperatingSystem.IsWindows()) return;
        var search = new SearchParameters
        {
            Size = Marshal.SizeOf<SearchParameters>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnUnknown = false,
            ReturnConnected = true,
            IssueInquiry = true,
            TimeoutMultiplier = 2,
        };
        var device = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>(), Name = string.Empty };
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (AppLog.Enabled) AppLog.Debug("bluetooth-probe begin inquiry=true timeout_multiplier=2 all_radios=true; not_a_connect_request");
        var find = FindFirstDevice(ref search, ref device);
        if (find == 0)
        {
            var error = Marshal.GetLastWin32Error();
            AppLog.Debug($"bluetooth-probe first_result=none win32={error} duration_ms={ConnectionDiagnostics.Milliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - started):F3}");
            return;
        }
        var count = 0;
        try
        {
            do
            {
                count++;
                if (AppLog.Enabled)
                    AppLog.Debug($"bluetooth-device name={device.Name} address={device.Address:X12} remembered={device.Remembered} authenticated={device.Authenticated} connected={device.Connected}; flags_not_input_confirmation");
                device = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>(), Name = string.Empty };
            } while (FindNextDevice(find, ref device));
            var error = Marshal.GetLastWin32Error();
            if (AppLog.Enabled)
                AppLog.Debug($"bluetooth-probe end count={count} terminal_win32={error} duration_ms={ConnectionDiagnostics.Milliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - started):F3}; ERROR_NO_MORE_ITEMS=259");
        }
        finally { FindDeviceClose(find); }
    }

    internal static bool LayoutIsValid() =>
        Marshal.SizeOf<SearchParameters>() == (Environment.Is64BitProcess ? 40 : 32) &&
        Marshal.SizeOf<DeviceInfo>() == 560;

    [StructLayout(LayoutKind.Sequential)]
    private struct SearchParameters
    {
        internal int Size;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] internal bool IssueInquiry;
        internal byte TimeoutMultiplier;
        internal nint Radio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceInfo
    {
        internal int Size;
        internal ulong Address;
        internal uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] internal bool Connected;
        [MarshalAs(UnmanagedType.Bool)] internal bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] internal bool Authenticated;
        internal SystemTime LastSeen;
        internal SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BluetoothMaxNameSize)] internal string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        internal ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [DllImport("bthprops.cpl", EntryPoint = "BluetoothFindFirstDevice", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint FindFirstDevice(ref SearchParameters search, ref DeviceInfo device);

    [DllImport("bthprops.cpl", EntryPoint = "BluetoothFindNextDevice", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextDevice(nint find, ref DeviceInfo device);

    [DllImport("bthprops.cpl", EntryPoint = "BluetoothFindDeviceClose")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindDeviceClose(nint find);
}
