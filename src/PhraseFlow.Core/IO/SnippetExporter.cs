using System.Text;
using System.Text.Json;
using PhraseFlow.Core.Models;
using PhraseFlow.Core.Storage;

namespace PhraseFlow.Core.IO;

public static class SnippetExporter
{
    /// <summary>Exports groups in PhraseFlow's library format (can be imported or used as a backup).</summary>
    public static string ToJson(IEnumerable<SnippetGroup> groups)
    {
        var library = new SnippetLibrary();
        foreach (var group in groups)
        {
            library.Groups.Add(group);
        }

        return JsonSerializer.Serialize(library, JsonConfig.Options);
    }

    public static string ToCsv(IEnumerable<SnippetGroup> groups)
    {
        var rows = new List<IEnumerable<string>>
        {
            new[] { "Keyword", "Content", "Name", "Group", "Trigger", "Case", "Enabled" },
        };
        foreach (var group in groups)
        {
            foreach (var s in group.Snippets)
            {
                rows.Add(
                [
                    s.Keyword, s.Content, s.Name, group.Name, s.Trigger.ToString(), s.CaseMode.ToString(),
                    s.Enabled ? "true" : "false",
                ]);
            }
        }

        return Csv.Write(rows);
    }

    /// <summary>Exports enabled snippets as AutoHotkey v2 hotstrings (static text only).</summary>
    public static string ToAutoHotkey(IEnumerable<SnippetGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; Exported from PhraseFlow. Placeholders are exported as plain text.");
        sb.AppendLine("#Requires AutoHotkey v2.0");
        foreach (var group in groups)
        {
            sb.AppendLine().Append("; ").AppendLine(group.Name);
            foreach (var s in group.Snippets.Where(s => s.Keyword.Length > 0 && !s.Keyword.Contains("::", StringComparison.Ordinal)))
            {
                var options = new StringBuilder("T");
                if (s.Trigger == TriggerMode.Immediate)
                {
                    options.Append('*');
                }

                if (s.ExpandInsideWords)
                {
                    options.Append('?');
                }

                if (s.CaseMode == CaseMode.Sensitive)
                {
                    options.Append('C');
                }
                else if (s.CaseMode == CaseMode.Insensitive)
                {
                    options.Append("C1");
                }

                if (s.OmitDelimiter)
                {
                    options.Append('O');
                }

                var content = s.Content.Replace("\r\n", "\n");
                if (content.Contains('\n'))
                {
                    sb.Append(':').Append(options).Append(':').Append(s.Keyword).AppendLine("::");
                    sb.AppendLine("(");
                    sb.AppendLine(content);
                    sb.AppendLine(")");
                }
                else
                {
                    sb.Append(':').Append(options).Append(':').Append(s.Keyword).Append("::").AppendLine(content);
                }
            }
        }

        return sb.ToString();
    }
}
