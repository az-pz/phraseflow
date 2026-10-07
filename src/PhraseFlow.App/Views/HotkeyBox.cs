using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhraseFlow.App.Services;

namespace PhraseFlow.App.Views;

/// <summary>Text box that records a key combination such as "Ctrl+Alt+Space".</summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(string),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((HotkeyBox)d).ShowHotkey()));

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Arrow;
        ToolTip = "Click, then press the key combination";
    }

    public string Hotkey
    {
        get => (string)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        AppController.Current?.SuspendHotkeys(true);
        Text = "Press keys…";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        AppController.Current?.SuspendHotkeys(false);
        ShowHotkey();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if (key == Key.Tab && modifiers == ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        if (key is Key.Back or Key.Delete or Key.Escape && modifiers == ModifierKeys.None)
        {
            Hotkey = "";
            Text = key == Key.Escape ? "" : "None";
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        if (modifiers == ModifierKeys.None && !win)
        {
            Text = "Add Ctrl, Alt, Shift or Win";
            return;
        }

        var name = KeyName(key);
        if (name is null)
        {
            return;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (win)
        {
            parts.Add("Win");
        }

        parts.Add(name);
        Hotkey = string.Join("+", parts);
        Text = Hotkey;
    }

    private void ShowHotkey() => Text = string.IsNullOrWhiteSpace(Hotkey) ? "None" : Hotkey;

    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => key.ToString(),
        Key.Space => "Space",
        Key.Enter => "Enter",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDn",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Pause => "Pause",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.Oem1 => ";",
        Key.Oem2 => "/",
        Key.Oem3 => "`",
        Key.Oem4 => "[",
        Key.Oem5 => "\\",
        Key.Oem6 => "]",
        Key.Oem7 => "'",
        _ => null,
    };
}
