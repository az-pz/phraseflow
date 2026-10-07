using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Placeholders;
using static PhraseFlow.App.Native.NativeMethods;

namespace PhraseFlow.App.Native;

/// <summary>A completed keyword detected by the hook.</summary>
/// <param name="ContextGeneration">Changes when the user clicks or switches windows; used to detect a moved caret.</param>
/// <param name="HoldGeneration">Identifies the hold started for this match; it is no longer current once abandoned.</param>
internal sealed record HookMatch(TypingMatch Match, nint Window, string? ProcessName, int ContextGeneration, int HoldGeneration);

/// <summary>Information needed to revert the last expansion when Backspace is pressed right after it.</summary>
internal sealed record UndoInfo(nint Window, int CaretLength, string OriginalText, int ContextGeneration);

/// <summary>
/// Global low-level keyboard and mouse hook running on its own thread. It tracks typed text, detects keywords,
/// blocks the key that completes a keyword, and holds back keys typed while an expansion is being inserted so they
/// can be replayed in order afterwards.
/// </summary>
internal sealed unsafe class KeyboardHook : IDisposable
{
    private const uint InvokeMessage = WM_APP + 1;
    private const int MaxReplayMilliseconds = 1500;
    private const uint WatchdogIntervalMilliseconds = 250;

    private static readonly int[] ModifierKeys =
    [
        VirtualKeys.LShift, VirtualKeys.RShift, VirtualKeys.LControl, VirtualKeys.RControl,
        VirtualKeys.LMenu, VirtualKeys.RMenu, VirtualKeys.LWin, VirtualKeys.RWin,
    ];

    private static KeyboardHook? _current;

    private readonly TypingEngine _engine = new();
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly List<HeldKey> _held = [];
    private readonly HashSet<uint> _heldDownKeys = [];
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _keyboardHook;
    private nint _mouseHook;
    private nuint _watchdog;

    private nint _foreground;
    private string? _foregroundProcess;
    private bool _foregroundExcluded;
    private bool _foregroundIsSelf;

    private bool _capsLock;
    private char _pendingDeadKey;
    private bool _expansionInFlight;
    private long _holdStarted;
    private int _holdGeneration;
    private int _contextGeneration;
    private int _replayPending;
    private long _replayStarted;
    private int _heldFront;
    private HeldKey? _swallowed;
    private int _userModifiers;
    private UndoInfo? _undo;

    private volatile KeywordIndex _index = KeywordIndex.Empty;
    private volatile DelimiterSet _delimiters = DelimiterSet.Default;
    private volatile HashSet<string> _excludedApps = new(StringComparer.OrdinalIgnoreCase);

    public event Action<HookMatch>? MatchFound;

    /// <summary>Raised when Backspace should revert the last expansion; the second argument is the hold generation.</summary>
    public event Action<UndoInfo, int>? UndoRequested;

    public bool Enabled { get; set; } = true;

    public bool BackspaceUndoEnabled { get; set; } = true;

    /// <summary>Longest time keys are held for one expansion before they are released and the expansion abandoned.</summary>
    public int MaxHoldMilliseconds { get; set; } = 5000;

    /// <summary>Set by the UI while the in-app playground has keyboard focus (expansion is otherwise off in PhraseFlow).</summary>
    public bool PlaygroundFocused { get; set; }

    public bool IsRunning => _keyboardHook != 0;

    public KeywordIndex Index
    {
        get => _index;
        set
        {
            _index = value;
            Post(() => _engine.Index = value);
        }
    }

    public DelimiterSet Delimiters
    {
        get => _delimiters;
        set
        {
            _delimiters = value;
            Post(() => _engine.Delimiters = value);
        }
    }

    public void SetExcludedApps(IEnumerable<string> apps)
    {
        _excludedApps = new HashSet<string>(apps.Select(AppFilter.Normalize).Where(a => a.Length > 0), StringComparer.OrdinalIgnoreCase);
        Post(() => _foreground = 0);
    }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _current = this;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "PhraseFlow keyboard hook",
            Priority = ThreadPriority.Highest,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_thread is null)
        {
            return;
        }

        PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _current = null;
    }

    /// <summary>True while no click or window switch happened since <paramref name="generation"/> was captured.</summary>
    public bool IsContextCurrent(int generation) => Volatile.Read(ref _contextGeneration) == generation;

    /// <summary>False once the hold identified by <paramref name="generation"/> was abandoned (the expansion took too long).</summary>
    public bool IsHoldCurrent(int generation) => Volatile.Read(ref _holdGeneration) == generation;

    /// <summary>Called by the expansion worker while it makes progress, so long expansions keep holding keys.</summary>
    public void KeepAlive() => Volatile.Write(ref _holdStarted, Environment.TickCount64);

    /// <summary>
    /// Starts holding user keystrokes (e.g. before inserting a snippet chosen in the picker or a form) and returns the
    /// current context generation and the new hold generation. Waits until the hook thread has applied it.
    /// </summary>
    public (int Context, int Hold) BeginExpansion()
    {
        var context = -1;
        var hold = -1;
        InvokeAndWait(() =>
        {
            _swallowed = null;
            hold = StartHolding();
            context = _contextGeneration;
        });
        return (context, hold);
    }

    /// <summary>
    /// Ends the hold <paramref name="hold"/> after an expansion was inserted: sets what precedes the caret, arms undo and
    /// replays held keys. Ignored if that hold was already ended or abandoned. With <paramref name="wait"/> the call
    /// returns only after the held keys were replayed.
    /// </summary>
    public void EndExpansion(int hold, string? context, UndoInfo? undo = null, bool wait = false)
    {
        void End()
        {
            if (!_expansionInFlight || _holdGeneration != hold)
            {
                return;
            }

            if (context is not null)
            {
                _engine.SetContext(context);
            }

            _expansionInFlight = false;
            _swallowed = null;
            _undo = undo is not null && undo.ContextGeneration == _contextGeneration ? undo : null;
            ReleaseHeldKeys();
        }

        if (wait)
        {
            InvokeAndWait(End);
        }
        else
        {
            Post(End);
        }
    }

    /// <summary>
    /// Gives up the hold <paramref name="hold"/> before anything was inserted: the key that completed the keyword is
    /// delivered as typed, followed by the keys held meanwhile.
    /// </summary>
    public void CancelExpansion(int hold) => Post(() =>
    {
        if (_expansionInFlight && _holdGeneration == hold)
        {
            AbandonHold();
            ReleaseHeldKeys();
        }
    });

    public void ResetBuffer() => Post(() =>
    {
        _engine.Reset();
        _undo = null;
    });

    public void SyncCapsLock(bool on) => Post(() => _capsLock = on);

    /// <summary>Modifier state from the user's own key presses (PhraseFlow's injected modifier changes are ignored).</summary>
    public bool IsUserDown(int vk) => (Volatile.Read(ref _userModifiers) & ModifierBit(vk)) != 0;

    private void Post(Action action)
    {
        _actions.Enqueue(action);
        if (_threadId != 0)
        {
            PostThreadMessageW(_threadId, InvokeMessage, 0, 0);
        }
    }

    private void InvokeAndWait(Action action)
    {
        if (_threadId == 0 || Environment.CurrentManagedThreadId == _thread?.ManagedThreadId)
        {
            action();
            return;
        }

        using var done = new ManualResetEventSlim();
        Post(() =>
        {
            try
            {
                action();
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait(TimeSpan.FromSeconds(1));
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        PeekMessageW(out _, 0, 0, 0, PM_NOREMOVE);
        var module = GetModuleHandleW(null);
        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, (nint)(delegate* unmanaged<int, nint, nint, nint>)&KeyboardProc, module, 0);
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, (nint)(delegate* unmanaged<int, nint, nint, nint>)&MouseProc, module, 0);
        _capsLock = (GetKeyState(VirtualKeys.Capital) & 1) != 0;
        if (_keyboardHook == 0)
        {
            Log.Error($"Could not install keyboard hook (error {Marshal.GetLastWin32Error()}).");
        }

        _started.Set();
        RunActions();

        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            if (message.hwnd == 0 && message.message == InvokeMessage)
            {
                RunActions();
                continue;
            }

            if (message.hwnd == 0 && message.message == WM_TIMER)
            {
                CheckStalled();
                continue;
            }

            TranslateMessage(in message);
            DispatchMessageW(in message);
        }

        if (_watchdog != 0)
        {
            KillTimer(0, _watchdog);
            _watchdog = 0;
        }

        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }

        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
    }

    private void RunActions()
    {
        while (_actions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("Hook action failed", ex);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        var hook = _current;
        if (nCode == HC_ACTION && hook is not null)
        {
            try
            {
                if (hook.OnKey((int)wParam, (KBDLLHOOKSTRUCT*)lParam))
                {
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Keyboard hook failed", ex);
            }
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        var hook = _current;
        if (nCode == HC_ACTION && hook is not null && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN or WM_NCLBUTTONDOWN)
        {
            // A click usually moves the caret, so the buffer no longer describes the text before it.
            hook.InvalidateContext();
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private void InvalidateContext()
    {
        _engine.Reset();
        _undo = null;
        _pendingDeadKey = '\0';
        Interlocked.Increment(ref _contextGeneration);
    }

    private bool OnKey(int message, KBDLLHOOKSTRUCT* data)
    {
        var vk = (int)data->vkCode;
        var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        var injected = (data->flags & LLKHF_INJECTED) != 0;
        var signature = data->dwExtraInfo;

        // PhraseFlow's own synthesized input passes untouched.
        if (injected && signature == InputInjector.Signature)
        {
            return false;
        }

        var replayed = injected && signature == InputInjector.ReplaySignature;
        if (!replayed && !_expansionInFlight && _replayPending == 0)
        {
            // Key releases can be missed (e.g. on the secure desktop), so resynchronize while PhraseFlow isn't injecting.
            _userModifiers = LogicalModifiers();
        }

        if (VirtualKeys.IsModifier(vk))
        {
            var bit = ModifierBit(vk);
            Volatile.Write(ref _userModifiers, isDown ? _userModifiers | bit : _userModifiers & ~bit);
        }

        var now = Environment.TickCount64;
        if (replayed && _replayPending > 0)
        {
            _replayPending--;
        }
        else if (_replayPending > 0 && now - _replayStarted > MaxReplayMilliseconds)
        {
            // Some replayed events never arrived (e.g. Windows rejected them); stop waiting for them.
            _replayPending = 0;
            if (!_expansionInFlight)
            {
                Post(ReleaseHeldKeys);
            }
        }

        if (_expansionInFlight && now - Volatile.Read(ref _holdStarted) > MaxHoldMilliseconds)
        {
            AbandonHold();
            Post(ReleaseHeldKeys);
        }

        // Keys typed while an expansion is being inserted are held and replayed afterwards so they land after it.
        // Shortcuts with Win or Alt pass through: holding them would let a bare Win/Alt press open menus.
        var userWin = IsUserDown(VirtualKeys.LWin) || IsUserDown(VirtualKeys.RWin);
        var userAltShortcut = IsUserDown(VirtualKeys.Menu) && !IsUserDown(VirtualKeys.Control);
        var mustHold = _expansionInFlight || (!replayed && (_replayPending > 0 || _held.Count > 0));
        if (mustHold && !VirtualKeys.IsModifier(vk) && !userWin && !userAltShortcut)
        {
            if (isDown)
            {
                HoldKey(vk, data, replayed);
                if (!_expansionInFlight && _replayPending == 0)
                {
                    Post(ReleaseHeldKeys);
                }

                return true;
            }

            if (_heldDownKeys.Remove(data->vkCode))
            {
                return true;
            }
        }

        if (!_expansionInFlight && _replayPending == 0 && _held.Count > 0)
        {
            Post(ReleaseHeldKeys);
        }

        if (!isDown)
        {
            return false;
        }

        if (vk == VirtualKeys.Capital)
        {
            _capsLock = !_capsLock;
            return false;
        }

        if (VirtualKeys.IsModifier(vk) || vk is VirtualKeys.NumLock or VirtualKeys.Scroll)
        {
            return false;
        }

        if (!Enabled)
        {
            return false;
        }

        var foreground = GetForegroundWindow();
        if (foreground != _foreground)
        {
            OnForegroundChanged(foreground);
        }

        if (_foregroundExcluded || (_foregroundIsSelf && !PlaygroundFocused))
        {
            _engine.Reset();
            return false;
        }

        var ctrl = IsDown(VirtualKeys.Control);
        var alt = IsDown(VirtualKeys.Menu);
        var shift = IsDown(VirtualKeys.Shift);
        var win = IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin);

        var undo = _undo;
        _undo = null;
        if (vk == VirtualKeys.Back)
        {
            if (undo is not null && !ctrl && !alt && !shift && !win && undo.Window == foreground && BackspaceUndoEnabled)
            {
                _engine.Reset();
                _swallowed = new HeldKey(VirtualKeys.Back, null, KeyModifiers.None);
                UndoRequested?.Invoke(undo, StartHolding());
                return true;
            }

            if (ctrl)
            {
                _engine.Reset();
            }
            else
            {
                _engine.Backspace();
            }

            return false;
        }

        if (VirtualKeys.ResetsContext(vk))
        {
            _engine.Reset();
            _pendingDeadKey = '\0';
            return false;
        }

        // Ctrl or Alt alone (or Win) means a shortcut; Ctrl+Alt together may be AltGr producing a character.
        if (win || (ctrl != alt))
        {
            _engine.Reset();
            return false;
        }

        var text = Translate(vk, data->scanCode, shift, ctrl && alt);
        if (text is null)
        {
            if (ctrl && alt)
            {
                _engine.Reset();
            }

            return false;
        }

        if (text.Length == 0)
        {
            return false;
        }

        if (text.Length > 1)
        {
            _engine.AppendWithoutMatching(text[..^1]);
        }

        var match = _engine.Type(text[^1], entry => entry.AppFilter.Allows(_foregroundProcess));
        if (match is null)
        {
            return false;
        }

        // The key that completed the keyword is blocked; it is delivered as typed if the expansion is abandoned.
        _swallowed = ToHeldKey(vk, text, ctrl, alt, shift);
        var hold = StartHolding();
        MatchFound?.Invoke(new HookMatch(match, foreground, _foregroundProcess, _contextGeneration, hold));
        return true;
    }

    /// <summary>Starts holding keys for a new expansion and returns its hold generation.</summary>
    private int StartHolding()
    {
        _expansionInFlight = true;
        Volatile.Write(ref _holdStarted, Environment.TickCount64);
        _heldDownKeys.Clear();
        EnsureWatchdog();
        return Interlocked.Increment(ref _holdGeneration);
    }

    /// <summary>
    /// Stops holding for the current expansion, which must then be skipped. The blocked key that completed the keyword
    /// goes first in the replay. The caller replays the held keys.
    /// </summary>
    private void AbandonHold()
    {
        _expansionInFlight = false;
        Interlocked.Increment(ref _holdGeneration);
        _engine.Reset();
        _undo = null;
        if (_swallowed is { } key)
        {
            _held.Insert(0, key);
            _heldFront++;
            _swallowed = null;
        }

        Post(() => Log.Warn("An expansion was abandoned; the keys typed meanwhile were released."));
    }

    /// <summary>Runs periodically on the hook thread while keys are held, so a stalled expansion or replay never wedges typing.</summary>
    private void CheckStalled()
    {
        var now = Environment.TickCount64;
        if (_expansionInFlight && now - Volatile.Read(ref _holdStarted) > MaxHoldMilliseconds)
        {
            AbandonHold();
        }

        if (_replayPending > 0 && now - _replayStarted > MaxReplayMilliseconds)
        {
            _replayPending = 0;
        }

        if (!_expansionInFlight && _replayPending == 0)
        {
            if (_held.Count > 0)
            {
                ReleaseHeldKeys();
            }
            else if (_watchdog != 0)
            {
                KillTimer(0, _watchdog);
                _watchdog = 0;
            }
        }
    }

    private void EnsureWatchdog()
    {
        if (_watchdog == 0)
        {
            _watchdog = SetTimer(0, 0, WatchdogIntervalMilliseconds, 0);
        }
    }

    private static int LogicalModifiers()
    {
        var bits = 0;
        foreach (var vk in ModifierKeys)
        {
            if (IsDown(vk))
            {
                bits |= ModifierBit(vk);
            }
        }

        return bits;
    }

    private void OnForegroundChanged(nint window)
    {
        _foreground = window;
        InvalidateContext();
        var pid = WindowHelper.GetProcessId(window);
        _foregroundIsSelf = pid == (uint)Environment.ProcessId;
        _foregroundProcess = WindowHelper.GetProcessName(pid);
        _foregroundExcluded = (_foregroundProcess is not null && _excludedApps.Contains(AppFilter.Normalize(_foregroundProcess)))
                              || WindowHelper.IsInputBlocked(pid);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Converts a key press to the text it produces in the foreground window's keyboard layout.</summary>
    private string? Translate(int vk, uint scanCode, bool shift, bool altGr)
    {
        if (vk == VirtualKeys.Packet)
        {
            return ((char)scanCode).ToString();
        }

        var state = stackalloc byte[256];
        new Span<byte>(state, 256).Clear();
        if (shift)
        {
            state[VirtualKeys.Shift] = 0x80;
            state[VirtualKeys.LShift] = 0x80;
        }

        if (altGr)
        {
            state[VirtualKeys.Control] = 0x80;
            state[VirtualKeys.LControl] = 0x80;
            state[VirtualKeys.Menu] = 0x80;
            state[VirtualKeys.RMenu] = 0x80;
        }

        if (_capsLock)
        {
            state[VirtualKeys.Capital] = 0x01;
        }

        var buffer = stackalloc char[8];
        var layout = WindowHelper.GetKeyboardLayoutFor(_foreground);

        // Flag 0x4 keeps the system's dead-key state untouched (Windows 10 1607+).
        var count = ToUnicodeEx((uint)vk, scanCode, state, buffer, 8, 0x4, layout);
        if (count < 0)
        {
            _pendingDeadKey = buffer[0];
            return "";
        }

        if (count == 0)
        {
            _pendingDeadKey = '\0';
            return null;
        }

        var text = new string(buffer, 0, count);
        if (text.Length == 1 && char.IsControl(text[0]) && text[0] is not ('\r' or '\n' or '\t'))
        {
            _pendingDeadKey = '\0';
            return null;
        }

        if (_pendingDeadKey != '\0')
        {
            text = ComposeDeadKey(_pendingDeadKey, text);
            _pendingDeadKey = '\0';
        }

        return text;
    }

    private static string ComposeDeadKey(char deadKey, string text)
    {
        if (text == " ")
        {
            return deadKey.ToString();
        }

        char? combining = deadKey switch
        {
            '`' => '\u0300',
            '´' or '\'' => '\u0301',
            '^' or 'ˆ' => '\u0302',
            '~' or '˜' => '\u0303',
            '¯' => '\u0304',
            '˘' => '\u0306',
            '˙' => '\u0307',
            '¨' or '"' => '\u0308',
            '°' or '˚' => '\u030A',
            '˝' => '\u030B',
            'ˇ' => '\u030C',
            '¸' => '\u0327',
            '˛' => '\u0328',
            _ => null,
        };
        if (combining is not null)
        {
            var composed = (text[0].ToString() + combining.Value).Normalize(NormalizationForm.FormC);
            if (composed.Length == 1)
            {
                return composed + text[1..];
            }
        }

        return deadKey + text;
    }

    private void HoldKey(int vk, KBDLLHOOKSTRUCT* data, bool replayed)
    {
        _heldDownKeys.Add(data->vkCode);

        // Use the user's own modifier state: PhraseFlow may have released Shift/AltGr temporarily for its injection.
        var ctrl = IsUserDown(VirtualKeys.Control) || (!_expansionInFlight && IsDown(VirtualKeys.Control));
        var alt = IsUserDown(VirtualKeys.Menu) || (!_expansionInFlight && IsDown(VirtualKeys.Menu));
        var shift = IsUserDown(VirtualKeys.Shift) || (!_expansionInFlight && IsDown(VirtualKeys.Shift));
        string? text = null;
        if (ctrl == alt && vk != VirtualKeys.Back && !VirtualKeys.ResetsContext(vk))
        {
            var saved = _pendingDeadKey;
            text = Translate(vk, data->scanCode, shift, ctrl && alt);
            if (text is "")
            {
                text = null;
                _pendingDeadKey = saved;
            }
        }

        var key = ToHeldKey(vk, text, ctrl, alt, shift);
        if (replayed)
        {
            // Replayed keys were typed before any key held since the replay started.
            _held.Insert(_heldFront++, key);
        }
        else
        {
            _held.Add(key);
        }

        EnsureWatchdog();
    }

    /// <summary>Printable text is replayed as Unicode (independent of modifiers); other keys as key chords.</summary>
    private static HeldKey ToHeldKey(int vk, string? text, bool ctrl, bool alt, bool shift)
    {
        if (text is { Length: > 0 } && text.All(c => !char.IsControl(c)))
        {
            return new HeldKey(0, text, KeyModifiers.None);
        }

        var modifiers = (ctrl ? KeyModifiers.Ctrl : 0) | (alt ? KeyModifiers.Alt : 0) | (shift ? KeyModifiers.Shift : 0);
        return new HeldKey(vk, null, ctrl && alt && text is not null ? KeyModifiers.None : modifiers);
    }

    /// <summary>Replays held keys as replay-signed input, which the hook then processes like normal typing.</summary>
    private void ReleaseHeldKeys()
    {
        if (_held.Count == 0)
        {
            return;
        }

        var inputs = new List<INPUT>();
        foreach (var key in _held)
        {
            if (key.Text is not null)
            {
                foreach (var c in key.Text)
                {
                    inputs.Add(InputInjector.Unicode(c, up: false, InputInjector.ReplaySignature));
                    inputs.Add(InputInjector.Unicode(c, up: true, InputInjector.ReplaySignature));
                }

                continue;
            }

            var modifiers = new List<int>();
            if (key.Modifiers.HasFlag(KeyModifiers.Ctrl) && !IsDown(VirtualKeys.Control))
            {
                modifiers.Add(VirtualKeys.LControl);
            }

            if (key.Modifiers.HasFlag(KeyModifiers.Alt) && !IsDown(VirtualKeys.Menu))
            {
                modifiers.Add(VirtualKeys.LMenu);
            }

            if (key.Modifiers.HasFlag(KeyModifiers.Shift) && !IsDown(VirtualKeys.Shift))
            {
                modifiers.Add(VirtualKeys.LShift);
            }

            foreach (var m in modifiers)
            {
                inputs.Add(InputInjector.Key(m, up: false, InputInjector.ReplaySignature));
            }

            inputs.Add(InputInjector.Key(key.VirtualKey, up: false, InputInjector.ReplaySignature));
            inputs.Add(InputInjector.Key(key.VirtualKey, up: true, InputInjector.ReplaySignature));
            for (var i = modifiers.Count - 1; i >= 0; i--)
            {
                inputs.Add(InputInjector.Key(modifiers[i], up: true, InputInjector.ReplaySignature));
            }
        }

        _held.Clear();
        _heldFront = 0;
        _heldDownKeys.Clear();
        _replayStarted = Environment.TickCount64;

        // Count the events before sending: SendInput on this thread runs the hook re-entrantly for each of them.
        _replayPending += inputs.Count;
        var accepted = InputInjector.Send(inputs);
        if (accepted < inputs.Count)
        {
            _replayPending = Math.Max(0, _replayPending - (inputs.Count - accepted));
            if (_replayPending == 0 && !_expansionInFlight && _held.Count > 0)
            {
                Post(ReleaseHeldKeys);
            }
        }

        EnsureWatchdog();
    }

    private static int ModifierBit(int vk) => vk switch
    {
        VirtualKeys.LShift => 1,
        VirtualKeys.RShift => 2,
        VirtualKeys.LControl => 4,
        VirtualKeys.RControl => 8,
        VirtualKeys.LMenu => 16,
        VirtualKeys.RMenu => 32,
        VirtualKeys.LWin => 64,
        VirtualKeys.RWin => 128,
        VirtualKeys.Shift => 3,
        VirtualKeys.Control => 12,
        VirtualKeys.Menu => 48,
        _ => 0,
    };

    private readonly record struct HeldKey(int VirtualKey, string? Text, KeyModifiers Modifiers);
}
