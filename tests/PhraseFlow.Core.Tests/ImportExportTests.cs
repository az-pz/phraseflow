using PhraseFlow.Core.IO;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Storage;

namespace PhraseFlow.Core.Tests;

public class ImportExportTests
{
    [Fact]
    public void ImportsCsvWithHeaderAndGroups()
    {
        const string csv = "Abbreviation,Content,Label,Folder\r\n;sig,\"Best,\r\nJane\",Signature,Mail\r\nbtw,\"say \"\"hi\"\"\",,\r\n";
        var result = SnippetImporter.ImportCsv(csv, "Imported");
        Assert.Equal(2, result.Groups.Count);
        var mail = result.Groups.Single(g => g.Name == "Mail");
        Assert.Equal(";sig", mail.Snippets[0].Keyword);
        Assert.Equal("Best,\nJane", mail.Snippets[0].Content);
        Assert.Equal("Signature", mail.Snippets[0].Name);
        Assert.Equal("say \"hi\"", result.Groups.Single(g => g.Name == "Imported").Snippets[0].Content);
    }

    [Fact]
    public void ImportsHeaderlessTabSeparatedFiles()
    {
        var result = SnippetImporter.ImportCsv("addr\t1 Main St\tAddress\nph\t555-0100\n", "TextExpander");
        Assert.Equal("TSV", result.Format);
        var group = Assert.Single(result.Groups);
        Assert.Equal(["addr", "ph"], group.Snippets.Select(s => s.Keyword));
        Assert.Equal("Address", group.Snippets[0].Name);
    }

    [Fact]
    public void CsvExportRoundTrips()
    {
        var group = new SnippetGroup { Name = "G" };
        group.Snippets.Add(new Snippet { Keyword = ";x", Content = "a, \"b\"\nc", Name = "X", Trigger = TriggerMode.Immediate, CaseMode = CaseMode.Sensitive });
        var csv = SnippetExporter.ToCsv([group]);
        var imported = SnippetImporter.ImportCsv(csv, "fallback");
        var snippet = Assert.Single(Assert.Single(imported.Groups).Snippets);
        Assert.Equal("a, \"b\"\nc", snippet.Content);
        Assert.Equal(TriggerMode.Immediate, snippet.Trigger);
        Assert.Equal(CaseMode.Sensitive, snippet.CaseMode);
        Assert.Equal("G", imported.Groups[0].Name);
    }

    [Fact]
    public void ImportsAutoHotkeyHotstrings()
    {
        const string ahk = """
            ; comment
            #Hotstring EndChars -()[]{}:;'"/\,.?!`n `t
            ::btw::by the way
            :*:]d::{Raw}today
            :C:AHK::AutoHotkey
            :?O:ing::ING ; trailing comment
            ::sig::
            (
            Best regards,
            Jane
            )
            ::run::
                Run "notepad.exe"
            return
            ::ty::Thank you{!}{Enter}See you{Tab 2}x
            """;
        var result = SnippetImporter.ImportAutoHotkey(ahk, "AHK");
        var snippets = Assert.Single(result.Groups).Snippets.ToDictionary(s => s.Keyword);
        Assert.Equal("by the way", snippets["btw"].Content);
        Assert.Equal(TriggerMode.Immediate, snippets["]d"].Trigger);
        Assert.Equal("today", snippets["]d"].Content);
        Assert.Equal(CaseMode.Sensitive, snippets["AHK"].CaseMode);
        Assert.True(snippets["ing"].ExpandInsideWords);
        Assert.True(snippets["ing"].OmitDelimiter);
        Assert.Equal("ING", snippets["ing"].Content);
        Assert.Equal("Best regards,\nJane", snippets["sig"].Content);
        Assert.Equal("Thank you!\nSee you{key:Tab|2}x", snippets["ty"].Content);
        Assert.False(snippets.ContainsKey("run"));
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void ImportsEspansoMatchFiles()
    {
        const string yaml = """
            matches:
              - trigger: ":hello"
                replace: "Hello $|$ world"
              - triggers: [":dt", ":today"]
                replace: "Today is {{mydate}}"
                vars:
                  - name: mydate
                    type: date
                    params:
                      format: "%A, %d %B %Y"
              - trigger: "alh"
                replace: "although"
                word: true
                propagate_case: true
              - trigger: ":clip"
                replace: "[{{clipboard}}]"
                vars:
                  - name: clipboard
                    type: clipboard
              - trigger: ":greet"
                form: "Hey [[name]], pick [[choice]]"
                form_fields:
                  choice:
                    type: choice
                    values:
                      - one
                      - two
              - regex: "(?P<x>\\d+)x"
                replace: "{{x}}"
            """;
        var result = SnippetImporter.ImportEspanso(yaml, "espanso");
        var snippets = Assert.Single(result.Groups).Snippets.ToDictionary(s => s.Keyword);
        Assert.Equal("Hello {cursor} world", snippets[":hello"].Content);
        Assert.Equal(TriggerMode.Immediate, snippets[":hello"].Trigger);
        Assert.Equal("Today is {datetime:dddd, dd MMMM yyyy}", snippets[":dt"].Content);
        Assert.Equal(snippets[":dt"].Content, snippets[":today"].Content);
        Assert.Equal(TriggerMode.Delimiter, snippets["alh"].Trigger);
        Assert.Equal(CaseMode.Adapt, snippets["alh"].CaseMode);
        Assert.Equal("[{clipboard}]", snippets[":clip"].Content);
        Assert.Equal("Hey {input:name}, pick {choice:choice|one|two}", snippets[":greet"].Content);
        Assert.Contains(result.Warnings, w => w.Contains("regex"));

        var preview = TestHost.Text(snippets[":dt"].Content);
        Assert.Equal("Today is Wednesday, 07 October 2026", preview);
    }

    [Theory]
    [InlineData("%Y-%m-%d", "yyyy-MM-dd")]
    [InlineData("%H:%M:%S", "HH':'mm':'ss")]
    [InlineData("%-d/%-m/%y", "%d'/'%M'/'yy")]
    [InlineData("%I:%M %p", "hh':'mm tt")]
    [InlineData("Week of %b %e", "'Week of 'MMM %d")]
    public void ConvertsStrftimeFormats(string strftime, string expected)
    {
        Assert.Equal(expected, SnippetImporter.ConvertStrftime(strftime));
    }

    [Fact]
    public void ImportsBeeftextJson()
    {
        const string json = """
            {
              "fileFormatVersion": 9,
              "groups": [ { "uuid": "{g1}", "name": "Work", "description": "Work combos", "enabled": true } ],
              "combos": [
                { "uuid": "{c1}", "name": "Signature", "keyword": "zsig", "snippet": "Regards #{dateTime:yyyy-MM-dd hh:mm}#{cursor}", "group": "{g1}", "enabled": true },
                { "uuid": "{c2}", "name": "Clip", "keyword": "zclip", "snippet": "#{upper:zsig} #{clipboard} #{input:Who}", "enabled": false }
              ]
            }
            """;
        var result = SnippetImporter.ImportJson(json, "Beeftext");
        Assert.Equal("Beeftext", result.Format);
        Assert.Equal(["Work", "Beeftext"], result.Groups.Select(g => g.Name));
        Assert.Equal("Regards {datetime:yyyy-MM-dd HH:mm}{cursor}", result.Groups[0].Snippets[0].Content);
        var clip = result.Groups[1].Snippets[0];
        Assert.Equal("{upper:{snippet:zsig}} {clipboard} {input:Who}", clip.Content);
        Assert.False(clip.Enabled);
    }

    [Fact]
    public void PhraseFlowJsonRoundTripsWithNewIdentities()
    {
        var group = new SnippetGroup { Name = "Mine", Description = "d", AppFilter = AppFilterMode.OnlyListed, Apps = "code.exe" };
        var snippet = new Snippet { Keyword = ";k", Content = "{date}", Name = "n", Trigger = TriggerMode.PickerOnly };
        group.Snippets.Add(snippet);
        var json = SnippetExporter.ToJson([group]);
        var result = SnippetImporter.ImportJson(json, "x");
        var imported = Assert.Single(result.Groups);
        Assert.Equal("Mine", imported.Name);
        Assert.Equal(AppFilterMode.OnlyListed, imported.AppFilter);
        Assert.NotEqual(group.Id, imported.Id);
        var importedSnippet = Assert.Single(imported.Snippets);
        Assert.Equal(TriggerMode.PickerOnly, importedSnippet.Trigger);
        Assert.NotEqual(snippet.Id, importedSnippet.Id);
    }

    [Fact]
    public void AutoHotkeyExportProducesHotstrings()
    {
        var group = new SnippetGroup { Name = "G" };
        group.Snippets.Add(new Snippet { Keyword = "btw", Content = "by the way" });
        group.Snippets.Add(new Snippet { Keyword = ":x:", Content = "line1\nline2", Trigger = TriggerMode.Immediate, CaseMode = CaseMode.Sensitive });
        var ahk = SnippetExporter.ToAutoHotkey([group]);
        Assert.Contains(":T:btw::by the way", ahk);
        Assert.Contains(":T*C::x:::", ahk);
    }

    [Fact]
    public void MergeImportCombinesGroupsAndSkipsDuplicates()
    {
        var library = new SnippetLibrary();
        var existing = new SnippetGroup { Name = "Mail" };
        existing.Snippets.Add(new Snippet { Keyword = ";a", Content = "A" });
        library.Groups.Add(existing);

        var incoming = new SnippetGroup { Name = "mail" };
        incoming.Snippets.Add(new Snippet { Keyword = ";a", Content = "A" });
        incoming.Snippets.Add(new Snippet { Keyword = ";b", Content = "B" });
        var other = new SnippetGroup { Name = "Other" };
        other.Snippets.Add(new Snippet { Keyword = ";c", Content = "C" });

        var result = LibraryMerger.MergeImport(library, [incoming, other]);
        Assert.Equal(new MergeResult(1, 2, 1), result);
        Assert.Equal(2, library.Groups.Count);
        Assert.Equal(2, existing.Snippets.Count);
    }
}

public class StorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "phraseflow-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void SavesLoadsAndBacksUpTheLibrary()
    {
        var paths = new DataPaths(_directory);
        var store = new LibraryStore(paths.LibraryFile, paths.BackupDirectory);
        var library = DefaultLibrary.CreateLibrary();
        library.Counters["invoice"] = 41;
        library.Statistics.TotalExpansions = 5;
        library.Groups[0].Snippets[0].UseCount = 3;
        store.Save(library);

        var loaded = store.Load();
        Assert.Equal(library.AllSnippets.Count(), loaded.AllSnippets.Count());
        Assert.Equal(41, loaded.Counters["INVOICE"]);
        Assert.Equal(5, loaded.Statistics.TotalExpansions);
        Assert.Equal(3, loaded.Groups[0].Snippets[0].UseCount);
        Assert.Equal(library.Groups[0].Snippets[0].Id, loaded.Groups[0].Snippets[0].Id);

        var json = File.ReadAllText(paths.LibraryFile);
        Assert.Contains("\"trigger\": \"delimiter\"", json);
        Assert.Contains(loaded.AllSnippets, s => s.Content == "😄");

        Assert.NotNull(store.CreateBackup(keep: 2));
        Assert.Single(store.ListBackups());
    }

    [Fact]
    public void SettingsFallBackToDefaultsAndRoundTrip()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var defaults = store.Load();
        Assert.True(defaults.ExpansionEnabled);
        Assert.Equal(InsertMethod.Auto, defaults.InsertMethod);

        defaults.PickerHotkey = "Ctrl+Shift+Space";
        defaults.ClipboardRestoreDelayMs = 1;
        store.Save(defaults);
        var loaded = store.Load();
        Assert.Equal("Ctrl+Shift+Space", loaded.PickerHotkey);
        Assert.Equal(50, loaded.ClipboardRestoreDelayMs);

        File.WriteAllText(store.FilePath, "{ not json");
        Assert.Equal("Ctrl+Alt+Space", store.Load().PickerHotkey);
    }

    [Fact]
    public void PortableMarkerSwitchesDataFolder()
    {
        Directory.CreateDirectory(_directory);
        Assert.NotEqual(Path.Combine(_directory, "Data"), DataPaths.Resolve(_directory).Root);
        File.WriteAllText(Path.Combine(_directory, DataPaths.PortableMarker), "");
        Assert.Equal(Path.Combine(_directory, "Data"), DataPaths.Resolve(_directory).Root);
    }
}
