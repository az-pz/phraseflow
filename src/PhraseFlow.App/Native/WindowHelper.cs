using System.Collections.Concurrent;
using static PhraseFlow.App.Native.NativeMethods;

namespace PhraseFlow.App.Native;

/// <summary>Information about other applications' windows.</summary>
internal static unsafe class WindowHelper
{
    private static readonly ConcurrentDictionary<uint, string?> ProcessNames = new();

    public static nint Foreground => GetForegroundWindow();

    public static uint GetProcessId(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    /// <summary>Executable file name (e.g. <c>notepad.exe</c>) of the process with the given id; cached.</summary>
    public static string? GetProcessName(uint pid)
    {
        if (pid == 0)
        {
            return null;
        }

        if (ProcessNames.Count > 512)
        {
            ProcessNames.Clear();
        }

        return ProcessNames.GetOrAdd(pid, static id =>
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, id);
            if (handle == 0)
            {
                return null;
            }

            try
            {
                var buffer = stackalloc char[1024];
                uint size = 1024;
                return QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                    ? Path.GetFileName(new string(buffer, 0, (int)size))
                    : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        });
    }

    public static string? GetProcessName(nint hwnd) => GetProcessName(GetProcessId(hwnd));

    /// <summary>
    /// True when the process runs elevated while PhraseFlow does not, so Windows would silently drop injected input
    /// (UIPI). Returns false when elevation cannot be determined.
    /// </summary>
    public static bool IsInputBlocked(uint pid)
    {
        if (pid == 0 || Environment.IsPrivilegedProcess)
        {
            return false;
        }

        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return false;
        }

        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token))
            {
                return false;
            }

            try
            {
                uint elevated = 0;
                return GetTokenInformation(token, TokenElevation, &elevated, sizeof(uint), out _) && elevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static nint GetKeyboardLayoutFor(nint hwnd) => GetKeyboardLayout(GetWindowThreadProcessId(hwnd, out _));

    /// <summary>Screen position of the text caret in the window, if the application exposes it.</summary>
    public static (int X, int Y)? GetCaretPosition(nint hwnd)
    {
        var threadId = GetWindowThreadProcessId(hwnd, out _);
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (!GetGUIThreadInfo(threadId, ref info) || info.hwndCaret == 0)
        {
            return null;
        }

        var point = new POINT { X = info.rcCaret.Left, Y = info.rcCaret.Bottom };
        return ClientToScreen(info.hwndCaret, ref point) ? (point.X, point.Y) : null;
    }

    /// <summary>Brings a window to the foreground, working around the foreground lock when necessary.</summary>
    public static bool Activate(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return false;
        }

        if (GetForegroundWindow() == hwnd)
        {
            return true;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        if (SetForegroundWindow(hwnd) && WaitForForeground(hwnd, 100))
        {
            return true;
        }

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        if (WaitForForeground(hwnd, 150))
        {
            return true;
        }

        // Pressing Alt unlocks SetForegroundWindow; the mask key prevents menu activation.
        var inputs = new List<INPUT>
        {
            InputInjector.Key(VirtualKeys.Menu, up: false),
            InputInjector.Key(VirtualKeys.MenuMask, up: false),
            InputInjector.Key(VirtualKeys.MenuMask, up: true),
            InputInjector.Key(VirtualKeys.Menu, up: true),
        };
        InputInjector.Send(inputs);
        SetForegroundWindow(hwnd);
        return WaitForForeground(hwnd, 250);
    }

    public static bool WaitForForeground(nint hwnd, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (GetForegroundWindow() == hwnd)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Display name of the signed-in user, e.g. "Jane Doe".</summary>
    public static string? GetDisplayName()
    {
        const int nameDisplay = 3;
        var buffer = stackalloc char[256];
        uint size = 256;
        if (GetUserNameExW(nameDisplay, buffer, ref size) && size > 0)
        {
            var name = new string(buffer, 0, (int)size).Trim();
            return name.Length > 0 ? name : null;
        }

        return null;
    }
}
