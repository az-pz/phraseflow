using System.Text;

namespace PhraseFlow.Core.Placeholders;

public abstract record TemplateNode;

public sealed record TextNode(string Text) : TemplateNode;

/// <param name="Name">Canonical placeholder name.</param>
/// <param name="Args">Arguments separated by <c>|</c>; each argument may contain nested placeholders.</param>
/// <param name="Source">The original text of the placeholder, e.g. <c>{date:yyyy}</c>.</param>
public sealed record PlaceholderNode(string Name, IReadOnlyList<IReadOnlyList<TemplateNode>> Args, string Source) : TemplateNode;

/// <summary>
/// Parses snippet content into text and placeholders.
/// Syntax: <c>{name}</c> or <c>{name:arg1|arg2}</c>. Only known placeholder names are recognised, so ordinary
/// braces in code stay literal. <c>\{name}</c> produces a literal placeholder. Inside arguments <c>\|</c>, <c>\{</c>,
/// <c>\}</c> and <c>\\</c> are escapes.
/// </summary>
public static class TemplateParser
{
    public static IReadOnlyList<TemplateNode> Parse(string? text)
    {
        var nodes = new List<TemplateNode>();
        if (!string.IsNullOrEmpty(text))
        {
            ParseSequence(text, 0, text.Length, inArgument: false, nodes);
        }

        return nodes;
    }

    /// <summary>Returns true when the text contains at least one recognised placeholder.</summary>
    public static bool ContainsPlaceholders(string? text) => Parse(text).Any(n => n is PlaceholderNode);

    private static void ParseSequence(string s, int start, int end, bool inArgument, List<TemplateNode> output)
    {
        var literal = new StringBuilder();
        var i = start;
        while (i < end)
        {
            var c = s[i];
            if (inArgument && c == '\\' && i + 1 < end && IsArgumentEscape(s[i + 1]))
            {
                literal.Append(s[i + 1]);
                i += 2;
                continue;
            }

            if (c == '{' && TryParsePlaceholder(s, i, end, out var node, out var next))
            {
                var escaped = !inArgument && literal.Length > 0 && literal[^1] == '\\';
                var templateLiteral = node.Args.Count == 0 && i > start && s[i - 1] == '$';
                if (escaped || templateLiteral)
                {
                    if (escaped)
                    {
                        literal.Length--;
                    }

                    literal.Append(s, i, next - i);
                }
                else
                {
                    Flush(literal, output);
                    output.Add(node);
                }

                i = next;
                continue;
            }

            literal.Append(c);
            i++;
        }

        Flush(literal, output);
    }

    private static bool TryParsePlaceholder(string s, int start, int end, out PlaceholderNode node, out int next)
    {
        node = null!;
        next = start;
        var i = start + 1;
        var nameStart = i;
        while (i < end && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '_'))
        {
            i++;
        }

        if (i == nameStart || i >= end || !char.IsAsciiLetter(s[nameStart]))
        {
            return false;
        }

        if (!PlaceholderCatalog.TryResolve(s[nameStart..i], out var info))
        {
            return false;
        }

        if (s[i] == '}')
        {
            if (info.MinArgs > 0)
            {
                return false;
            }

            node = new PlaceholderNode(info.Name, [], s[start..(i + 1)]);
            next = i + 1;
            return true;
        }

        if (s[i] != ':')
        {
            return false;
        }

        var argStart = i + 1;
        var separators = new List<int>();
        var depth = 0;
        var close = -1;
        for (var j = argStart; j < end; j++)
        {
            var c = s[j];
            if (c == '\\' && j + 1 < end && IsArgumentEscape(s[j + 1]))
            {
                j++;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                if (depth == 0)
                {
                    close = j;
                    break;
                }

                depth--;
            }
            else if (c == '|' && depth == 0)
            {
                separators.Add(j);
            }
        }

        if (close < 0)
        {
            return false;
        }

        separators.Add(close);
        var args = new List<IReadOnlyList<TemplateNode>>(separators.Count);
        var segmentStart = argStart;
        foreach (var separator in separators)
        {
            var argNodes = new List<TemplateNode>();
            ParseSequence(s, segmentStart, separator, inArgument: true, argNodes);
            args.Add(argNodes);
            segmentStart = separator + 1;
        }

        if (args.Count < info.MinArgs)
        {
            return false;
        }

        node = new PlaceholderNode(info.Name, args, s[start..(close + 1)]);
        next = close + 1;
        return true;
    }

    private static bool IsArgumentEscape(char c) => c is '|' or '{' or '}' or '\\';

    private static void Flush(StringBuilder literal, List<TemplateNode> output)
    {
        if (literal.Length == 0)
        {
            return;
        }

        output.Add(new TextNode(literal.ToString()));
        literal.Clear();
    }
}
