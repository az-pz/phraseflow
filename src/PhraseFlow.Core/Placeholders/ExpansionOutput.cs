using System.Globalization;
using System.Text;

namespace PhraseFlow.Core.Placeholders;

public abstract record OutputSegment;

public sealed record TextSegment(string Text) : OutputSegment;

public sealed record KeySegment(KeyChord Chord, int Repeat = 1) : OutputSegment;

public sealed record DelaySegment(int Milliseconds) : OutputSegment;

public sealed record CursorSegment : OutputSegment;

/// <summary>The evaluated result of a snippet: text plus optional key presses, delays and a caret position.</summary>
public sealed class ExpansionOutput
{
    public ExpansionOutput(IReadOnlyList<OutputSegment> segments, IReadOnlyList<string>? errors = null)
    {
        Segments = Merge(segments);
        Errors = errors ?? [];
    }

    public static ExpansionOutput FromText(string text) => new([new TextSegment(text)]);

    public IReadOnlyList<OutputSegment> Segments { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>All text without key presses or caret marker.</summary>
    public string Text => string.Concat(Segments.OfType<TextSegment>().Select(s => s.Text));

    public bool HasCursor => Segments.Any(s => s is CursorSegment);

    public bool HasActions => Segments.Any(s => s is KeySegment or DelaySegment);

    /// <summary>True when the expansion is plain text that can be reverted with backspaces.</summary>
    public bool IsSimpleText => !HasActions && !HasCursor;

    public ExpansionOutput Append(string text) => new([.. Segments, new TextSegment(text)], Errors);

    /// <summary>Applies a transformation to every text segment.</summary>
    public ExpansionOutput MapText(Func<string, string> transform) =>
        new(Segments.Select(s => s is TextSegment t ? new TextSegment(transform(t.Text)) : s).ToList(), Errors);

    /// <summary>Upper-cases the first letter of the expansion.</summary>
    public ExpansionOutput CapitalizeFirstLetter(CultureInfo culture)
    {
        var done = false;
        var segments = new List<OutputSegment>();
        foreach (var segment in Segments)
        {
            if (!done && segment is TextSegment text)
            {
                var index = -1;
                for (var i = 0; i < text.Text.Length; i++)
                {
                    if (char.IsLetter(text.Text[i]))
                    {
                        index = i;
                        break;
                    }
                }

                if (index >= 0)
                {
                    var chars = text.Text.ToCharArray();
                    chars[index] = char.ToUpper(chars[index], culture);
                    segments.Add(new TextSegment(new string(chars)));
                    done = true;
                    continue;
                }
            }

            segments.Add(segment);
        }

        return new ExpansionOutput(segments, Errors);
    }

    /// <summary>Human-readable rendering used by previews: keys as ⟨Enter⟩ and the caret as ▮.</summary>
    public string ToPreviewString()
    {
        var sb = new StringBuilder();
        foreach (var segment in Segments)
        {
            switch (segment)
            {
                case TextSegment t:
                    sb.Append(t.Text);
                    break;
                case KeySegment k:
                    sb.Append('⟨').Append(k.Chord);
                    if (k.Repeat > 1)
                    {
                        sb.Append(" ×").Append(k.Repeat);
                    }

                    sb.Append('⟩');
                    break;
                case DelaySegment d:
                    sb.Append("⟨wait ").Append(d.Milliseconds).Append(" ms⟩");
                    break;
                case CursorSegment:
                    sb.Append('▮');
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Number of caret positions in the text, as an editor counts them (CRLF and surrogate pairs count as one).
    /// </summary>
    public static int CaretLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var normalized = text.Replace("\r\n", "\n");
        return StringInfo.ParseCombiningCharacters(normalized).Length;
    }

    private static List<OutputSegment> Merge(IReadOnlyList<OutputSegment> segments)
    {
        var merged = new List<OutputSegment>(segments.Count);
        var cursorSeen = false;
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case TextSegment { Text.Length: 0 }:
                    continue;
                case TextSegment t when merged.Count > 0 && merged[^1] is TextSegment previous:
                    merged[^1] = new TextSegment(previous.Text + t.Text);
                    continue;
                case CursorSegment when cursorSeen:
                    continue;
                case CursorSegment:
                    cursorSeen = true;
                    break;
            }

            merged.Add(segment);
        }

        return merged;
    }
}
