using System.Windows.Interop;
using PhraseFlow.App.Native;
using PhraseFlow.Core.Placeholders;
using static PhraseFlow.App.Native.NativeMethods;

namespace PhraseFlow.App.Services;

/// <summary>Registers system-wide hotkeys on a message-only window owned by the UI thread.</summary>
internal sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 0xB000;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("PhraseFlowMessageWindow")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Handle of the message window (also used as clipboard owner).</summary>
    public nint Handle => _source.Handle;

    public static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (!KeyChord.TryParse(text, out var chord))
        {
            return false;
        }

        vk = (uint)chord.VirtualKey;
        if (chord.Character is { } c)
        {
            var scan = VkKeyScanExW((ushort)c, GetKeyboardLayout(0));
            if (scan == -1)
            {
                return false;
            }

            vk = (uint)(scan & 0xFF);
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Ctrl))
        {
            modifiers |= MOD_CONTROL;
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= MOD_ALT;
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= MOD_SHIFT;
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Win))
        {
            modifiers |= MOD_WIN;
        }

        return vk != 0 && modifiers != 0;
    }

    /// <summary>Registers a hotkey such as "Ctrl+Alt+Space". Returns the id, or null when invalid or already taken.</summary>
    public int? Register(string? hotkey, Action handler)
    {
        if (string.IsNullOrWhiteSpace(hotkey) || !TryParse(hotkey, out var modifiers, out var vk))
        {
            return null;
        }

        var id = _nextId++;
        if (!RegisterHotKey(Handle, id, modifiers | MOD_NOREPEAT, vk))
        {
            return null;
        }

        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int? id)
    {
        if (id is { } value && _handlers.Remove(value))
        {
            UnregisterHotKey(Handle, value);
        }
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList())
        {
            UnregisterHotKey(Handle, id);
        }

        _handlers.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue((int)wParam, out var handler))
        {
            handled = true;
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Log.Error("Hotkey handler failed", ex);
            }
        }

        return 0;
    }
}
