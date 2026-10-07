using System.Text;

namespace PhraseFlow.Core.IO;

/// <summary>Minimal RFC 4180 CSV reader/writer (quoted fields, escaped quotes, embedded new lines).</summary>
public static class Csv
{
    public static char DetectSeparator(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        int commas = 0, semicolons = 0, tabs = 0;
        var quoted = false;
        foreach (var c in firstLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted)
            {
                switch (c)
                {
                    case ',': commas++; break;
                    case ';': semicolons++; break;
                    case '\t': tabs++; break;
                }
            }
        }

        if (tabs > 0 && tabs >= commas && tabs >= semicolons)
        {
            return '\t';
        }

        return semicolons > commas ? ';' : ',';
    }

    public static List<List<string>> Parse(string text, char separator)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
                fieldStarted = true;
            }
            else if (c == separator)
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = true;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                if (fieldStarted || field.Length > 0 || row.Count > 0)
                {
                    row.Add(field.ToString());
                    rows.Add(row);
                }

                row = [];
                field.Clear();
                fieldStarted = false;
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }

        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    public static string Escape(string value, char separator = ',')
    {
        var needsQuotes = value.IndexOfAny([separator, '"', '\r', '\n']) >= 0 || value.StartsWith(' ') || value.EndsWith(' ');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Write(IEnumerable<IEnumerable<string>> rows, char separator = ',')
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendJoin(separator, row.Select(v => Escape(v, separator)));
            sb.Append("\r\n");
        }

        return sb.ToString();
    }
}
