using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.Core.Tests;

public class TypingEngineTests
{
    private static TypingEngine CreateEngine(params Snippet[] snippets)
    {
        var group = new SnippetGroup { Name = "Test" };
        foreach (var snippet in snippets)
        {
            group.Snippets.Add(snippet);
        }

        return new TypingEngine { Index = KeywordIndex.Build([group]) };
    }

    private static TypingMatch? TypeText(TypingEngine engine, string text, Func<SnippetEntry, bool>? filter = null)
    {
        TypingMatch? last = null;
        foreach (var c in text)
        {
            last = engine.Type(c, filter);
            if (last is not null)
            {
                return last;
            }
        }

        return last;
    }

    [Fact]
    public void ExpandsAfterDelimiter()
    {
        var engine = CreateEngine(new Snippet { Keyword = "teh", Content = "the" });
        Assert.Null(TypeText(engine, "teh"));
        var match = engine.Type(' ');
        Assert.NotNull(match);
        Assert.Equal("teh", match.TypedKeyword);
        Assert.Equal(' ', match.TriggerChar);
        Assert.True(match.IsDelimiterTrigger);
        Assert.Equal(3, match.CharactersToDelete);
        Assert.Equal("teh ", match.OriginalText);
    }

    [Theory]
    [InlineData("steh ")]
    [InlineData("teha ")]
    [InlineData("teh")]
    public void DoesNotExpandInsideWordsOrWithoutDelimiter(string typed)
    {
        var engine = CreateEngine(new Snippet { Keyword = "teh", Content = "the" });
        Assert.Null(TypeText(engine, typed));
    }

    [Fact]
    public void ExpandInsideWordsOptionAllowsSuffixMatches()
    {
        var engine = CreateEngine(new Snippet { Keyword = "teh", Content = "the", ExpandInsideWords = true });
        Assert.NotNull(TypeText(engine, "steh."));
    }

    [Fact]
    public void ImmediateTriggerFiresOnLastCharacter()
    {
        var engine = CreateEngine(new Snippet { Keyword = ":smile:", Content = "😄", Trigger = TriggerMode.Immediate });
        var match = TypeText(engine, "hi :smile:");
        Assert.NotNull(match);
        Assert.False(match.IsDelimiterTrigger);
        Assert.Equal(':', match.TriggerChar);
        Assert.Equal(6, match.CharactersToDelete);
        Assert.Equal(":smile:", match.OriginalText);
    }

    [Fact]
    public void PrefixedKeywordsNeedNoWordBoundary()
    {
        var engine = CreateEngine(new Snippet { Keyword = ";date", Content = "{date}" });
        Assert.NotNull(TypeText(engine, "today:;date "));
        engine.Reset();
        Assert.NotNull(TypeText(engine, "abc;date\n"));
    }

    [Fact]
    public void PrefersTheLongestKeyword()
    {
        var engine = CreateEngine(
            new Snippet { Keyword = "date", Content = "short" },
            new Snippet { Keyword = ";date", Content = "long" });
        var match = TypeText(engine, ";date ");
        Assert.Equal(";date", match!.Entry.Keyword);
    }

    [Fact]
    public void CaseSensitiveKeywordsMustMatchExactly()
    {
        var engine = CreateEngine(
            new Snippet { Keyword = "\\Gamma", Content = "Γ", CaseMode = CaseMode.Sensitive },
            new Snippet { Keyword = "\\gamma", Content = "γ", CaseMode = CaseMode.Sensitive });
        Assert.Equal("Γ", TypeText(engine, "\\Gamma ")!.Entry.Content);
        Assert.Equal("γ", TypeText(engine, "\\gamma ")!.Entry.Content);
        Assert.Null(TypeText(engine, "\\GAMMA "));
    }

    [Fact]
    public void BackspaceAndResetUpdateTheBuffer()
    {
        var engine = CreateEngine(new Snippet { Keyword = "teh", Content = "the" });
        TypeText(engine, "tehx");
        engine.Backspace();
        Assert.Equal("teh", engine.BufferText);
        Assert.NotNull(engine.Type(' '));

        TypeText(engine, "te");
        engine.Reset();
        Assert.Null(TypeText(engine, "h "));
    }

    [Fact]
    public void BufferStartCountsAsWordBoundary()
    {
        var engine = CreateEngine(new Snippet { Keyword = "teh", Content = "the" });
        TypeText(engine, "xx");
        engine.SetContext(" ");
        Assert.NotNull(TypeText(engine, "teh."));
    }

    [Fact]
    public void SkipsExpansionsThatWouldNotChangeAnything()
    {
        var engine = CreateEngine(new Snippet { Keyword = "monday", Content = "Monday" });
        Assert.Null(TypeText(engine, "Monday "));
        Assert.Null(TypeText(engine, "MONDAY "));
        Assert.NotNull(TypeText(engine, "monday "));
    }

    [Fact]
    public void RespectsFiltersAndPickerOnlyAndDisabledSnippets()
    {
        var engine = CreateEngine(
            new Snippet { Keyword = ";a", Content = "A" },
            new Snippet { Keyword = ";b", Content = "B", Trigger = TriggerMode.PickerOnly },
            new Snippet { Keyword = ";c", Content = "C", Enabled = false });
        Assert.Null(TypeText(engine, ";a ", _ => false));
        Assert.NotNull(TypeText(engine, ";a ", _ => true));
        Assert.Null(TypeText(engine, ";b "));
        Assert.Null(TypeText(engine, ";c "));
        Assert.Equal(2, engine.Index.Count);
    }

    [Fact]
    public void DelimitersAreConfigurable()
    {
        var engine = CreateEngine(new Snippet { Keyword = "teh", Content = "the" });
        engine.Delimiters = DelimiterSet.Create(space: false, tab: true, enter: false, punctuation: "");
        Assert.Null(TypeText(engine, "teh "));
        engine.Reset();
        Assert.Null(TypeText(engine, "teh."));
        engine.Reset();
        Assert.NotNull(TypeText(engine, "teh\t"));
    }

    [Fact]
    public void CarriageReturnIsTreatedAsEnter()
    {
        var engine = CreateEngine(new Snippet { Keyword = "teh", Content = "the" });
        var match = TypeText(engine, "teh\r");
        Assert.Equal('\n', match!.TriggerChar);
    }

    [Fact]
    public void BufferKeepsWorkingPastCapacity()
    {
        var engine = CreateEngine(new Snippet { Keyword = "teh", Content = "the" });
        TypeText(engine, new string('x', TypingEngine.Capacity * 3) + " ");
        Assert.NotNull(TypeText(engine, "teh "));
    }

    [Fact]
    public void GroupAppFiltersAreApplied()
    {
        var only = AppFilter.FromText(AppFilterMode.OnlyListed, "Code.exe, notepad");
        Assert.True(only.Allows("code"));
        Assert.True(only.Allows(@"C:\Windows\notepad.exe"));
        Assert.False(only.Allows("chrome.exe"));
        Assert.False(only.Allows(null));

        var except = AppFilter.FromText(AppFilterMode.AllExceptListed, "chrome.exe\nmsedge.exe");
        Assert.False(except.Allows("Chrome.EXE"));
        Assert.True(except.Allows("notepad.exe"));
        Assert.True(AppFilter.All.Allows("anything"));
    }

    [Fact]
    public void FindByKeywordPrefersExactCase()
    {
        var group = new SnippetGroup { Name = "G" };
        group.Snippets.Add(new Snippet { Keyword = "\\delta", Content = "δ", CaseMode = CaseMode.Sensitive });
        group.Snippets.Add(new Snippet { Keyword = "\\Delta", Content = "Δ", CaseMode = CaseMode.Sensitive });
        var index = KeywordIndex.Build([group]);
        Assert.Equal("Δ", index.FindByKeyword("\\Delta")!.Content);
        Assert.Equal("δ", index.FindByKeyword("\\delta")!.Content);
        Assert.Equal("δ", index.FindByKeyword("\\DELTA")!.Content);
    }
}

public class CaseAdapterTests
{
    [Theory]
    [InlineData("btw", "btw", CaseStyle.AsIs)]
    [InlineData("Btw", "btw", CaseStyle.Capitalize)]
    [InlineData("BTW", "btw", CaseStyle.Upper)]
    [InlineData(";Btw", ";btw", CaseStyle.Capitalize)]
    [InlineData(";BTW", ";btw", CaseStyle.Upper)]
    [InlineData("I", "i", CaseStyle.Capitalize)]
    [InlineData("bTW", "btw", CaseStyle.AsIs)]
    [InlineData(";;", ";;", CaseStyle.AsIs)]
    public void DetectsTypedCase(string typed, string keyword, CaseStyle expected)
    {
        Assert.Equal(expected, CaseAdapter.Detect(typed, keyword));
    }

    [Fact]
    public void AppliesStyleToTextSegmentsOnly()
    {
        var output = new ExpansionOutput([new TextSegment("(by the way)"), new KeySegment(new KeyChord(KeyModifiers.None, 0x0D)), new TextSegment("ok")]);
        Assert.Equal("(By the way)ok", CaseAdapter.Apply(output, CaseStyle.Capitalize, TestHost.Culture).Text);
        Assert.Equal("(BY THE WAY)OK", CaseAdapter.Apply(output, CaseStyle.Upper, TestHost.Culture).Text);
        Assert.Equal("Α", CaseAdapter.Apply(ExpansionOutput.FromText("α"), CaseStyle.Capitalize, TestHost.Culture).Text);
    }
}
