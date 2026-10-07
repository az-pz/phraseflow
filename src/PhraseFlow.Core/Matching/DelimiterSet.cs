using System.Globalization;

namespace PhraseFlow.Core.Matching;

/// <summary>The characters that complete a keyword in <see cref="Models.TriggerMode.Delimiter"/> mode.</summary>
public sealed class DelimiterSet
{
    private readonly HashSet<char> _chars;

    private DelimiterSet(HashSet<char> chars) => _chars = chars;

    public static DelimiterSet Default { get; } =
        Create(space: true, tab: true, enter: true, Models.AppSettings.DefaultTriggerCharacters);

    public IReadOnlyCollection<char> Characters => _chars;

    public static DelimiterSet Create(bool space, bool tab, bool enter, string punctuation)
    {
        var set = new HashSet<char>();
        if (space)
        {
            set.Add(' ');
            set.Add('\u00A0');
        }

        if (tab)
        {
            set.Add('\t');
        }

        if (enter)
        {
            set.Add('\n');
        }

        foreach (var c in punctuation)
        {
            if (!char.IsLetterOrDigit(c) && !char.IsControl(c) && !char.IsWhiteSpace(c))
            {
                set.Add(c);
            }
        }

        return new DelimiterSet(set);
    }

    public bool Contains(char c) => _chars.Contains(c);
}

public static class WordChars
{
    public static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' ||
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
}
