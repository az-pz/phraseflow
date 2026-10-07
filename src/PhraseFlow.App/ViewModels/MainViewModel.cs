using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhraseFlow.App.Services;
using PhraseFlow.App.Views;
using PhraseFlow.Core.IO;
using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Placeholders;
using PhraseFlow.Core.Storage;

namespace PhraseFlow.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly ListCollectionView _view;
    private readonly Dictionary<Snippet, SnippetItem> _items = [];
    private readonly Dictionary<SnippetGroup, GroupItem> _groupItems = [];
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _previewTimer;
    private string[] _queryTokens = [];
    private Snippet? _observedSnippet;

    internal MainViewModel(AppController app)
    {
        _app = app;
        AllGroupsItem = new GroupItem(null);
        Groups.Add(AllGroupsItem);
        foreach (var group in Library.Groups)
        {
            AttachGroup(group);
        }

        Library.Groups.CollectionChanged += OnLibraryGroupsChanged;
        _view = new ListCollectionView(Snippets) { Filter = FilterSnippet };
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _queryTokens = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _view.Refresh();
        };
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };

        _selectedGroup = AllGroupsItem;
        Placeholders = BuildPlaceholderDocs();
        var placeholderView = new ListCollectionView(Placeholders.ToList());
        placeholderView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlaceholderDoc.Category)));
        PlaceholdersView = placeholderView;

        _app.StatusChanged += UpdateStatus;
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.PickerHotkey) or nameof(AppSettings.ToggleHotkey))
            {
                OnPropertyChanged(nameof(PickerHint));
                OnPropertyChanged(nameof(PickerHotkeyStatus));
                OnPropertyChanged(nameof(ToggleHotkeyStatus));
            }
        };
        UpdateStatus();
    }

    public event Action? FocusKeywordRequested;

    public event Action<SnippetItem>? ScrollIntoViewRequested;

    public AppSettings Settings => _app.Settings;

    public SnippetLibrary Library => _app.Library;

    public LibraryStatistics Statistics => _app.Library.Statistics;

    public string DataFolder => _app.Paths.Root;

    public string VersionText => "Version " + (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0");

    public string PickerHint => string.IsNullOrWhiteSpace(Settings.PickerHotkey)
        ? "Search all snippets and insert one"
        : $"Search all snippets and insert one ({Settings.PickerHotkey} anywhere)";

    public string PickerHotkeyStatus => HotkeyStatus(nameof(AppSettings.PickerHotkey), Settings.PickerHotkey);

    public string ToggleHotkeyStatus => HotkeyStatus(nameof(AppSettings.ToggleHotkey), Settings.ToggleHotkey);

    public ObservableCollection<GroupItem> Groups { get; } = [];

    public ObservableCollection<SnippetItem> Snippets { get; } = [];

    public ICollectionView SnippetsView => _view;

    public GroupItem AllGroupsItem { get; }

    public IReadOnlyList<PlaceholderDoc> Placeholders { get; }

    public ICollectionView PlaceholdersView { get; }

    public IReadOnlyList<SnippetItem> SelectedItems { get; set; } = [];

    public IReadOnlyList<Option<TriggerMode>> TriggerOptions { get; } =
    [
        new(TriggerMode.Delimiter, "After a trigger key (space, Enter, punctuation…)"),
        new(TriggerMode.Immediate, "Immediately when the keyword is typed"),
        new(TriggerMode.PickerOnly, "Never automatically (search picker only)"),
    ];

    public IReadOnlyList<Option<CaseMode>> CaseOptions { get; } =
    [
        new(CaseMode.Adapt, "Any case, mirror it (btw → by the way, BTW → BY THE WAY)"),
        new(CaseMode.Sensitive, "Case-sensitive (type the keyword exactly)"),
        new(CaseMode.Insensitive, "Any case, insert as written"),
    ];

    public IReadOnlyList<Option<InsertMethod>> SnippetMethodOptions { get; } =
    [
        new(InsertMethod.Default, "Default (from Settings)"),
        new(InsertMethod.Auto, "Automatic"),
        new(InsertMethod.Typing, "Simulate typing"),
        new(InsertMethod.Clipboard, "Paste via clipboard"),
    ];

    public IReadOnlyList<Option<InsertMethod>> GlobalMethodOptions { get; } =
    [
        new(InsertMethod.Auto, "Automatic: type short text, paste long or multi-line text"),
        new(InsertMethod.Typing, "Always simulate typing"),
        new(InsertMethod.Clipboard, "Always paste via the clipboard"),
    ];

    public IReadOnlyList<Option<PasteShortcut>> PasteOptions { get; } =
    [
        new(PasteShortcut.CtrlV, "Ctrl+V"),
        new(PasteShortcut.ShiftInsert, "Shift+Insert (terminals, older apps)"),
    ];

    public IReadOnlyList<Option<ThemePreference>> ThemeOptions { get; } =
    [
        new(ThemePreference.System, "Use Windows setting"),
        new(ThemePreference.Light, "Light"),
        new(ThemePreference.Dark, "Dark"),
    ];

    [ObservableProperty]
    private GroupItem? _selectedGroup;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorGroup), nameof(UsageText), nameof(HasSelection))]
    private SnippetItem? _selectedSnippet;

    [ObservableProperty]
    private string _previewText = "";

    [ObservableProperty]
    private string _previewInfo = "";

    [ObservableProperty]
    private bool _previewHasErrors;

    [ObservableProperty]
    private string _keywordWarning = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _lastExpansion = "";

    [ObservableProperty]
    private string _runningApp = "";

    public bool HasSelection => SelectedSnippet is not null;

    public IReadOnlyList<string> RunningApps =>
        Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != 0; } catch (InvalidOperationException) { return false; } })
            .Select(p => p.ProcessName + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public SnippetGroup? EditorGroup
    {
        get => SelectedSnippet?.Group;
        set
        {
            if (value is not null && SelectedSnippet is { } item && value != item.Group)
            {
                MoveSnippets([item], value);
            }
        }
    }

    public string UsageText
    {
        get
        {
            if (SelectedSnippet?.Snippet is not { } s)
            {
                return "";
            }

            var used = s.UseCount switch
            {
                0 => "Not used yet",
                1 => "Used once",
                _ => $"Used {s.UseCount:N0} times",
            };
            return s.LastUsed is { } last ? $"{used} · last {last.LocalDateTime:g}" : used;
        }
    }

    // ------------------------------------------------------------------ snippet commands

    [RelayCommand]
    private void NewSnippet()
    {
        var group = SelectedGroup?.Group ?? Library.Groups.FirstOrDefault(g => g.SourceId is null) ?? CreateUserGroup();
        var snippet = new Snippet
        {
            Keyword = "",
            Name = "New snippet",
            Content = "",
            Trigger = TriggerMode.Delimiter,
        };
        group.Snippets.Insert(0, snippet);
        SelectSnippet(snippet);
        FocusKeywordRequested?.Invoke();
    }

    [RelayCommand]
    private void DuplicateSnippet()
    {
        if (SelectedSnippet is not { } item)
        {
            return;
        }

        var copy = item.Snippet.Clone();
        copy.Name = string.IsNullOrEmpty(copy.Name) ? "Copy" : copy.Name + " (copy)";
        var index = item.Group.Snippets.IndexOf(item.Snippet);
        item.Group.Snippets.Insert(index + 1, copy);
        SelectSnippet(copy);
        FocusKeywordRequested?.Invoke();
    }

    [RelayCommand]
    private void DeleteSnippets()
    {
        var items = SelectionOrCurrent();
        if (items.Count == 0)
        {
            return;
        }

        var message = items.Count == 1
            ? $"Delete the snippet \"{Describe(items[0].Snippet)}\"?"
            : $"Delete {items.Count} snippets?";
        if (!Dialogs.Confirm(message + "\n\nA backup of your library is kept in the data folder."))
        {
            return;
        }

        if (items.Count > 5)
        {
            _app.CreateBackup();
        }

        foreach (var item in items)
        {
            item.Group.Snippets.Remove(item.Snippet);
        }
    }

    [RelayCommand]
    private void EnableSelected() => SetEnabled(true);

    [RelayCommand]
    private void DisableSelected() => SetEnabled(false);

    [RelayCommand]
    private void MoveSelectedTo(SnippetGroup? group)
    {
        if (group is not null)
        {
            MoveSnippets(SelectionOrCurrent(), group);
        }
    }

    // ------------------------------------------------------------------ group commands

    [RelayCommand]
    private void NewGroup()
    {
        var name = Dialogs.Prompt("New group", "Group name", "My snippets");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var group = new SnippetGroup { Name = name.Trim() };
        Library.Groups.Insert(0, group);
        SelectedGroup = _groupItems[group];
    }

    [RelayCommand]
    private void EditGroup(GroupItem? item)
    {
        if ((item ?? SelectedGroup)?.Group is not { } group)
        {
            return;
        }

        new GroupDialog(group, RunningApps) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        _view.Refresh();
    }

    [RelayCommand]
    private void DeleteGroup(GroupItem? item)
    {
        if ((item ?? SelectedGroup)?.Group is not { } group)
        {
            return;
        }

        if (!Dialogs.Confirm($"Delete the group \"{group.Name}\" and its {group.Snippets.Count:N0} snippets?\n\nA backup is created first; built-in groups can be brought back with \"Restore default snippets\"."))
        {
            return;
        }

        _app.CreateBackup();
        Library.Groups.Remove(group);
        SelectedGroup = AllGroupsItem;
    }

    [RelayCommand]
    private void ChangeGroupPrefix(GroupItem? item)
    {
        if ((item ?? SelectedGroup)?.Group is not { } group || group.Snippets.Count == 0)
        {
            return;
        }

        var current = CommonPrefix(group.Snippets.Select(s => s.Keyword).Where(k => k.Length > 0).ToList());
        var from = Dialogs.Prompt("Change keyword prefix", $"Replace this prefix in all keywords of \"{group.Name}\":", current);
        if (from is null)
        {
            return;
        }

        var to = Dialogs.Prompt("Change keyword prefix", $"Replace \"{from}\" with:", from == ";" ? "//" : ";");
        if (to is null || to == from)
        {
            return;
        }

        var changed = 0;
        foreach (var snippet in group.Snippets.Where(s => from.Length > 0 && s.Keyword.StartsWith(from, StringComparison.Ordinal)))
        {
            snippet.Keyword = to + snippet.Keyword[from.Length..];
            changed++;
        }

        Dialogs.Info($"Updated {changed:N0} keywords.");
    }

    [RelayCommand]
    private void ExportGroup(GroupItem? item)
    {
        if ((item ?? SelectedGroup)?.Group is { } group)
        {
            Export([group], group.Name);
        }
    }

    [RelayCommand]
    private void ExportAll() => Export(Library.Groups.ToList(), "PhraseFlow snippets");

    [RelayCommand]
    private void Import()
    {
        var path = Dialogs.OpenFile(SnippetImporter.FileDialogFilter);
        if (path is null)
        {
            return;
        }

        try
        {
            var result = SnippetImporter.ImportFile(path);
            if (result.SnippetCount == 0)
            {
                Dialogs.Info("No snippets were found in this file." + FormatWarnings(result.Warnings));
                return;
            }

            if (!Dialogs.Confirm($"Import {result.SnippetCount:N0} snippets in {result.Groups.Count} group(s) from this {result.Format} file?"))
            {
                return;
            }

            _app.CreateBackup();
            var merge = LibraryMerger.MergeImport(Library, result.Groups);
            Dialogs.Info($"Imported {merge.SnippetsAdded:N0} snippets ({merge.GroupsAdded} new groups, {merge.SnippetsSkipped:N0} duplicates skipped)." + FormatWarnings(result.Warnings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException
                                       or System.Text.Json.JsonException or YamlDotNet.Core.YamlException or KeyNotFoundException or InvalidOperationException)
        {
            Dialogs.Error("The file could not be imported:\n" + ex.Message);
        }
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        if (!Dialogs.Confirm("Add any built-in snippets and groups that are missing from your library?\n\nNothing you have changed is overwritten."))
        {
            return;
        }

        _app.CreateBackup();
        var result = LibraryMerger.RestoreDefaults(Library);
        Dialogs.Info(result.SnippetsAdded == 0
            ? "Your library already contains every built-in snippet."
            : $"Added {result.SnippetsAdded:N0} snippets ({result.GroupsAdded} groups).");
    }

    [RelayCommand]
    private void BackupNow()
    {
        var path = _app.CreateBackup();
        Dialogs.Info(path is null ? "The backup could not be created." : $"Backup saved:\n{path}");
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        var path = Dialogs.OpenFile("PhraseFlow library|*.json", _app.Store.BackupDirectory);
        if (path is null || !Dialogs.Confirm("Replace your current snippets with this backup?\n\nYour current library is backed up first."))
        {
            return;
        }

        try
        {
            _app.RestoreBackup(path);
            SelectedGroup = AllGroupsItem;
            Dialogs.Info("The backup was restored.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            Dialogs.Error("The backup could not be restored:\n" + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => _app.OpenDataFolder();

    [RelayCommand]
    private void ShowPicker() => _app.ShowPicker();

    [RelayCommand]
    private void ResetTriggerCharacters()
    {
        Settings.TriggerCharacters = AppSettings.DefaultTriggerCharacters;
        Settings.TriggerOnSpace = Settings.TriggerOnTab = Settings.TriggerOnEnter = true;
    }

    [RelayCommand]
    private void AddExcludedApp()
    {
        var app = RunningApp.Trim();
        if (app.Length == 0)
        {
            return;
        }

        var existing = AppFilter.ParseList(Settings.ExcludedApps).ToList();
        if (!existing.Contains(app, StringComparer.OrdinalIgnoreCase))
        {
            existing.Add(app);
            Settings.ExcludedApps = string.Join("\n", existing);
        }

        RunningApp = "";
    }

    [RelayCommand]
    private void ResetStatistics()
    {
        if (!Dialogs.Confirm("Reset usage counts and statistics for all snippets?"))
        {
            return;
        }

        foreach (var snippet in Library.AllSnippets)
        {
            snippet.UseCount = 0;
            snippet.LastUsed = null;
        }

        Statistics.TotalExpansions = 0;
        Statistics.CharactersSaved = 0;
        UpdateStatus();
    }

    [RelayCommand]
    private void Exit() => _app.Exit();

    public void SetPlaygroundFocused(bool focused) => _app.SetPlaygroundFocused(focused);

    public void SelectSnippet(Snippet snippet)
    {
        if (!_items.TryGetValue(snippet, out var item))
        {
            return;
        }

        if (!_view.PassesFilter(item))
        {
            SearchText = "";
            _queryTokens = [];
            if (SelectedGroup is { IsAll: false } && SelectedGroup.Group != item.Group)
            {
                SelectedGroup = AllGroupsItem;
            }

            _view.Refresh();
        }

        SelectedSnippet = item;
        ScrollIntoViewRequested?.Invoke(item);
    }

    // ------------------------------------------------------------------ property hooks

    partial void OnSelectedGroupChanged(GroupItem? value) => _view.Refresh();

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnSelectedSnippetChanged(SnippetItem? value)
    {
        if (_observedSnippet is not null)
        {
            _observedSnippet.PropertyChanged -= OnObservedSnippetChanged;
        }

        _observedSnippet = value?.Snippet;
        if (_observedSnippet is not null)
        {
            _observedSnippet.PropertyChanged += OnObservedSnippetChanged;
        }

        UpdatePreview();
        UpdateKeywordWarning();
    }

    private void OnObservedSnippetChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Snippet.Content):
            case nameof(Snippet.PlainText):
                _previewTimer.Stop();
                _previewTimer.Start();
                break;
            case nameof(Snippet.Keyword):
            case nameof(Snippet.Trigger):
                UpdateKeywordWarning();
                break;
            case nameof(Snippet.UseCount):
            case nameof(Snippet.LastUsed):
                OnPropertyChanged(nameof(UsageText));
                break;
        }
    }

    // ------------------------------------------------------------------ helpers

    private void UpdatePreview()
    {
        if (SelectedSnippet?.Snippet is not { } snippet)
        {
            PreviewText = PreviewInfo = "";
            PreviewHasErrors = false;
            return;
        }

        var fields = TemplateEvaluator.GetFields(snippet.Content, snippet.PlainText, _app.Host);
        var output = TemplateEvaluator.Expand(snippet.Content, snippet.PlainText, _app.Host, FormField.PreviewValues(fields), EvaluationMode.Preview, CultureInfo.CurrentCulture);
        PreviewText = output.ToPreviewString();
        var info = new List<string>();
        if (fields.Count > 0)
        {
            info.Add($"Asks for: {string.Join(", ", fields.Select(f => f.Label))}");
        }

        if (output.HasCursor)
        {
            info.Add("▮ marks where the cursor ends up");
        }

        info.AddRange(output.Errors.Select(e => "⚠ " + e));
        PreviewInfo = string.Join("\n", info);
        PreviewHasErrors = output.Errors.Count > 0;
    }

    private void UpdateKeywordWarning()
    {
        if (SelectedSnippet?.Snippet is not { } snippet)
        {
            KeywordWarning = "";
            return;
        }

        var keyword = snippet.Keyword;
        if (keyword.Length == 0)
        {
            KeywordWarning = "No keyword yet: this snippet can only be inserted from the search picker.";
            return;
        }

        if (keyword.Any(char.IsWhiteSpace))
        {
            KeywordWarning = "Keywords cannot contain spaces, tabs or line breaks.";
            return;
        }

        var duplicates = Library.Groups
            .SelectMany(g => g.Snippets.Where(s => s != snippet && string.Equals(s.Keyword, keyword, StringComparison.OrdinalIgnoreCase)).Select(s => (g, s)))
            .Take(3)
            .Select(x => $"{Describe(x.s)} ({x.g.Name})")
            .ToList();
        KeywordWarning = duplicates.Count > 0 ? "Also used by: " + string.Join(", ", duplicates) : "";
    }

    private void UpdateStatus()
    {
        var active = _app.Index.Count;
        var total = Snippets.Count;
        var state = Settings.ExpansionEnabled ? "Expansion on" : "Paused";
        StatusText = $"{state} · {active:N0} of {total:N0} snippets active in {Library.Groups.Count} groups · " +
                     $"{Statistics.TotalExpansions:N0} expansions · {Statistics.CharactersSaved:N0} characters saved";
        LastExpansion = _app.LastExpansion is { } last ? "Last: " + last : "";
        OnPropertyChanged(nameof(UsageText));
    }

    private string HotkeyStatus(string which, string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return "Not set";
        }

        return _app.IsHotkeyRegistered(which) ? "Active" : "Unavailable: another app uses this shortcut";
    }

    private bool FilterSnippet(object value)
    {
        var item = (SnippetItem)value;
        if (SelectedGroup is { IsAll: false } group && item.Group != group.Group)
        {
            return false;
        }

        foreach (var token in _queryTokens)
        {
            var s = item.Snippet;
            if (!s.Keyword.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !s.Name.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !s.Content.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private void SetEnabled(bool enabled)
    {
        foreach (var item in SelectionOrCurrent())
        {
            item.Snippet.Enabled = enabled;
        }
    }

    private List<SnippetItem> SelectionOrCurrent()
    {
        var items = SelectedItems.ToList();
        if (items.Count == 0 && SelectedSnippet is not null)
        {
            items.Add(SelectedSnippet);
        }

        return items;
    }

    private void MoveSnippets(IReadOnlyList<SnippetItem> items, SnippetGroup target)
    {
        var moved = new List<Snippet>();
        foreach (var item in items.Where(i => i.Group != target).ToList())
        {
            item.Group.Snippets.Remove(item.Snippet);
            target.Snippets.Add(item.Snippet);
            moved.Add(item.Snippet);
        }

        if (moved.Count > 0)
        {
            SelectSnippet(moved[0]);
        }
    }

    private SnippetGroup CreateUserGroup()
    {
        var group = new SnippetGroup { Name = "My snippets", Description = "Your own snippets." };
        Library.Groups.Insert(0, group);
        return group;
    }

    private void Export(IReadOnlyList<SnippetGroup> groups, string name)
    {
        var path = Dialogs.SaveFile("PhraseFlow JSON|*.json|CSV spreadsheet|*.csv|AutoHotkey hotstrings|*.ahk", name);
        if (path is null)
        {
            return;
        }

        try
        {
            var content = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".csv" => SnippetExporter.ToCsv(groups),
                ".ahk" => SnippetExporter.ToAutoHotkey(groups),
                _ => SnippetExporter.ToJson(groups),
            };
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Dialogs.Info($"Exported {groups.Sum(g => g.Snippets.Count):N0} snippets to\n{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error("Export failed:\n" + ex.Message);
        }
    }

    private IReadOnlyList<PlaceholderDoc> BuildPlaceholderDocs()
    {
        var host = new DelegateExpansionHost
        {
            Clipboard = () => "copied text",
            SnippetLookup = k => _app.Index.FindByKeyword(k)?.ToReference(),
            FullName = _app.Host.FullName,
        };
        return PlaceholderCatalog.All.Select(p =>
        {
            var output = TemplateEvaluator.Expand(p.Example, false, host, mode: EvaluationMode.Preview);
            return new PlaceholderDoc(p.Category, p.Syntax.Replace("  ", "   or   "), p.Description, p.Example, output.ToPreviewString(), p.Template);
        }).ToList();
    }

    private static string Describe(Snippet snippet) =>
        snippet.Keyword.Length > 0 ? snippet.Keyword : snippet.Name.Length > 0 ? snippet.Name : snippet.Preview;

    private static string FormatWarnings(IReadOnlyList<string> warnings) =>
        warnings.Count == 0 ? "" : "\n\nNotes:\n• " + string.Join("\n• ", warnings.Take(10)) + (warnings.Count > 10 ? $"\n… and {warnings.Count - 10} more" : "");

    private static string CommonPrefix(IReadOnlyList<string> keywords)
    {
        if (keywords.Count == 0)
        {
            return "";
        }

        var prefix = new string(keywords[0].TakeWhile(c => !char.IsLetterOrDigit(c)).ToArray());
        return keywords.All(k => k.StartsWith(prefix, StringComparison.Ordinal)) ? prefix : "";
    }

    // ------------------------------------------------------------------ library synchronisation

    private void AttachGroup(SnippetGroup group, int? position = null)
    {
        var item = new GroupItem(group) { Count = group.Snippets.Count };
        _groupItems[group] = item;
        if (position is { } index && index + 1 <= Groups.Count)
        {
            Groups.Insert(index + 1, item);
        }
        else
        {
            Groups.Add(item);
        }

        group.Snippets.CollectionChanged += OnGroupSnippetsChanged;
        foreach (var snippet in group.Snippets)
        {
            AddItem(snippet, group);
        }

        AllGroupsItem.Count = Snippets.Count;
    }

    private void DetachGroup(SnippetGroup group)
    {
        group.Snippets.CollectionChanged -= OnGroupSnippetsChanged;
        if (_groupItems.Remove(group, out var item))
        {
            Groups.Remove(item);
        }

        foreach (var snippet in group.Snippets)
        {
            RemoveItem(snippet);
        }

        AllGroupsItem.Count = Snippets.Count;
    }

    private void AddItem(Snippet snippet, SnippetGroup group)
    {
        var item = new SnippetItem(snippet, group);
        _items[snippet] = item;
        Snippets.Add(item);
    }

    private void RemoveItem(Snippet snippet)
    {
        if (_items.Remove(snippet, out var item))
        {
            Snippets.Remove(item);
            if (SelectedSnippet == item)
            {
                SelectedSnippet = null;
            }
        }
    }

    private void OnLibraryGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var group in _groupItems.Keys.ToList())
            {
                DetachGroup(group);
            }

            foreach (var group in Library.Groups)
            {
                AttachGroup(group);
            }

            return;
        }

        foreach (var group in e.OldItems?.OfType<SnippetGroup>() ?? [])
        {
            DetachGroup(group);
        }

        var position = e.NewStartingIndex;
        foreach (var group in e.NewItems?.OfType<SnippetGroup>() ?? [])
        {
            AttachGroup(group, position >= 0 ? position++ : null);
        }
    }

    private void OnGroupSnippetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var group = _groupItems.Keys.FirstOrDefault(g => ReferenceEquals(g.Snippets, sender));
        if (group is null)
        {
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var snippet in _items.Where(kv => kv.Value.Group == group).Select(kv => kv.Key).ToList())
            {
                RemoveItem(snippet);
            }

            foreach (var snippet in group.Snippets)
            {
                AddItem(snippet, group);
            }
        }
        else
        {
            foreach (var snippet in e.OldItems?.OfType<Snippet>() ?? [])
            {
                RemoveItem(snippet);
            }

            foreach (var snippet in e.NewItems?.OfType<Snippet>() ?? [])
            {
                AddItem(snippet, group);
            }
        }

        _groupItems[group].Count = group.Snippets.Count;
        AllGroupsItem.Count = Snippets.Count;
    }
}
