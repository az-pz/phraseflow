using CommunityToolkit.Mvvm.ComponentModel;
using PhraseFlow.Core.Matching;

namespace PhraseFlow.Core.Models;

/// <summary>User preferences. Persisted as JSON next to the snippet library.</summary>
public sealed partial class AppSettings : ObservableObject
{
    /// <summary>Default punctuation that completes a keyword (same set AutoHotkey uses for hotstrings).</summary>
    public const string DefaultTriggerCharacters = "-()[]{}':;\"/\\,.?!";

    public const string DefaultExcludedApps = "KeePass.exe\nKeePassXC.exe\n1Password.exe\nBitwarden.exe";

    // General
    [ObservableProperty]
    private bool _expansionEnabled = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized = true;

    [ObservableProperty]
    private ThemePreference _theme = ThemePreference.System;

    [ObservableProperty]
    private bool _playSound;

    [ObservableProperty]
    private bool _showNotifications = true;

    // Triggers
    [ObservableProperty]
    private bool _triggerOnSpace = true;

    [ObservableProperty]
    private bool _triggerOnTab = true;

    [ObservableProperty]
    private bool _triggerOnEnter = true;

    [ObservableProperty]
    private string _triggerCharacters = DefaultTriggerCharacters;

    [ObservableProperty]
    private bool _backspaceUndo = true;

    // Insertion
    [ObservableProperty]
    private InsertMethod _insertMethod = InsertMethod.Auto;

    [ObservableProperty]
    private int _clipboardThreshold = 80;

    [ObservableProperty]
    private PasteShortcut _pasteShortcut = PasteShortcut.CtrlV;

    [ObservableProperty]
    private int _clipboardRestoreDelayMs = 400;

    [ObservableProperty]
    private int _typingDelayMs;

    // Hotkeys
    [ObservableProperty]
    private string _pickerHotkey = "Ctrl+Alt+Space";

    [ObservableProperty]
    private string _toggleHotkey = "Ctrl+Alt+Shift+P";

    // Applications
    [ObservableProperty]
    private string _excludedApps = DefaultExcludedApps;

    // Advanced
    [ObservableProperty]
    private bool _allowScripts;

    [ObservableProperty]
    private int _scriptTimeoutSeconds = 5;

    [ObservableProperty]
    private int _maxBackups = 20;

    [ObservableProperty]
    private bool _hasShownTrayHint;

    public DelimiterSet BuildDelimiterSet() =>
        DelimiterSet.Create(TriggerOnSpace, TriggerOnTab, TriggerOnEnter, TriggerCharacters ?? "");

    public void Normalize()
    {
        TriggerCharacters ??= DefaultTriggerCharacters;
        ExcludedApps ??= "";
        PickerHotkey ??= "";
        ToggleHotkey ??= "";
        if (InsertMethod == InsertMethod.Default)
        {
            InsertMethod = InsertMethod.Auto;
        }

        ClipboardThreshold = Math.Clamp(ClipboardThreshold, 0, 100_000);
        ClipboardRestoreDelayMs = Math.Clamp(ClipboardRestoreDelayMs, 50, 5_000);
        TypingDelayMs = Math.Clamp(TypingDelayMs, 0, 500);
        ScriptTimeoutSeconds = Math.Clamp(ScriptTimeoutSeconds, 1, 120);
        MaxBackups = Math.Clamp(MaxBackups, 0, 500);
    }
}
