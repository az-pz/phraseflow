using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using PhraseFlow.App.Native;
using PhraseFlow.App.Views;
using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Placeholders;
using PhraseFlow.Core.Storage;

namespace PhraseFlow.App.Services;

/// <summary>Composition root: owns the data, the global hook, the expansion pipeline and the shell integration.</summary>
internal sealed class AppController : IExpansionUi, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _settingsStore;
    private readonly DispatcherTimer _settingsSaveTimer;
    private HotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private LibraryWatcher? _watcher;
    private ExpansionService? _expansion;
    private int? _pickerHotkeyId;
    private int? _toggleHotkeyId;
    private PickerWindow? _picker;
    private bool _exiting;

    public AppController(DataPaths paths)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Paths = paths;
        _settingsStore = new SettingsStore(paths.SettingsFile);
        Store = new LibraryStore(paths.LibraryFile, paths.BackupDirectory);
        Settings = _settingsStore.Load();
        Library = LoadLibrary();
        Index = KeywordIndex.Build(Library.Groups);
        Host = new AppExpansionHost(() => Index, () => Settings, Library, () => _dispatcher.BeginInvoke(() => _watcher?.MarkDirty()));
        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SaveSettings();
        };
    }

    public static AppController Current { get; private set; } = null!;

    public DataPaths Paths { get; }

    public LibraryStore Store { get; }

    public AppSettings Settings { get; }

    public SnippetLibrary Library { get; private set; }

    public KeywordIndex Index { get; private set; }

    public IExpansionHost Host { get; }

    public KeyboardHook Hook { get; } = new();

    public string? LastExpansion { get; private set; }

    /// <summary>Raised on the UI thread after a snippet was expanded or the index changed.</summary>
    public event Action? StatusChanged;

    public event Action? NewSnippetRequested;

    public void Start(bool showWindow)
    {
        Current = this;
        ApplyTheme();
        _hotkeys = new HotkeyService();
        ClipboardService.OwnerWindow = _hotkeys.Handle;

        _watcher = new LibraryWatcher(Library, RebuildIndex, SaveLibrary);
        Hook.Index = Index;
        ApplySettingsToHook();
        Hook.Start();
        if (!Hook.IsRunning)
        {
            MessageBox.Show("PhraseFlow could not install its keyboard hook, so snippets will not expand.", "PhraseFlow", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _expansion = new ExpansionService(Hook, () => Settings, Host, this);

        _tray = new TrayIconService();
        _tray.OpenRequested += () => ShowMainWindow();
        _tray.PickerRequested += () => _dispatcher.BeginInvoke(ShowPicker, DispatcherPriority.Background);
        _tray.NewSnippetRequested += () =>
        {
            ShowMainWindow();
            NewSnippetRequested?.Invoke();
        };
        _tray.PauseToggled += () => Settings.ExpansionEnabled = !Settings.ExpansionEnabled;
        _tray.ExitRequested += Exit;

        RegisterHotkeys();
        Settings.PropertyChanged += OnSettingsChanged;
        StartupService.Apply(Settings.StartWithWindows);
        UpdateTray();

        var window = new MainWindow();
        Application.Current.MainWindow = window;
        window.Closing += OnMainWindowClosing;
        if (showWindow)
        {
            window.Show();
        }

        Log.Info($"Started with {Index.Count} active snippets.");
    }

    public void ShowMainWindow()
    {
        _dispatcher.BeginInvoke(() =>
        {
            var window = Application.Current.MainWindow;
            if (window is null)
            {
                return;
            }

            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            WindowHelper.Activate(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        });
    }

    public void ShowPicker()
    {
        if (_picker is not null)
        {
            _picker.Activate();
            return;
        }

        var target = WindowHelper.Foreground;
        _picker = new PickerWindow(Index, Host, target);
        _picker.Chosen += (entry, copy) => _expansion?.InsertFromPicker(entry, target, copy);
        _picker.Closed += (_, _) => _picker = null;
        _picker.Show();
    }

    public void Exit()
    {
        _exiting = true;
        Application.Current.Shutdown();
    }

    public void RebuildIndex()
    {
        Index = KeywordIndex.Build(Library.Groups);
        Hook.Index = Index;
        UpdateTray();
        StatusChanged?.Invoke();
    }

    public void SaveLibrary()
    {
        try
        {
            string json;
            lock (Library.Counters)
            {
                json = LibraryStore.Serialize(Library);
            }

            AtomicFile.WriteAllText(Store.FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Saving the library failed", ex);
            _tray?.ShowNotification("PhraseFlow", "Your snippets could not be saved: " + ex.Message, warning: true);
        }
    }

    public void SaveSettings()
    {
        try
        {
            _settingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Saving settings failed", ex);
        }
    }

    public string? CreateBackup()
    {
        try
        {
            SaveLibrary();
            return Store.CreateBackup(Math.Max(1, Settings.MaxBackups));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Backup failed", ex);
            return null;
        }
    }

    /// <summary>Replaces the library contents with a backup (a backup of the current state is made first).</summary>
    public void RestoreBackup(string path)
    {
        var restored = LibraryStore.Deserialize(File.ReadAllText(path));
        CreateBackup();
        Library.Groups.Clear();
        foreach (var group in restored.Groups)
        {
            Library.Groups.Add(group);
        }

        lock (Library.Counters)
        {
            Library.Counters.Clear();
            foreach (var (key, value) in restored.Counters)
            {
                Library.Counters[key] = value;
            }
        }

        Library.Statistics.TotalExpansions = restored.Statistics.TotalExpansions;
        Library.Statistics.CharactersSaved = restored.Statistics.CharactersSaved;
    }

    public void OpenDataFolder()
    {
        Directory.CreateDirectory(Paths.Root);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.Root}\"") { UseShellExecute = true });
    }

    public void SetPlaygroundFocused(bool focused) => Hook.PlaygroundFocused = focused;

    // ------------------------------------------------------------------ IExpansionUi

    public IReadOnlyDictionary<string, string>? ShowForm(SnippetEntry entry, IReadOnlyList<FormField> fields, nint targetWindow) =>
        _dispatcher.Invoke(() =>
        {
            var form = new FillInWindow(entry, fields, Host, targetWindow);
            return form.ShowDialog() == true ? form.Values : null;
        });

    public void OnExpanded(SnippetEntry entry, string typed, ExpansionOutput output)
    {
        _dispatcher.BeginInvoke(() =>
        {
            entry.Snippet.UseCount++;
            entry.Snippet.LastUsed = DateTimeOffset.Now;
            Library.Statistics.TotalExpansions++;
            Library.Statistics.CharactersSaved += Math.Max(0, output.Text.Length - typed.Length);
            var preview = output.Text.ReplaceLineEndings(" ⏎ ");
            LastExpansion = $"{(typed.Length > 0 ? typed : entry.Keyword)} → {(preview.Length > 60 ? preview[..60] + "…" : preview)}";
            if (Settings.PlaySound)
            {
                SoundService.Play();
            }

            StatusChanged?.Invoke();
        });
    }

    public void OnError(string message)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (Settings.ShowNotifications)
            {
                _tray?.ShowNotification("PhraseFlow", message, warning: true);
            }
        });
    }

    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _expansion?.Dispose();
        Hook.Dispose();
        _watcher?.Dispose();
        SaveLibrary();
        SaveSettings();
        _hotkeys?.Dispose();
        _tray?.Dispose();
    }

    // ------------------------------------------------------------------ internals

    private SnippetLibrary LoadLibrary()
    {
        if (!Store.Exists)
        {
            Log.Info("First run: creating the default snippet library.");
            var defaults = DefaultLibrary.CreateLibrary();
            Store.Save(defaults);
            return defaults;
        }

        try
        {
            var library = Store.Load();
            var newest = Store.ListBackups().FirstOrDefault();
            if (newest is null || DateTime.Now - newest.LastWriteTime > TimeSpan.FromHours(12))
            {
                Store.CreateBackup(Math.Max(1, Settings.MaxBackups));
            }

            return library;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            Log.Error("The library could not be loaded", ex);
            var corrupt = Store.FilePath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(Store.FilePath, corrupt, overwrite: true);
            MessageBox.Show(
                $"Your snippet library could not be read and was saved as:\n{corrupt}\n\nPhraseFlow will start with the default library. You can restore a backup from Settings.",
                "PhraseFlow",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            var defaults = DefaultLibrary.CreateLibrary();
            Store.Save(defaults);
            return defaults;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ExpansionEnabled):
                Hook.Enabled = Settings.ExpansionEnabled;
                Hook.ResetBuffer();
                UpdateTray();
                StatusChanged?.Invoke();
                break;
            case nameof(AppSettings.TriggerOnSpace):
            case nameof(AppSettings.TriggerOnTab):
            case nameof(AppSettings.TriggerOnEnter):
            case nameof(AppSettings.TriggerCharacters):
            case nameof(AppSettings.ExcludedApps):
            case nameof(AppSettings.BackspaceUndo):
            case nameof(AppSettings.ScriptTimeoutSeconds):
                ApplySettingsToHook();
                break;
            case nameof(AppSettings.PickerHotkey):
            case nameof(AppSettings.ToggleHotkey):
                RegisterHotkeys();
                UpdateTray();
                break;
            case nameof(AppSettings.StartWithWindows):
                StartupService.Apply(Settings.StartWithWindows);
                break;
            case nameof(AppSettings.Theme):
                ApplyTheme();
                break;
        }
    }

    private void ApplySettingsToHook()
    {
        Hook.Enabled = Settings.ExpansionEnabled;
        Hook.BackspaceUndoEnabled = Settings.BackspaceUndo;
        Hook.Delimiters = Settings.BuildDelimiterSet();
        Hook.SetExcludedApps(AppFilter.ParseList(Settings.ExcludedApps));

        // Script placeholders run before anything is typed; keep holding keys for as long as a script may take.
        Hook.MaxHoldMilliseconds = Math.Max(5000, Settings.ScriptTimeoutSeconds * 1000 + 2000);
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null)
        {
            return;
        }

        _hotkeys.Unregister(_pickerHotkeyId);
        _hotkeys.Unregister(_toggleHotkeyId);
        _pickerHotkeyId = _hotkeys.Register(Settings.PickerHotkey, ShowPicker);
        _toggleHotkeyId = _hotkeys.Register(Settings.ToggleHotkey, () => Settings.ExpansionEnabled = !Settings.ExpansionEnabled);
        if (!string.IsNullOrWhiteSpace(Settings.PickerHotkey) && _pickerHotkeyId is null)
        {
            Log.Warn($"Picker hotkey '{Settings.PickerHotkey}' could not be registered.");
            _tray?.ShowNotification("PhraseFlow", $"The shortcut {Settings.PickerHotkey} is used by another application. Choose another one in Settings.", warning: true);
        }
    }

    public bool IsHotkeyRegistered(string which) => which == nameof(AppSettings.PickerHotkey) ? _pickerHotkeyId is not null : _toggleHotkeyId is not null;

    /// <summary>Temporarily releases the global hotkeys, e.g. while the user records a new shortcut.</summary>
    public void SuspendHotkeys(bool suspend)
    {
        if (suspend)
        {
            _hotkeys?.Unregister(_pickerHotkeyId);
            _hotkeys?.Unregister(_toggleHotkeyId);
            _pickerHotkeyId = _toggleHotkeyId = null;
        }
        else
        {
            RegisterHotkeys();
        }
    }

    private void ApplyTheme()
    {
#pragma warning disable WPF0001
        Application.Current.ThemeMode = Settings.Theme switch
        {
            ThemePreference.Light => ThemeMode.Light,
            ThemePreference.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001
    }

    private void UpdateTray() => _tray?.Update(Settings.ExpansionEnabled, Index.Count, Settings.PickerHotkey);

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        e.Cancel = true;
        ((Window)sender!).Hide();
        if (!Settings.HasShownTrayHint)
        {
            Settings.HasShownTrayHint = true;
            _tray?.ShowNotification("PhraseFlow is still running", "Snippets keep expanding in the background. Use the tray icon to open PhraseFlow or exit.");
        }
    }
}
