using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.Core.Tests;

public class TemplateEvaluatorTests
{
    [Theory]
    [InlineData("{date:yyyy-MM-dd}", "2026-10-07")]
    [InlineData("{date:iso}", "2026-10-07")]
    [InlineData("{date}", "10/7/2026")]
    [InlineData("{date:dddd, MMMM d, yyyy}", "Wednesday, October 7, 2026")]
    [InlineData("{date:yyyy-MM-dd|+1d}", "2026-10-08")]
    [InlineData("{date:yyyy-MM-dd|-1w}", "2026-09-30")]
    [InlineData("{date:yyyy-MM-dd|+1M -1d}", "2026-11-06")]
    [InlineData("{date:yyyy-MM-dd|eom}", "2026-10-31")]
    [InlineData("{date:yyyy-MM-dd|som}", "2026-10-01")]
    [InlineData("{date:yyyy-MM-dd|sow}", "2026-10-05")]
    [InlineData("{date:yyyy-MM-dd|eow}", "2026-10-11")]
    [InlineData("{date:yyyy-MM-dd|+3bd}", "2026-10-12")]
    [InlineData("{time:HH:mm}", "13:45")]
    [InlineData("{time:h:mm tt|+2h}", "3:45 PM")]
    [InlineData("{time:HH:mm|-30m}", "13:15")]
    [InlineData("{datetime:yyyy-MM-dd HH:mm:ss}", "2026-10-07 13:45:30")]
    [InlineData("Week {week}", "Week 41")]
    [InlineData("Q{quarter}", "Q4")]
    [InlineData("{date:MMMM} {ordinal:{date:%d}}", "October 7th")]
    public void FormatsDatesAndTimes(string template, string expected)
    {
        Assert.Equal(expected, TestHost.Text(template));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(102, "102nd")]
    [InlineData(111, "111th")]
    public void FormatsOrdinals(int number, string expected)
    {
        Assert.Equal(expected, TestHost.Text($"{{ordinal:{number}}}"));
    }

    [Fact]
    public void UnixAndUtcAreProduced()
    {
        var unix = TestHost.Text("{unix}");
        Assert.Equal(new DateTimeOffset(TestHost.Now).ToUnixTimeSeconds().ToString(), unix);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", TestHost.Text("{utc}"));
    }

    [Fact]
    public void InsertsClipboardUserAndSystemValues()
    {
        Assert.Equal("[clip text] jdoe Jane Doe WORKSTATION 10.0.0.5", TestHost.Text("[{clipboard}] {user} {fullname} {computer} {ip}"));
        Assert.Equal("", TestHost.Text("{clipboard}", TestHost.Create(clipboard: null)));
    }

    [Fact]
    public void ReadsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("PHRASEFLOW_TEST_VAR", "value-42");
        Assert.Equal("value-42", TestHost.Text("{env:PHRASEFLOW_TEST_VAR}"));
    }

    [Fact]
    public void CursorProducesSegmentAndIsCountedFromTheEnd()
    {
        var output = TestHost.Expand("<b>{cursor}</b>");
        Assert.True(output.HasCursor);
        Assert.Equal("<b></b>", output.Text);
        Assert.Equal("<b>▮</b>", output.ToPreviewString());
        var afterCursor = string.Concat(output.Segments.SkipWhile(s => s is not CursorSegment).OfType<TextSegment>().Select(t => t.Text));
        Assert.Equal(4, ExpansionOutput.CaretLength(afterCursor));
    }

    [Fact]
    public void FieldsUseProvidedValuesOrDefaults()
    {
        const string template = "Hi {input:Name|there}, {choice:Mood|great|fine}{checkbox:Sign|!|.} Due {pickdate:Due|MMM d}";
        Assert.Equal("Hi there, great. Due Oct 7", TestHost.Text(template));

        var values = new Dictionary<string, string>
        {
            [FormField.MakeKey(FieldKind.Text, "Name")] = "Ada",
            [FormField.MakeKey(FieldKind.Choice, "mood")] = "fine",
            [FormField.MakeKey(FieldKind.Checkbox, "Sign")] = "true",
            [FormField.MakeKey(FieldKind.Date, "Due")] = "2026-12-24",
        };
        Assert.Equal("Hi Ada, fine! Due Dec 24", TestHost.Text(template, fields: values));
    }

    [Fact]
    public void CollectsUniqueFieldsIncludingNestedSnippets()
    {
        var host = TestHost.Create(snippets: k => k == ";greet" ? new SnippetReference(k, "Hello {input:Name}!", false) : null);
        var fields = TemplateEvaluator.GetFields(
            "{input:Name} {upper:{input:Name}} {paragraph:Body} {snippet:;greet} {choice:Size|S|M|L} {input:Date|{date:yyyy}}",
            false,
            host,
            TestHost.Culture);

        Assert.Equal(["Name", "Body", "Size", "Date"], fields.Select(f => f.Label));
        Assert.Equal(FieldKind.Paragraph, fields[1].Kind);
        Assert.Equal(["S", "M", "L"], fields[2].Options);
        Assert.Equal("2026", fields[3].DefaultValue);
    }

    [Fact]
    public void PlainTextSnippetsAreNotParsed()
    {
        Assert.Empty(TemplateEvaluator.GetFields("{input:X}", plainText: true, TestHost.Create()));
        Assert.Equal("{date}", TemplateEvaluator.Expand("{date}", plainText: true, TestHost.Create()).Text);
    }

    [Fact]
    public void ExpandsNestedSnippetsAndStopsRecursion()
    {
        var host = TestHost.Create(snippets: k => k switch
        {
            ";a" => new SnippetReference(k, "A{snippet:;b}", false),
            ";b" => new SnippetReference(k, "B{cursor}", false),
            ";loop" => new SnippetReference(k, "x{snippet:;loop}", false),
            _ => null,
        });

        var nested = TestHost.Expand("[{snippet:;a}]", host);
        Assert.Equal("[AB]", nested.Text);
        Assert.True(nested.HasCursor);

        var loop = TestHost.Expand("{snippet:;loop}", host);
        Assert.NotEmpty(loop.Errors);

        var missing = TestHost.Expand("{snippet:;nope}", host);
        Assert.Equal("{snippet:;nope}", missing.Text);
        Assert.Single(missing.Errors);
    }

    [Theory]
    [InlineData("{upper:make it loud}", "MAKE IT LOUD")]
    [InlineData("{lower:QUIET}", "quiet")]
    [InlineData("{title:the quick brown fox}", "The Quick Brown Fox")]
    [InlineData("[{trim:   padded   }]", "[padded]")]
    [InlineData("{slug:Fix Login Bug #42! (Café)}", "fix-login-bug-42-cafe")]
    [InlineData("{urlencode:a b&c}", "a%20b%26c")]
    [InlineData("{replace:2026-01-31|-|/}", "2026/01/31")]
    [InlineData("{replace:a\nb|\\n|, }", "a, b")]
    [InlineData("{repeat:=|5}", "=====")]
    [InlineData("{upper:{clipboard}}", "CLIP TEXT")]
    [InlineData("{upper:a|b}", "A|B")]
    public void TransformsText(string template, string expected)
    {
        Assert.Equal(expected, TestHost.Text(template));
    }

    [Fact]
    public void GeneratesGuidsAndRandomValues()
    {
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", TestHost.Text("{guid}"));
        Assert.Matches("^[0-9A-F]{32}$", TestHost.Text("{uuid:N upper}"));
        for (var i = 0; i < 50; i++)
        {
            var value = int.Parse(TestHost.Text("{random:1|6}"));
            Assert.InRange(value, 1, 6);
            Assert.Contains(TestHost.Text("{random:red|green|blue}"), new[] { "red", "green", "blue" });
        }
    }

    [Fact]
    public void CountersIncrementOnlyWhenLive()
    {
        var host = TestHost.Create();
        Assert.Equal("#1", TestHost.Text("#{counter:ticket}", host));
        Assert.Equal("#0002", TestHost.Text("#{counter:ticket|0000}", host));
        Assert.Equal("#3", TestHost.Expand("#{counter:ticket}", host, mode: EvaluationMode.Preview).Text);
        Assert.Equal("#3", TestHost.Text("#{counter:ticket}", host));
    }

    [Theory]
    [InlineData("{calc:1+2*3}", "7")]
    [InlineData("{calc:(1+2)*3}", "9")]
    [InlineData("{calc:2^10}", "1024")]
    [InlineData("{calc:10/4}", "2.5")]
    [InlineData("{calc:19.99*3|0.00}", "59.97")]
    [InlineData("{calc:round(3.14159, 2)}", "3.14")]
    [InlineData("{calc:max(1, 7, 3) - min(4, 2)}", "5")]
    [InlineData("{calc:sqrt(16) + abs(-2)}", "6")]
    [InlineData("{calc:1000|N0}", "1,000")]
    public void Calculates(string template, string expected)
    {
        Assert.Equal(expected, TestHost.Text(template));
    }

    [Fact]
    public void ProducesKeyAndDelaySegments()
    {
        var output = TestHost.Expand("user{tab}pass{key:Ctrl+Shift+S}{key:Left|3}{delay:200}{enter}");
        Assert.Equal("userpass", output.Text);
        Assert.True(output.HasActions);
        var keys = output.Segments.OfType<KeySegment>().ToList();
        Assert.Equal(4, keys.Count);
        Assert.Equal(0x09, keys[0].Chord.VirtualKey);
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Shift, keys[1].Chord.Modifiers);
        Assert.Equal('S', keys[1].Chord.VirtualKey);
        Assert.Equal(3, keys[2].Repeat);
        Assert.Equal(200, output.Segments.OfType<DelaySegment>().Single().Milliseconds);
        Assert.Equal("user⟨Tab⟩pass⟨Ctrl+Shift+S⟩⟨Left ×3⟩⟨wait 200 ms⟩⟨Enter⟩", output.ToPreviewString());
    }

    [Fact]
    public void KeysInsideArgumentsAreIgnored()
    {
        var output = TestHost.Expand("{upper:a{enter}b}");
        Assert.Equal("AB", output.Text);
        Assert.False(output.HasActions);
    }

    [Fact]
    public void ScriptsRunOnlyWhenAllowedAndNeverInPreview()
    {
        var disabled = TestHost.Expand("{shell:echo hi}");
        Assert.Equal("{shell:echo hi}", disabled.Text);
        Assert.Single(disabled.Errors);

        var host = TestHost.Create(scripts: (cmd, kind) => $"{kind}:{cmd}\r\n");
        Assert.Equal("Cmd:echo hi", TestHost.Text("{shell:echo hi}", host));
        Assert.Equal("PowerShell:Get-Date", TestHost.Text("{powershell:Get-Date}", host));
        Assert.Equal("⟨output of: echo hi⟩", TestHost.Expand("{shell:echo hi}", host, mode: EvaluationMode.Preview).Text);
    }

    [Fact]
    public void InvalidArgumentsReportErrorsAndKeepSource()
    {
        var output = TestHost.Expand("x{date:yyyy|+1q}y");
        Assert.Equal("x{date:yyyy|+1q}y", output.Text);
        Assert.Single(output.Errors);
    }
}
