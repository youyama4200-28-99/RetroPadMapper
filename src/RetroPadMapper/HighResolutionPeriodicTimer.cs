using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RetroPadMapper;

internal sealed partial class HighResolutionPeriodicTimer : IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private readonly EventWaitHandle _waitHandle;

    internal HighResolutionPeriodicTimer(int periodMilliseconds)
    {
        var handle = CreateWaitableTimerEx(0, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        if (handle == 0) handle = CreateWaitableTimerEx(0, null, 0, TimerAllAccess);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        _waitHandle = new AutoResetEvent(false);
        _waitHandle.SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
        var dueTime100Ns = -10_000L * periodMilliseconds;
        if (!SetWaitableTimer(handle, ref dueTime100Ns, periodMilliseconds, 0, 0, false))
        {
            var error = Marshal.GetLastWin32Error();
            _waitHandle.Dispose();
            throw new Win32Exception(error);
        }
    }

    internal WaitHandle WaitHandle => _waitHandle;

    public void Dispose() => _waitHandle.Dispose();

    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint desiredAccess);

    [LibraryImport("kernel32.dll", EntryPoint = "SetWaitableTimer", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completionRoutine, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
