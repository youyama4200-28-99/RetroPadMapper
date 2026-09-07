using System.Runtime.InteropServices;

namespace RetroPadMapper;

internal sealed class InputEmitter
{
    private readonly HashSet<int> _heldKeys = [];
    private readonly HashSet<int> _heldMouse = [];

    public void Set(OutputBinding binding, bool pressed)
    {
        switch (binding.Kind)
        {
            case OutputKind.Key: SetKey(binding.Code, pressed); break;
            case OutputKind.MouseButton: SetMouse(binding.Code, pressed); break;
            case OutputKind.MouseWheel when pressed: SendMouse(0, 0, binding.Code, 0x0800); break;
        }
    }

    public void ReleaseAll()
    {
        foreach (var key in _heldKeys.ToArray()) SetKey(key, false);
        foreach (var button in _heldMouse.ToArray()) SetMouse(button, false);
    }

    private void SetKey(int key, bool pressed)
    {
        if (pressed ? !_heldKeys.Add(key) : !_heldKeys.Remove(key)) return;
        var input = new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = pressed ? 0u : 0x0002u } } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private void SetMouse(int button, bool pressed)
    {
        if (pressed ? !_heldMouse.Add(button) : !_heldMouse.Remove(button)) return;
        uint flags = (button, pressed) switch
        {
            (0, true) => 0x0002, (0, false) => 0x0004,
            (1, true) => 0x0008, (1, false) => 0x0010,
            (2, true) => 0x0020, (2, false) => 0x0040,
            _ => 0
        };
        if (flags != 0) SendMouse(0, 0, 0, flags);
    }

    private static void SendMouse(int dx, int dy, int data, uint flags)
    {
        var input = new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy, mouseData; public uint dwFlags, time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }
}
