using System.Globalization;
using System.Text;

namespace PhraseFlow.Core.Placeholders;

public enum EvaluationMode
{
    /// <summary>Real expansion: counters increase and scripts run.</summary>
    Live,

    /// <summary>Editor preview: no side effects.</summary>
    Preview,
}

/// <summary>Turns parsed snippet content into an <see cref="ExpansionOutput"/>.</summary>
public sealed class TemplateEvaluator
{
    private const int MaxSnippetDepth = 8;
    private const int VkReturn = 0x0D;
    private const int VkTab = 0x09;

    private readonly IExpansionHost _host;
    private readonly IReadOnlyDictionary<string, string> _fieldValues;
    private readonly EvaluationMode _mode;
    private readonly CultureInfo _culture;
    private readonly Random _random;
    private readonly List<string> _errors = [];
    private readonly Stack<string> _snippetStack = new();

    public TemplateEvaluator(
        IExpansionHost host,
        IReadOnlyDictionary<string, string>? fieldValues = null,
        EvaluationMode mode = EvaluationMode.Live,
        CultureInfo? culture = null,
        Random? random = null)
    {
        _host = host;
        _fieldValues = fieldValues ?? new Dictionary<string, string>();
        _mode = mode;
        _culture = culture ?? CultureInfo.CurrentCulture;
        _random = random ?? Random.Shared;
    }

    /// <summary>Convenience: parse and evaluate snippet content.</summary>
    public static ExpansionOutput Expand(
        string content,
        bool plainText,
        IExpansionHost host,
        IReadOnlyDictionary<string, string>? fieldValues = null,
        EvaluationMode mode = EvaluationMode.Live,
        CultureInfo? culture = null)
    {
        if (plainText)
        {
            return ExpansionOutput.FromText(content);
        }

        return new TemplateEvaluator(host, fieldValues, mode, culture).Evaluate(TemplateParser.Parse(content));
    }

    /// <summary>Convenience: list the fill-in fields used by snippet content (including nested snippets).</summary>
    public static IReadOnlyList<FormField> GetFields(string content, bool plainText, IExpansionHost host, CultureInfo? culture = null)
    {
        if (plainText)
        {
            return [];
        }

        return new TemplateEvaluator(host, mode: EvaluationMode.Preview, culture: culture).CollectFields(TemplateParser.Parse(content));
    }

    public ExpansionOutput Evaluate(IReadOnlyList<TemplateNode> nodes)
    {
        var segments = new List<OutputSegment>();
        Emit(nodes, segments, allowActions: true);
        return new ExpansionOutput(segments, [.. _errors]);
    }

    public IReadOnlyList<FormField> CollectFields(IReadOnlyList<TemplateNode> nodes)
    {
        var fields = new List<FormField>();
        var seen = new HashSet<string>();
        CollectFields(nodes, fields, seen, depth: 0);
        return fields;
    }

    private void CollectFields(IReadOnlyList<TemplateNode> nodes, List<FormField> fields, HashSet<string> seen, int depth)
    {
        foreach (var node in nodes.OfType<PlaceholderNode>())
        {
            var field = TryCreateField(node);
            if (field is not null && seen.Add(field.Key))
            {
                fields.Add(field);
            }

            foreach (var arg in node.Args)
            {
                CollectFields(arg, fields, seen, depth);
            }

            if (node.Name == Ph.Snippet && depth < MaxSnippetDepth)
            {
                var keyword = SafeText(node.Args[0]).Trim();
                if (_snippetStack.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var reference = _host.FindSnippet(keyword);
                if (reference is { PlainText: false })
                {
                    _snippetStack.Push(keyword);
                    CollectFields(TemplateParser.Parse(reference.Content), fields, seen, depth + 1);
                    _snippetStack.Pop();
                }
            }
        }
    }

    private FormField? TryCreateField(PlaceholderNode node)
    {
        if (!PlaceholderCatalog.IsFieldPlaceholder(node.Name))
        {
            return null;
        }

        var label = SafeText(node.Args[0]).Trim();
        if (label.Length == 0)
        {
            label = "Value";
        }

        switch (node.Name)
        {
            case Ph.Input:
                return new FormField(FormField.MakeKey(FieldKind.Text, label), FieldKind.Text, label, ArgText(node, 1), []);
            case Ph.Paragraph:
                return new FormField(FormField.MakeKey(FieldKind.Paragraph, label), FieldKind.Paragraph, label, ArgText(node, 1), []);
            case Ph.Choice:
            {
                var options = node.Args.Skip(1).Select(SafeText).ToList();
                return new FormField(FormField.MakeKey(FieldKind.Choice, label), FieldKind.Choice, label, options.FirstOrDefault() ?? "", options);
            }

            case Ph.Checkbox:
                return new FormField(FormField.MakeKey(FieldKind.Checkbox, label), FieldKind.Checkbox, label, "false", []);
            case Ph.PickDate:
                return new FormField(
                    FormField.MakeKey(FieldKind.Date, label),
                    FieldKind.Date,
                    label,
                    _host.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    []);
            default:
                return null;
        }
    }

    private string SafeText(IReadOnlyList<TemplateNode> nodes)
    {
        var segments = new List<OutputSegment>();
        Emit(nodes, segments, allowActions: false);
        return string.Concat(segments.OfType<TextSegment>().Select(s => s.Text));
    }

    private string ArgText(PlaceholderNode node, int index, string fallback = "") =>
        index < node.Args.Count ? SafeText(node.Args[index]) : fallback;

    private string JoinedArgs(PlaceholderNode node) => string.Join("|", node.Args.Select(SafeText));

    private void Emit(IReadOnlyList<TemplateNode> nodes, List<OutputSegment> output, bool allowActions)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    output.Add(new TextSegment(text.Text));
                    break;
                case PlaceholderNode placeholder:
                    try
                    {
                        EmitPlaceholder(placeholder, output, allowActions);
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException
                                                   or OverflowException or TimeoutException or System.ComponentModel.Win32Exception)
                    {
                        _errors.Add($"{placeholder.Source}: {ex.Message}");
                        output.Add(new TextSegment(placeholder.Source));
                    }

                    break;
            }
        }
    }

    private void EmitPlaceholder(PlaceholderNode node, List<OutputSegment> output, bool allowActions)
    {
        switch (node.Name)
        {
            case Ph.Date:
                output.Add(new TextSegment(FormatDate(node, "d", "yyyy-MM-dd", utc: false)));
                break;
            case Ph.Time:
                output.Add(new TextSegment(FormatDate(node, "t", "HH:mm:ss", utc: false)));
                break;
            case Ph.DateTime:
                output.Add(new TextSegment(FormatDate(node, "g", "yyyy-MM-dd'T'HH:mm:ss", utc: false)));
                break;
            case Ph.Utc:
                output.Add(new TextSegment(FormatDate(node, "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'", utc: true)));
                break;
            case Ph.Week:
                output.Add(new TextSegment(ISOWeek.GetWeekOfYear(DateMath.Apply(_host.Now, ArgText(node, 0))).ToString(_culture)));
                break;
            case Ph.Quarter:
                output.Add(new TextSegment(((DateMath.Apply(_host.Now, ArgText(node, 0)).Month - 1) / 3 + 1).ToString(_culture)));
                break;
            case Ph.Unix:
                output.Add(new TextSegment(new DateTimeOffset(_host.Now).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
                break;
            case Ph.Ordinal:
                output.Add(new TextSegment(ToOrdinal(ArgText(node, 0))));
                break;

            case Ph.Clipboard:
                output.Add(new TextSegment(_host.GetClipboardText() ?? ""));
                break;
            case Ph.Cursor:
                if (allowActions)
                {
                    output.Add(new CursorSegment());
                }

                break;

            case Ph.Input:
            case Ph.Paragraph:
            case Ph.Choice:
            case Ph.Checkbox:
            case Ph.PickDate:
                output.Add(new TextSegment(EvaluateField(node)));
                break;

            case Ph.Snippet:
                EmitNestedSnippet(node, output, allowActions);
                break;
            case Ph.Upper:
                output.Add(new TextSegment(JoinedArgs(node).ToUpper(_culture)));
                break;
            case Ph.Lower:
                output.Add(new TextSegment(JoinedArgs(node).ToLower(_culture)));
                break;
            case Ph.Title:
                output.Add(new TextSegment(_culture.TextInfo.ToTitleCase(JoinedArgs(node))));
                break;
            case Ph.Trim:
                output.Add(new TextSegment(JoinedArgs(node).Trim()));
                break;
            case Ph.Slug:
                output.Add(new TextSegment(Slugify(JoinedArgs(node))));
                break;
            case Ph.UrlEncode:
                output.Add(new TextSegment(Uri.EscapeDataString(JoinedArgs(node))));
                break;
            case Ph.Replace:
                output.Add(new TextSegment(ReplaceText(ArgText(node, 0), Unescape(ArgText(node, 1)), Unescape(ArgText(node, 2)))));
                break;
            case Ph.Repeat:
            {
                var count = ParseInt(ArgText(node, 1), 1, 0, 1000);
                output.Add(new TextSegment(string.Concat(Enumerable.Repeat(ArgText(node, 0), count))));
                break;
            }

            case Ph.User:
                output.Add(new TextSegment(_host.UserName));
                break;
            case Ph.FullName:
                output.Add(new TextSegment(_host.FullName));
                break;
            case Ph.Computer:
                output.Add(new TextSegment(_host.MachineName));
                break;
            case Ph.Env:
                output.Add(new TextSegment(_host.GetEnvironmentVariable(ArgText(node, 0).Trim()) ?? ""));
                break;
            case Ph.Ip:
                output.Add(new TextSegment(_host.GetLocalIpAddress() ?? ""));
                break;

            case Ph.Guid:
                output.Add(new TextSegment(FormatGuid(ArgText(node, 0))));
                break;
            case Ph.Random:
                output.Add(new TextSegment(RandomValue(node)));
                break;
            case Ph.Counter:
            {
                var value = _host.GetCounter(ArgText(node, 0).Trim(), increment: _mode == EvaluationMode.Live);
                var format = ArgText(node, 1).Trim();
                output.Add(new TextSegment(format.Length > 0 ? value.ToString(format, _culture) : value.ToString(_culture)));
                break;
            }

            case Ph.Calc:
            {
                var result = Calculator.Evaluate(ArgText(node, 0));
                output.Add(new TextSegment(Calculator.Format(result, ArgText(node, 1), _culture)));
                break;
            }

            case Ph.Key:
            {
                var keyText = ArgText(node, 0);
                if (!KeyChord.TryParse(keyText, out var chord))
                {
                    throw new FormatException($"Unknown key '{keyText}'.");
                }

                if (allowActions)
                {
                    output.Add(new KeySegment(chord, ParseInt(ArgText(node, 1), 1, 1, 100)));
                }

                break;
            }

            case Ph.Enter:
                if (allowActions)
                {
                    output.Add(new KeySegment(new KeyChord(KeyModifiers.None, VkReturn)));
                }

                break;
            case Ph.Tab:
                if (allowActions)
                {
                    output.Add(new KeySegment(new KeyChord(KeyModifiers.None, VkTab)));
                }

                break;
            case Ph.Delay:
                if (allowActions)
                {
                    output.Add(new DelaySegment(ParseInt(ArgText(node, 0), 0, 0, 10_000)));
                }

                break;

            case Ph.Shell:
            case Ph.PowerShell:
            {
                var command = JoinedArgs(node);
                var kind = node.Name == Ph.Shell ? ScriptKind.Cmd : ScriptKind.PowerShell;
                output.Add(new TextSegment(_mode == EvaluationMode.Preview
                    ? $"⟨output of: {command}⟩"
                    : _host.RunScript(command, kind).TrimEnd('\r', '\n')));
                break;
            }

            default:
                throw new FormatException($"Unsupported placeholder '{node.Name}'.");
        }
    }

    private string FormatDate(PlaceholderNode node, string defaultFormat, string isoFormat, bool utc)
    {
        var format = ArgText(node, 0).Trim();
        var now = utc ? _host.Now.ToUniversalTime() : _host.Now;
        var value = DateMath.Apply(now, ArgText(node, 1));
        if (format.Length == 0)
        {
            format = defaultFormat;
        }
        else if (format.Equals("iso", StringComparison.OrdinalIgnoreCase))
        {
            format = isoFormat;
        }

        return value.ToString(format, _culture);
    }

    private string EvaluateField(PlaceholderNode node)
    {
        var label = ArgText(node, 0).Trim();
        if (label.Length == 0)
        {
            label = "Value";
        }

        switch (node.Name)
        {
            case Ph.Input:
                return _fieldValues.GetValueOrDefault(FormField.MakeKey(FieldKind.Text, label)) ?? ArgText(node, 1);
            case Ph.Paragraph:
                return _fieldValues.GetValueOrDefault(FormField.MakeKey(FieldKind.Paragraph, label)) ?? ArgText(node, 1);
            case Ph.Choice:
                return _fieldValues.GetValueOrDefault(FormField.MakeKey(FieldKind.Choice, label)) ?? ArgText(node, 1);
            case Ph.Checkbox:
            {
                var value = _fieldValues.GetValueOrDefault(FormField.MakeKey(FieldKind.Checkbox, label));
                var isChecked = bool.TryParse(value, out var parsed) && parsed;
                return isChecked ? ArgText(node, 1) : ArgText(node, 2);
            }

            case Ph.PickDate:
            {
                var value = _fieldValues.GetValueOrDefault(FormField.MakeKey(FieldKind.Date, label));
                var date = DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : _host.Now.Date;
                var format = ArgText(node, 1).Trim();
                return date.ToString(format.Length > 0 ? format : "d", _culture);
            }

            default:
                return "";
        }
    }

    private void EmitNestedSnippet(PlaceholderNode node, List<OutputSegment> output, bool allowActions)
    {
        var keyword = ArgText(node, 0).Trim();
        if (_snippetStack.Count >= MaxSnippetDepth || _snippetStack.Contains(keyword, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Snippet '{keyword}' includes itself.");
        }

        var reference = _host.FindSnippet(keyword) ?? throw new ArgumentException($"No snippet with keyword '{keyword}'.");
        if (reference.PlainText)
        {
            output.Add(new TextSegment(reference.Content));
            return;
        }

        _snippetStack.Push(keyword);
        try
        {
            Emit(TemplateParser.Parse(reference.Content), output, allowActions);
        }
        finally
        {
            _snippetStack.Pop();
        }
    }

    private string RandomValue(PlaceholderNode node)
    {
        var args = node.Args.Select(SafeText).ToList();
        if (args.Count == 2 &&
            long.TryParse(args[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var min) &&
            long.TryParse(args[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
        {
            if (min > max)
            {
                (min, max) = (max, min);
            }

            return _random.NextInt64(min, max + 1).ToString(_culture);
        }

        if (args.Count == 1 && long.TryParse(args[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var upper) && upper >= 1)
        {
            return _random.NextInt64(1, upper + 1).ToString(_culture);
        }

        return args[_random.Next(args.Count)];
    }

    private static string FormatGuid(string options)
    {
        var lower = options.Trim().ToLowerInvariant();
        var upper = lower.Contains("upper", StringComparison.Ordinal);
        var format = lower.Replace("upper", "", StringComparison.Ordinal).Trim();
        if (format is not ("n" or "d" or "b" or "p" or "x"))
        {
            format = "d";
        }

        var text = System.Guid.NewGuid().ToString(format, CultureInfo.InvariantCulture);
        return upper ? text.ToUpperInvariant() : text;
    }

    private static string ToOrdinal(string text)
    {
        if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            throw new FormatException($"'{text}' is not a whole number.");
        }

        var lastTwo = Math.Abs(n % 100);
        var suffix = lastTwo is >= 11 and <= 13
            ? "th"
            : (Math.Abs(n % 10)) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };
        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string Slugify(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var pendingDash = false;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (pendingDash && sb.Length > 0)
                {
                    sb.Append('-');
                }

                pendingDash = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingDash = true;
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Unescape(string text) =>
        text.Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal);

    private static string ReplaceText(string text, string find, string replacement)
    {
        if (find.Length == 0)
        {
            return text;
        }

        if (find.Contains('\n') && !find.Contains('\r'))
        {
            text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        return text.Replace(find, replacement, StringComparison.Ordinal);
    }

    private static int ParseInt(string text, int fallback, int min, int max) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
}
