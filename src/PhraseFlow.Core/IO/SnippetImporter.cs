using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Storage;
using YamlDotNet.Serialization;

namespace PhraseFlow.Core.IO;

public sealed record ImportResult(string Format, IReadOnlyList<SnippetGroup> Groups, IReadOnlyList<string> Warnings)
{
    public int SnippetCount => Groups.Sum(g => g.Snippets.Count);
}

/// <summary>
/// Imports snippets from PhraseFlow JSON, CSV/TSV (TextExpander and generic), AutoHotkey hotstrings,
/// espanso YAML match files and Beeftext JSON.
/// </summary>
public static partial class SnippetImporter
{
    public static string FileDialogFilter =>
        "All supported|*.json;*.csv;*.tsv;*.txt;*.ahk;*.yml;*.yaml|" +
        "PhraseFlow or Beeftext JSON|*.json|CSV / TSV (TextExpander, spreadsheets)|*.csv;*.tsv;*.txt|" +
        "AutoHotkey hotstrings|*.ahk|espanso match files|*.yml;*.yaml|All files|*.*";

    public static ImportResult ImportFile(string path)
    {
        var text = File.ReadAllText(path);
        var name = Path.GetFileNameWithoutExtension(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => ImportJson(text, name),
            ".ahk" => ImportAutoHotkey(text, name),
            ".yml" or ".yaml" => ImportEspanso(text, name),
            _ => ImportCsv(text, name),
        };
    }

    // ---------------------------------------------------------------- JSON (PhraseFlow / Beeftext)

    public static ImportResult ImportJson(string json, string fallbackGroupName)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("combos", out _))
        {
            return ImportBeeftext(root, fallbackGroupName);
        }

        var library = LibraryStore.Deserialize(json);
        foreach (var snippet in library.AllSnippets)
        {
            snippet.Id = Guid.NewGuid();
        }

        foreach (var group in library.Groups)
        {
            group.Id = Guid.NewGuid();
        }

        return new ImportResult("PhraseFlow", library.Groups.ToList(), []);
    }

    private static ImportResult ImportBeeftext(JsonElement root, string fallbackGroupName)
    {
        var warnings = new List<string>();
        var groups = new Dictionary<string, SnippetGroup>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<SnippetGroup>();
        if (root.TryGetProperty("groups", out var groupArray) && groupArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in groupArray.EnumerateArray())
            {
                var group = new SnippetGroup
                {
                    Name = GetString(g, "name") ?? "Beeftext",
                    Description = GetString(g, "description") ?? "",
                    Enabled = GetBool(g, "enabled") ?? true,
                };
                groups[GetString(g, "uuid") ?? Guid.NewGuid().ToString()] = group;
                ordered.Add(group);
            }
        }

        SnippetGroup? fallback = null;
        foreach (var combo in root.GetProperty("combos").EnumerateArray())
        {
            var keyword = GetString(combo, "keyword") ?? "";
            var content = GetString(combo, "snippet") ?? GetString(combo, "substitutionText") ?? GetString(combo, "text") ?? "";
            var snippet = new Snippet
            {
                Keyword = keyword,
                Name = GetString(combo, "name") ?? "",
                Content = ConvertBeeftextVariables(content),
                Enabled = GetBool(combo, "enabled") ?? true,
                CaseMode = GetBool(combo, "caseSensitive") == true ? CaseMode.Sensitive : CaseMode.Insensitive,
                Trigger = TriggerMode.Immediate,
                ExpandInsideWords = GetString(combo, "matchingMode") is "loose" or "1",
            };

            var groupId = GetString(combo, "group");
            if (groupId is null || !groups.TryGetValue(groupId, out var target))
            {
                fallback ??= new SnippetGroup { Name = fallbackGroupName };
                target = fallback;
            }

            target.Snippets.Add(snippet);
        }

        if (fallback is not null)
        {
            ordered.Add(fallback);
        }

        return new ImportResult("Beeftext", ordered.Where(g => g.Snippets.Count > 0).ToList(), warnings);
    }

    [GeneratedRegex(@"#\{(?<name>[A-Za-z]+)(?::(?<arg>[^}]*))?\}")]
    private static partial Regex BeeftextVariable();

    private static string ConvertBeeftextVariables(string text) => BeeftextVariable().Replace(text, m =>
    {
        var arg = m.Groups["arg"].Success ? m.Groups["arg"].Value : null;
        return m.Groups["name"].Value.ToLowerInvariant() switch
        {
            "clipboard" => "{clipboard}",
            "cursor" => "{cursor}",
            "date" => "{date}",
            "time" => "{time}",
            "datetime" => arg is null ? "{datetime}" : "{datetime:" + ConvertQtDateFormat(arg) + "}",
            "input" => "{input:" + (arg ?? "Value") + "}",
            "combo" => "{snippet:" + arg + "}",
            "upper" => "{upper:{snippet:" + arg + "}}",
            "lower" => "{lower:{snippet:" + arg + "}}",
            "trim" => "{trim:{snippet:" + arg + "}}",
            "envvar" => "{env:" + arg + "}",
            "key" or "shortcut" => "{key:" + arg + "}",
            "delay" => "{delay:" + arg + "}",
            "powershell" => "{powershell:& '" + arg + "'}",
            _ => m.Value,
        };
    });

    private static string ConvertQtDateFormat(string format)
    {
        var hasAmPm = format.Contains("AP", StringComparison.OrdinalIgnoreCase);
        var result = format.Replace("AP", "tt").Replace("ap", "tt").Replace("zzz", "fff");
        if (!hasAmPm)
        {
            result = result.Replace("hh", "HH").Replace("h", "H").Replace("HHH", "HH");
        }

        return result;
    }

    // ---------------------------------------------------------------- CSV / TSV

    private static readonly string[] KeywordHeaders = ["keyword", "abbreviation", "abbrev", "shortcut", "trigger", "shortcode", "short"];
    private static readonly string[] ContentHeaders = ["content", "expansion", "phrase", "replacement", "replace", "text", "snippet", "plain text", "plaintext", "value", "long"];
    private static readonly string[] NameHeaders = ["name", "label", "description", "title"];
    private static readonly string[] GroupHeaders = ["group", "folder", "category"];

    public static ImportResult ImportCsv(string text, string groupName)
    {
        var separator = Csv.DetectSeparator(text);
        var rows = Csv.Parse(text, separator).Where(r => r.Any(c => c.Length > 0)).ToList();
        var warnings = new List<string>();
        if (rows.Count == 0)
        {
            return new ImportResult("CSV", [], ["The file is empty."]);
        }

        int keywordCol = 0, contentCol = 1, nameCol = 2, groupCol = -1, triggerCol = -1, caseCol = -1, enabledCol = -1;
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        if (header.Any(h => KeywordHeaders.Contains(h) || ContentHeaders.Contains(h)))
        {
            keywordCol = header.FindIndex(KeywordHeaders.Contains);
            contentCol = header.FindIndex(ContentHeaders.Contains);
            nameCol = header.FindIndex(NameHeaders.Contains);
            groupCol = header.FindIndex(GroupHeaders.Contains);
            triggerCol = header.IndexOf("trigger") >= 0 && keywordCol != header.IndexOf("trigger") ? header.IndexOf("trigger") : -1;
            caseCol = header.IndexOf("case");
            enabledCol = header.IndexOf("enabled");
            rows.RemoveAt(0);
            if (keywordCol < 0 || contentCol < 0)
            {
                return new ImportResult("CSV", [], ["Could not find keyword and content columns."]);
            }
        }

        var groups = new Dictionary<string, SnippetGroup>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<SnippetGroup>();
        foreach (var row in rows)
        {
            string Cell(int index) => index >= 0 && index < row.Count ? row[index] : "";
            var keyword = Cell(keywordCol).Trim();
            var content = Cell(contentCol);
            if (keyword.Length == 0 && content.Length == 0)
            {
                continue;
            }

            var snippet = new Snippet { Keyword = keyword, Content = content.Replace("\r\n", "\n"), Name = Cell(nameCol).Trim() };
            if (Enum.TryParse<TriggerMode>(Cell(triggerCol), ignoreCase: true, out var trigger))
            {
                snippet.Trigger = trigger;
            }

            if (Enum.TryParse<CaseMode>(Cell(caseCol), ignoreCase: true, out var caseMode))
            {
                snippet.CaseMode = caseMode;
            }

            if (bool.TryParse(Cell(enabledCol), out var enabled))
            {
                snippet.Enabled = enabled;
            }

            var targetName = Cell(groupCol).Trim();
            if (targetName.Length == 0)
            {
                targetName = groupName;
            }

            if (!groups.TryGetValue(targetName, out var group))
            {
                group = new SnippetGroup { Name = targetName };
                groups[targetName] = group;
                ordered.Add(group);
            }

            group.Snippets.Add(snippet);
        }

        return new ImportResult(separator == '\t' ? "TSV" : "CSV", ordered, warnings);
    }

    // ---------------------------------------------------------------- AutoHotkey

    [GeneratedRegex(@"^\s*:(?<options>[^:\s]*):(?<abbr>.+?)::(?<text>.*)$")]
    private static partial Regex HotstringLine();

    [GeneratedRegex(@"^\s*#Hotstring\s+(?<options>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex HotstringDirective();

    public static ImportResult ImportAutoHotkey(string text, string groupName)
    {
        var group = new SnippetGroup { Name = groupName };
        var warnings = new List<string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var defaultOptions = "";
        for (var i = 0; i < lines.Length; i++)
        {
            var directive = HotstringDirective().Match(lines[i]);
            if (directive.Success && !directive.Groups["options"].Value.TrimStart().StartsWith("EndChars", StringComparison.OrdinalIgnoreCase)
                                  && !directive.Groups["options"].Value.TrimStart().StartsWith("NoMouse", StringComparison.OrdinalIgnoreCase))
            {
                defaultOptions = directive.Groups["options"].Value.Replace(" ", "");
                continue;
            }

            var match = HotstringLine().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var options = (defaultOptions + match.Groups["options"].Value).ToUpperInvariant();
            var abbreviation = match.Groups["abbr"].Value;
            var replacement = match.Groups["text"].Value;
            var raw = options.Contains('R') || options.Contains('T');

            if (options.Contains('X'))
            {
                warnings.Add($"Skipped '{abbreviation}': hotstrings that run code cannot be imported.");
                continue;
            }

            if (replacement.Trim().Length == 0)
            {
                // A continuation section "( ... )" holds multi-line text; anything else is code.
                var next = i + 1;
                while (next < lines.Length && lines[next].Trim().Length == 0)
                {
                    next++;
                }

                if (next < lines.Length && lines[next].TrimStart().StartsWith('('))
                {
                    var body = new List<string>();
                    var j = next + 1;
                    for (; j < lines.Length && !lines[j].TrimStart().StartsWith(')'); j++)
                    {
                        body.Add(lines[j]);
                    }

                    replacement = string.Join("\n", body);
                    i = j;
                }
                else
                {
                    warnings.Add($"Skipped '{abbreviation}': hotstrings that run code cannot be imported.");
                    continue;
                }
            }
            else
            {
                replacement = StripAhkComment(replacement);
            }

            var snippet = new Snippet
            {
                Keyword = abbreviation,
                Content = raw ? replacement : ConvertAhkSendText(replacement),
                Trigger = HasOption(options, '*') ? TriggerMode.Immediate : TriggerMode.Delimiter,
                ExpandInsideWords = options.Contains('?'),
                OmitDelimiter = HasOption(options, 'O'),
                CaseMode = options.Contains("C1") ? CaseMode.Insensitive : HasOption(options, 'C') ? CaseMode.Sensitive : CaseMode.Adapt,
            };
            group.Snippets.Add(snippet);
        }

        return new ImportResult("AutoHotkey", group.Snippets.Count > 0 ? [group] : [], warnings);
    }

    private static bool HasOption(string options, char option)
    {
        var index = options.IndexOf(option);
        return index >= 0 && (index + 1 >= options.Length || options[index + 1] != '0');
    }

    private static string StripAhkComment(string text)
    {
        var index = text.IndexOf(" ;", StringComparison.Ordinal);
        while (index > 0 && text[index - 1] == '`')
        {
            index = text.IndexOf(" ;", index + 2, StringComparison.Ordinal);
        }

        return index >= 0 ? text[..index] : text;
    }

    [GeneratedRegex(@"\{(?<key>[^{}\s]+|\{|\})(?:\s+(?<count>\d+))?\}")]
    private static partial Regex AhkBraceKey();

    private static string ConvertAhkSendText(string text)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '`' && i + 1 < text.Length)
            {
                var next = text[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => next,
                });
                continue;
            }

            if (c == '{')
            {
                var match = AhkBraceKey().Match(text, i);
                if (match.Success && match.Index == i)
                {
                    var key = match.Groups["key"].Value;
                    var count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture) : 1;
                    sb.Append(key.Length == 1
                        ? key switch
                        {
                            "{" => "{",
                            "}" => "}",
                            _ => new string(key[0], count),
                        }
                        : key.ToLowerInvariant() switch
                        {
                            "enter" when count == 1 => "\n",
                            "tab" when count == 1 => "\t",
                            "space" => new string(' ', count),
                            "raw" or "text" or "blind" => "",
                            _ => count == 1 ? "{key:" + key + "}" : "{key:" + key + "|" + count + "}",
                        });
                    i += match.Length - 1;
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------- espanso

    [GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z0-9_\-]+)(?:\.(?<field>[A-Za-z0-9_\-]+))?\s*\}\}")]
    private static partial Regex EspansoVariable();

    [GeneratedRegex(@"\[\[\s*(?<name>[A-Za-z0-9_\-]+)\s*\]\]")]
    private static partial Regex EspansoFormField();

    public static ImportResult ImportEspanso(string yaml, string groupName)
    {
        var deserializer = new DeserializerBuilder().Build();
        var root = deserializer.Deserialize<object>(yaml) as IDictionary<object, object>;
        var warnings = new List<string>();
        var group = new SnippetGroup { Name = groupName };
        if (root is null || !root.TryGetValue("matches", out var matchesObject) || matchesObject is not IList<object> matches)
        {
            return new ImportResult("espanso", [], ["No 'matches' section found."]);
        }

        var globalVars = root.TryGetValue("global_vars", out var gv) && gv is IList<object> gvl
            ? gvl.OfType<IDictionary<object, object>>().ToList()
            : [];

        foreach (var item in matches.OfType<IDictionary<object, object>>())
        {
            var triggers = new List<string>();
            if (Str(item, "trigger") is { } trigger)
            {
                triggers.Add(trigger);
            }

            if (item.TryGetValue("triggers", out var list) && list is IList<object> many)
            {
                triggers.AddRange(many.Select(t => t?.ToString() ?? "").Where(t => t.Length > 0));
            }

            if (triggers.Count == 0)
            {
                if (item.ContainsKey("regex"))
                {
                    warnings.Add($"Skipped regex trigger '{Str(item, "regex")}'.");
                }

                continue;
            }

            string? content;
            if (Str(item, "form") is { } form)
            {
                content = ConvertEspansoForm(form, item);
            }
            else
            {
                content = Str(item, "replace") ?? Str(item, "markdown") ?? Str(item, "html");
            }

            if (content is null)
            {
                warnings.Add($"Skipped '{triggers[0]}': only text replacements can be imported.");
                continue;
            }

            var vars = item.TryGetValue("vars", out var v) && v is IList<object> varList
                ? varList.OfType<IDictionary<object, object>>().Concat(globalVars).ToList()
                : globalVars;
            content = content.Replace("$|$", "{cursor}", StringComparison.Ordinal);
            content = EspansoVariable().Replace(content, m => ConvertEspansoVariable(m, vars, warnings));

            var word = Bool(item, "word") || Bool(item, "left_word") || Bool(item, "right_word");
            foreach (var t in triggers)
            {
                group.Snippets.Add(new Snippet
                {
                    Keyword = t,
                    Content = content,
                    Name = Str(item, "label") ?? "",
                    Trigger = word ? TriggerMode.Delimiter : TriggerMode.Immediate,
                    ExpandInsideWords = !word,
                    CaseMode = Bool(item, "propagate_case") ? CaseMode.Adapt : CaseMode.Sensitive,
                });
            }
        }

        return new ImportResult("espanso", group.Snippets.Count > 0 ? [group] : [], warnings);
    }

    private static string ConvertEspansoForm(string form, IDictionary<object, object> item)
    {
        var fields = item.TryGetValue("form_fields", out var f) && f is IDictionary<object, object> map ? map : null;
        return EspansoFormField().Replace(form, m =>
        {
            var name = m.Groups["name"].Value;
            if (fields?.TryGetValue(name, out var definition) == true && definition is IDictionary<object, object> d)
            {
                var type = Str(d, "type");
                if ((type is "choice" or "list") && d.TryGetValue("values", out var values))
                {
                    var options = values is IList<object> l
                        ? l.Select(o => o?.ToString() ?? "")
                        : (values?.ToString() ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    return "{choice:" + name + "|" + string.Join("|", options.Select(EscapeArg)) + "}";
                }

                if (Bool(d, "multiline"))
                {
                    return "{paragraph:" + name + "}";
                }

                if (Str(d, "default") is { } defaultValue)
                {
                    return "{input:" + name + "|" + EscapeArg(defaultValue) + "}";
                }
            }

            return "{input:" + name + "}";
        });
    }

    private static string ConvertEspansoVariable(Match match, List<IDictionary<object, object>> vars, List<string> warnings)
    {
        var name = match.Groups["name"].Value;
        if (match.Groups["field"].Success)
        {
            return "{input:" + match.Groups["field"].Value + "}";
        }

        var definition = vars.FirstOrDefault(v => Str(v, "name") == name);
        if (definition is null)
        {
            return name is "clipboard" ? "{clipboard}" : match.Value;
        }

        var parameters = definition.TryGetValue("params", out var p) && p is IDictionary<object, object> pd ? pd : new Dictionary<object, object>();
        switch (Str(definition, "type"))
        {
            case "date":
            {
                var format = ConvertStrftime(Str(parameters, "format") ?? "%x");
                var offset = Str(parameters, "offset");
                return offset is null ? "{datetime:" + format + "}" : "{datetime:" + format + "|" + offset + "s}";
            }

            case "clipboard":
                return "{clipboard}";
            case "echo":
                return Str(parameters, "echo") ?? "";
            case "random":
                return parameters.TryGetValue("choices", out var c) && c is IList<object> choices
                    ? "{random:" + string.Join("|", choices.Select(o => EscapeArg(o?.ToString() ?? ""))) + "}"
                    : "";
            case "choice":
                return parameters.TryGetValue("values", out var vals) && vals is IList<object> values
                    ? "{choice:" + name + "|" + string.Join("|", values.Select(o => EscapeArg(
                        o is IDictionary<object, object> od ? Str(od, "label") ?? Str(od, "id") ?? "" : o?.ToString() ?? ""))) + "}"
                    : "{input:" + name + "}";
            case "shell":
            {
                var command = Str(parameters, "cmd") ?? "";
                var shell = Str(parameters, "shell") ?? "cmd";
                return shell.Contains("powershell", StringComparison.OrdinalIgnoreCase) || shell.Contains("pwsh", StringComparison.OrdinalIgnoreCase)
                    ? "{powershell:" + EscapeArg(command) + "}"
                    : "{shell:" + EscapeArg(command) + "}";
            }

            case "match":
                return "{snippet:" + (Str(parameters, "trigger") ?? "") + "}";
            case "form":
                return "{input:" + name + "}";
            default:
                warnings.Add($"Variable '{name}' of type '{Str(definition, "type")}' is not supported and was kept as text.");
                return match.Value;
        }
    }

    /// <summary>Converts a strftime format (espanso/chrono) into a .NET custom date format.</summary>
    public static string ConvertStrftime(string format)
    {
        var sb = new StringBuilder();
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0)
            {
                return;
            }

            var text = literal.ToString();
            var needsQuotes = text.Any(c => char.IsLetter(c) || c is ':' or '/' or '\'' or '"' or '\\' or '%');
            if (needsQuotes)
            {
                sb.Append('\'').Append(text.Replace("'", "\\'")).Append('\'');
            }
            else
            {
                sb.Append(text);
            }

            literal.Clear();
        }

        void Token(string value)
        {
            FlushLiteral();
            sb.Append(value);
        }

        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                literal.Append(format[i]);
                continue;
            }

            var spec = format[++i];
            var noPad = false;
            if (spec is '-' or '_' && i + 1 < format.Length)
            {
                noPad = true;
                spec = format[++i];
            }

            switch (spec)
            {
                case 'Y': Token("yyyy"); break;
                case 'y': Token("yy"); break;
                case 'm': Token(noPad ? "%M" : "MM"); break;
                case 'd': Token(noPad ? "%d" : "dd"); break;
                case 'e': Token("%d"); break;
                case 'B': Token("MMMM"); break;
                case 'b' or 'h': Token("MMM"); break;
                case 'A': Token("dddd"); break;
                case 'a': Token("ddd"); break;
                case 'H': Token(noPad ? "%H" : "HH"); break;
                case 'k': Token("%H"); break;
                case 'I': Token(noPad ? "%h" : "hh"); break;
                case 'l': Token("%h"); break;
                case 'M': Token(noPad ? "%m" : "mm"); break;
                case 'S': Token(noPad ? "%s" : "ss"); break;
                case 'p' or 'P': Token("tt"); break;
                case 'z': Token("zzz"); break;
                case 'Z': Token("zzz"); break;
                case 'T': Token("HH:mm:ss"); break;
                case 'R': Token("HH:mm"); break;
                case 'D': Token("MM/dd/yy"); break;
                case 'F': Token("yyyy-MM-dd"); break;
                case 'x': Token("d"); break;
                case 'X': Token("T"); break;
                case 'c': Token("F"); break;
                case 'f': Token("ffffff"); break;
                case 'n': literal.Append('\n'); break;
                case 't': literal.Append('\t'); break;
                case '%': literal.Append('%'); break;
                default: literal.Append('%').Append(spec); break;
            }
        }

        FlushLiteral();
        return sb.ToString();
    }

    private static string EscapeArg(string value) =>
        value.Replace("\\", "\\\\").Replace("|", "\\|").Replace("{", "\\{").Replace("}", "\\}");

    private static string? Str(IDictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool Bool(IDictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && bool.TryParse(value?.ToString(), out var b) && b;

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? GetBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
