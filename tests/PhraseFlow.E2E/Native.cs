using System.Runtime.InteropServices;

namespace PhraseFlow.E2E;

/// <summary>SendInput helpers that refuse to send anything unless the expected window/process is in the foreground.</summary>
internal static class Native
{
    public const int VK_BACK = 0x08;
    public const int VK_RETURN = 0x0D;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_SPACE = 0x20;
    public const int VK_END = 0x23;
    public const int VK_HOME = 0x24;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern unsafe uint SendInput(uint count, INPUT* inputs, int size);
    [DllImport("user32.dll")] private static extern short VkKeyScanExW(char ch, nint hkl);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);

    public static int ForegroundProcessId()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return (int)pid;
    }

    public static bool ForceForeground(nint hwnd)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetForegroundWindow() == hwnd)
            {
                return true;
            }

            var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            var current = GetCurrentThreadId();
            var attached = foregroundThread != current && AttachThreadInput(current, foregroundThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            if (attached)
            {
                AttachThreadInput(current, foregroundThread, false);
            }

            for (var i = 0; i < 20 && GetForegroundWindow() != hwnd; i++)
            {
                Thread.Sleep(25);
                Application.DoEvents();
            }
        }

        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Types text like a user (virtual keys with Shift where needed). \n = Enter, \b = Backspace.</summary>
    public static async Task TypeAsync(string text, nint requiredForegroundWindow = 0, int requiredForegroundProcess = 0, bool batch = false)
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        if (batch)
        {
            EnsureForeground(requiredForegroundWindow, requiredForegroundProcess);
            var all = new List<INPUT>();
            foreach (var c in text)
            {
                AddChar(all, c, layout);
            }

            Send(all);
            return;
        }

        foreach (var c in text)
        {
            EnsureForeground(requiredForegroundWindow, requiredForegroundProcess);
            var inputs = new List<INPUT>();
            AddChar(inputs, c, layout);
            Send(inputs);
            await Task.Delay(25);
        }
    }

    public static void SendChord(nint requiredForegroundWindow, params int[] keys)
    {
        EnsureForeground(requiredForegroundWindow, 0);
        var inputs = new List<INPUT>();
        foreach (var key in keys)
        {
            inputs.Add(Key(key, up: false));
        }

        for (var i = keys.Length - 1; i >= 0; i--)
        {
            inputs.Add(Key(keys[i], up: true));
        }

        Send(inputs);
    }

    private static void EnsureForeground(nint window, int process)
    {
        if (window != 0 && GetForegroundWindow() != window)
        {
            throw new InvalidOperationException("The harness window is not in the foreground; input was not sent.");
        }

        if (process != 0 && ForegroundProcessId() != process)
        {
            throw new InvalidOperationException("PhraseFlow is not in the foreground; input was not sent.");
        }
    }

    private static void AddChar(List<INPUT> inputs, char c, nint layout)
    {
        if (c == '\n')
        {
            inputs.Add(Key(VK_RETURN, false));
            inputs.Add(Key(VK_RETURN, true));
            return;
        }

        if (c == '\b')
        {
            inputs.Add(Key(VK_BACK, false));
            inputs.Add(Key(VK_BACK, true));
            return;
        }

        var scan = VkKeyScanExW(c, layout);
        var shiftState = (scan >> 8) & 0xFF;
        if (scan == -1 || (shiftState & 0x6) != 0)
        {
            inputs.Add(Unicode(c, false));
            inputs.Add(Unicode(c, true));
            return;
        }

        var vk = scan & 0xFF;
        var shift = (shiftState & 1) != 0;
        if (shift)
        {
            inputs.Add(Key(VK_SHIFT, false));
        }

        inputs.Add(Key(vk, false));
        inputs.Add(Key(vk, true));
        if (shift)
        {
            inputs.Add(Key(VK_SHIFT, true));
        }
    }

    // Navigation keys (Page Up..Delete) are extended keys; without the flag they act as numeric keypad keys.
    private static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (vk is >= 0x21 and <= 0x2E ? KEYEVENTF_EXTENDEDKEY : 0),
            },
        },
    };

    private static INPUT Unicode(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } },
    };

    private static unsafe void Send(List<INPUT> inputs)
    {
        var array = inputs.ToArray();
        fixed (INPUT* pointer = array)
        {
            SendInput((uint)array.Length, pointer, sizeof(INPUT));
        }
    }
}
