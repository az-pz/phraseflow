using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.Core.Tests;

public class TemplateParserTests
{
    [Fact]
    public void PlainTextIsSingleTextNode()
    {
        var nodes = TemplateParser.Parse("hello world");
        Assert.Equal([new TextNode("hello world")], nodes);
    }

    [Fact]
    public void ParsesPlaceholderWithAndWithoutArguments()
    {
        var nodes = TemplateParser.Parse("A {date} B {date:yyyy-MM-dd|+1d}");
        Assert.Equal(4, nodes.Count);
        var bare = Assert.IsType<PlaceholderNode>(nodes[1]);
        Assert.Equal(Ph.Date, bare.Name);
        Assert.Empty(bare.Args);
        var withArgs = Assert.IsType<PlaceholderNode>(nodes[3]);
        Assert.Equal(2, withArgs.Args.Count);
        Assert.Equal("{date:yyyy-MM-dd|+1d}", withArgs.Source);
    }

    [Theory]
    [InlineData("if (x) { return; }")]
    [InlineData("{foo}")]
    [InlineData("\\d{1,3}")]
    [InlineData("{\"key\": 1}")]
    [InlineData("<h1>{title}</h1>")]
    [InlineData("const {key} = obj;")]
    [InlineData("`Hello ${user}`")]
    [InlineData("{date:yyyy")]
    [InlineData("{ date }")]
    public void LeavesNonPlaceholderBracesAlone(string text)
    {
        Assert.Equal(text, TestHost.Text(text));
        Assert.False(TemplateParser.ContainsPlaceholders(text));
    }

    [Fact]
    public void BackslashEscapesAKnownPlaceholder()
    {
        Assert.Equal("Use {date} for dates", TestHost.Text("Use \\{date} for dates"));
    }

    [Fact]
    public void NamesAreCaseInsensitiveAndAliasesResolve()
    {
        var nodes = TemplateParser.Parse("{DATE}{uuid}{Now}");
        Assert.Equal([Ph.Date, Ph.Guid, Ph.DateTime], nodes.OfType<PlaceholderNode>().Select(n => n.Name));
    }

    [Fact]
    public void ParsesNestedPlaceholdersInArguments()
    {
        var node = Assert.IsType<PlaceholderNode>(Assert.Single(TemplateParser.Parse("{upper:{input:Name}}")));
        var inner = Assert.IsType<PlaceholderNode>(Assert.Single(node.Args[0]));
        Assert.Equal(Ph.Input, inner.Name);
    }

    [Fact]
    public void SupportsEscapesInsideArguments()
    {
        var host = TestHost.Create();
        var fields = TemplateEvaluator.GetFields("{choice:Pick|a\\|b|c\\}d}", false, host, TestHost.Culture);
        var field = Assert.Single(fields);
        Assert.Equal(["a|b", "c}d"], field.Options);
    }

    [Fact]
    public void ColonsInArgumentsBelongToTheArgument()
    {
        Assert.Equal("13:45", TestHost.Text("{time:HH:mm}"));
    }
}
