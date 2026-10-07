using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Placeholders;
using PhraseFlow.Core.Storage;

namespace PhraseFlow.Core.Tests;

public class DefaultLibraryTests
{
    private static readonly IReadOnlyList<SnippetGroup> Groups = DefaultLibrary.CreateGroups();

    private static IEnumerable<(SnippetGroup Group, Snippet Snippet)> All =>
        Groups.SelectMany(g => g.Snippets.Select(s => (g, s)));

    [Fact]
    public void ShipsAVastLibrary()
    {
        Assert.True(Groups.Count >= 12, $"Only {Groups.Count} groups");
        Assert.True(All.Count() >= 1200, $"Only {All.Count()} snippets");
        Assert.All(Groups, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Name));
            Assert.False(string.IsNullOrWhiteSpace(g.SourceId));
            Assert.NotEmpty(g.Snippets);
        });
        Assert.Equal(Groups.Count, Groups.Select(g => g.SourceId).Distinct().Count());
    }

    [Fact]
    public void KeywordsAreWellFormed()
    {
        Assert.All(All, item =>
        {
            Assert.False(string.IsNullOrEmpty(item.Snippet.Keyword), $"Empty keyword in {item.Group.Name}");
            Assert.False(item.Snippet.Keyword.Any(char.IsWhiteSpace), $"Whitespace in keyword '{item.Snippet.Keyword}'");
            Assert.False(string.IsNullOrEmpty(item.Snippet.Content), $"Empty content for '{item.Snippet.Keyword}'");
        });
    }

    [Fact]
    public void KeywordsAreUnique()
    {
        var duplicates = All
            .GroupBy(x => x.Snippet.Keyword, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Where(g => !g.All(x => x.Snippet.CaseMode == CaseMode.Sensitive) ||
                        g.Select(x => x.Snippet.Keyword).Distinct(StringComparer.Ordinal).Count() != g.Count())
            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(x => x.Group.Name))})")
            .ToList();
        Assert.True(duplicates.Count == 0, "Duplicate keywords: " + string.Join("; ", duplicates));
    }

    [Fact]
    public void NoKeywordFiresBeforeAnotherOneIsComplete()
    {
        var delimiters = DelimiterSet.Default;
        var snippets = All.Select(x => x.Snippet).ToList();
        var conflicts = new List<string>();
        foreach (var shorter in snippets)
        {
            var k = shorter.Keyword;
            foreach (var longer in snippets)
            {
                var l = longer.Keyword;
                if (ReferenceEquals(shorter, longer) || l.Length <= k.Length)
                {
                    continue;
                }

                for (var start = l.IndexOf(k, StringComparison.OrdinalIgnoreCase); start >= 0;
                     start = l.IndexOf(k, start + 1, StringComparison.OrdinalIgnoreCase))
                {
                    var end = start + k.Length;
                    var boundaryOk = shorter.ExpandInsideWords || !WordChars.IsWordChar(k[0]) || start == 0 || !WordChars.IsWordChar(l[start - 1]);
                    if (!boundaryOk)
                    {
                        continue;
                    }

                    var fires = shorter.Trigger switch
                    {
                        TriggerMode.Immediate => end < l.Length || longer.Trigger == TriggerMode.Delimiter,
                        TriggerMode.Delimiter => end < l.Length && delimiters.Contains(l[end]),
                        _ => false,
                    };
                    if (fires)
                    {
                        conflicts.Add($"'{k}' fires while typing '{l}'");
                    }
                }
            }
        }

        Assert.True(conflicts.Count == 0, string.Join("; ", conflicts.Distinct()));
    }

    [Fact]
    public void AllContentEvaluatesWithoutErrors()
    {
        var library = DefaultLibrary.CreateLibrary();
        var index = KeywordIndex.Build(library.Groups);
        var host = new DelegateExpansionHost
        {
            Clock = () => TestHost.Now,
            Clipboard = () => "https://example.com",
            SnippetLookup = k => index.FindByKeyword(k)?.ToReference(),
        };

        var errors = new List<string>();
        foreach (var (group, snippet) in All)
        {
            var output = TemplateEvaluator.Expand(snippet.Content, snippet.PlainText, host, mode: EvaluationMode.Preview, culture: TestHost.Culture);
            errors.AddRange(output.Errors.Select(e => $"{snippet.Keyword}: {e}"));
            _ = TemplateEvaluator.GetFields(snippet.Content, snippet.PlainText, host, TestHost.Culture);
        }

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void CommonSnippetsBehaveAsDocumented()
    {
        var library = DefaultLibrary.CreateLibrary();
        var engine = new TypingEngine { Index = KeywordIndex.Build(library.Groups) };

        TypingMatch? TypeAll(string text)
        {
            engine.Reset();
            TypingMatch? match = null;
            foreach (var c in text)
            {
                match = engine.Type(c) ?? match;
            }

            return match;
        }

        Assert.Equal("the", TypeAll("teh ")!.Entry.Content);
        Assert.Equal("don't", TypeAll("dont ")!.Entry.Content);
        Assert.Equal("by the way", TypeAll(";btw ")!.Entry.Content);
        Assert.Equal("😄", TypeAll(":smile:")!.Entry.Content);
        Assert.Equal("Δ", TypeAll("\\Delta ")!.Entry.Content);
        Assert.Equal("→", TypeAll(";-> ")!.Entry.Content);
        Assert.Equal("½", TypeAll(";1/2 ")!.Entry.Content);
        Assert.Equal("{date}", TypeAll(";date ")!.Entry.Content);
        Assert.Null(TypeAll("the "));
        Assert.Null(TypeAll("Monday "));
    }

    [Fact]
    public void RestoreDefaultsOnlyAddsMissingItems()
    {
        var library = DefaultLibrary.CreateLibrary();
        var total = library.AllSnippets.Count();
        Assert.Equal(new MergeResult(0, 0, total), LibraryMerger.RestoreDefaults(library));

        var autocorrect = library.Groups.Single(g => g.SourceId == "autocorrect");
        autocorrect.Snippets.RemoveAt(0);
        library.Groups.Remove(library.Groups.Single(g => g.SourceId == "emoji"));
        var emojiCount = DefaultLibrary.CreateGroups().Single(g => g.SourceId == "emoji").Snippets.Count;

        var result = LibraryMerger.RestoreDefaults(library);
        Assert.Equal(1, result.GroupsAdded);
        Assert.Equal(emojiCount + 1, result.SnippetsAdded);
        Assert.Equal(total, library.AllSnippets.Count());
    }
}
