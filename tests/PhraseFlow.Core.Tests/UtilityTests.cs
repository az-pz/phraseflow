using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.Core.Tests;

public class DateMathTests
{
    private static readonly DateTime Friday = new(2026, 10, 9, 9, 30, 0);

    [Theory]
    [InlineData("+1bd", "2026-10-12")]
    [InlineData("-1bd", "2026-10-08")]
    [InlineData("+5bd", "2026-10-16")]
    [InlineData("+1y", "2027-10-09")]
    [InlineData("+2months", "2026-12-09")]
    [InlineData("+1mo", "2026-11-09")]
    [InlineData("eoy", "2026-12-31")]
    [InlineData("soy", "2026-01-01")]
    [InlineData("+1M som", "2026-11-01")]
    [InlineData("+1M,eom", "2026-11-30")]
    [InlineData("", "2026-10-09")]
    public void ShiftsDates(string shift, string expected)
    {
        Assert.Equal(expected, DateMath.Apply(Friday, shift).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void DistinguishesMonthsAndMinutes()
    {
        Assert.Equal(new DateTime(2026, 11, 9, 9, 30, 0), DateMath.Apply(Friday, "+1M"));
        Assert.Equal(new DateTime(2026, 10, 9, 9, 31, 0), DateMath.Apply(Friday, "+1m"));
        Assert.Equal(new DateTime(2026, 10, 9, 9, 0, 0), DateMath.Apply(Friday, "-30min"));
        Assert.Equal(new DateTime(2026, 10, 9, 11, 30, 5), DateMath.Apply(Friday, "+2h+5s"));
    }

    [Theory]
    [InlineData("tomorrow")]
    [InlineData("+1")]
    [InlineData("+1x")]
    public void RejectsInvalidShifts(string shift)
    {
        Assert.Throws<FormatException>(() => DateMath.Apply(Friday, shift));
    }
}

public class CalculatorTests
{
    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("-(2 + 3) * 2", -10)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("7 % 4", 3)]
    [InlineData("1.5e2 / 3", 50)]
    [InlineData("floor(2.7) + ceil(2.1)", 5)]
    [InlineData("pow(2, 8)", 256)]
    [InlineData("round(2.5)", 3)]
    [InlineData("pi > 3", double.NaN)]
    public void Evaluates(string expression, double expected)
    {
        if (double.IsNaN(expected))
        {
            Assert.Throws<FormatException>(() => Calculator.Evaluate(expression));
            return;
        }

        Assert.Equal(expected, Calculator.Evaluate(expression), 9);
    }

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("(1 + 2")]
    [InlineData("foo(1)")]
    [InlineData("")]
    public void RejectsInvalidExpressions(string expression)
    {
        Assert.Throws<FormatException>(() => Calculator.Evaluate(expression));
    }
}

public class KeyChordTests
{
    [Theory]
    [InlineData("Enter", KeyModifiers.None, 0x0D)]
    [InlineData("tab", KeyModifiers.None, 0x09)]
    [InlineData("Ctrl+S", KeyModifiers.Ctrl, 'S')]
    [InlineData("ctrl+shift+esc", KeyModifiers.Ctrl | KeyModifiers.Shift, 0x1B)]
    [InlineData("Alt+F4", KeyModifiers.Alt, 0x73)]
    [InlineData("Win+D", KeyModifiers.Win, 'D')]
    [InlineData("Ctrl+Alt+Space", KeyModifiers.Ctrl | KeyModifiers.Alt, 0x20)]
    [InlineData("PgDn", KeyModifiers.None, 0x22)]
    [InlineData("F24", KeyModifiers.None, 0x87)]
    public void ParsesNamedKeys(string text, KeyModifiers modifiers, int vk)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(modifiers, chord.Modifiers);
        Assert.Equal(vk, chord.VirtualKey);
    }

    [Fact]
    public void ParsesCharactersAndPlus()
    {
        Assert.True(KeyChord.TryParse("Ctrl++", out var plus));
        Assert.Equal('+', plus.Character);
        Assert.True(KeyChord.TryParse("/", out var slash));
        Assert.Equal('/', slash.Character);
        Assert.Equal("Ctrl+Shift+S", new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, 'S').ToString());
        Assert.Equal("Alt+F4", new KeyChord(KeyModifiers.Alt, 0x73).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hyper+X")]
    [InlineData("NotAKey")]
    public void RejectsUnknownKeys(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _));
    }
}
