using System.Globalization;
using PhraseFlow.Core.Models;

namespace PhraseFlow.Core.Matching;

/// <param name="Entry">The matched snippet.</param>
/// <param name="TypedKeyword">The keyword exactly as the user typed it (used for case propagation).</param>
/// <param name="TriggerChar">The character whose key press completed the match (it was not delivered to the app).</param>
/// <param name="IsDelimiterTrigger">True when <paramref name="TriggerChar"/> is a delimiter following the keyword.</param>
/// <param name="CharactersToDelete">How many characters already reached the application and must be erased.</param>
public sealed record TypingMatch(
    SnippetEntry Entry,
    string TypedKeyword,
    char TriggerChar,
    bool IsDelimiterTrigger,
    int CharactersToDelete)
{
    /// <summary>The text the user typed, including the trigger character (used to undo an expansion).</summary>
    public string OriginalText => IsDelimiterTrigger ? TypedKeyword + TriggerChar : TypedKeyword;
}

/// <summary>
/// Keeps a rolling buffer of recently typed characters and reports when a keyword was completed.
/// Not thread-safe: owned by the keyboard hook thread.
/// </summary>
public sealed class TypingEngine
{
    public const int Capacity = 256;

    private readonly char[] _buffer = new char[Capacity];
    private int _length;

    public KeywordIndex Index { get; set; } = KeywordIndex.Empty;

    public DelimiterSet Delimiters { get; set; } = DelimiterSet.Default;

    public string BufferText => new(_buffer, 0, _length);

    /// <summary>Feeds a typed character. Returns a match if the character completes a keyword.</summary>
    public TypingMatch? Type(char c, Func<SnippetEntry, bool>? filter = null)
    {
        if (c == '\r')
        {
            c = '\n';
        }

        Append(c);
        var text = new ReadOnlySpan<char>(_buffer, 0, _length);

        var immediate = Index.FindSuffixMatch(text, TriggerMode.Immediate, filter);
        if (immediate is { } im)
        {
            var typed = text[^im.Length..].ToString();
            return im.Entry.WouldBeNoOp(typed)
                ? null
                : new TypingMatch(im.Entry, typed, c, IsDelimiterTrigger: false, TextLength(typed) - 1);
        }

        if (Delimiters.Contains(c) && text.Length > 1)
        {
            var beforeTrigger = text[..^1];
            var delimited = Index.FindSuffixMatch(beforeTrigger, TriggerMode.Delimiter, filter);
            if (delimited is { } dm)
            {
                var typed = beforeTrigger[^dm.Length..].ToString();
                return dm.Entry.WouldBeNoOp(typed)
                    ? null
                    : new TypingMatch(dm.Entry, typed, c, IsDelimiterTrigger: true, TextLength(typed));
            }
        }

        return null;
    }

    /// <summary>Adds text to the buffer without checking for matches.</summary>
    public void AppendWithoutMatching(string text)
    {
        foreach (var c in text)
        {
            Append(c);
        }
    }

    public void Backspace()
    {
        if (_length == 0)
        {
            return;
        }

        _length--;
        if (_length > 0 && char.IsLowSurrogate(_buffer[_length]) && char.IsHighSurrogate(_buffer[_length - 1]))
        {
            _length--;
        }
    }

    public void Reset() => _length = 0;

    /// <summary>Replaces the buffer, e.g. with the delimiter typed after an expansion.</summary>
    public void SetContext(string text)
    {
        Reset();
        AppendWithoutMatching(text);
    }

    private void Append(char c)
    {
        if (_length == Capacity)
        {
            const int keep = Capacity / 2;
            Array.Copy(_buffer, Capacity - keep, _buffer, 0, keep);
            _length = keep;
        }

        _buffer[_length++] = c == '\r' ? '\n' : c;
    }

    private static int TextLength(string text) => new StringInfo(text).LengthInTextElements;
}
