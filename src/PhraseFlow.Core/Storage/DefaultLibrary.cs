using System.Text.Json;
using PhraseFlow.Core.Models;

namespace PhraseFlow.Core.Storage;

/// <summary>The snippet collection shipped with PhraseFlow (embedded JSON files in <c>Library/</c>).</summary>
public static class DefaultLibrary
{
    private const string ResourcePrefix = "PhraseFlow.Core.Library.";

    public static IReadOnlyList<SnippetGroup> CreateGroups()
    {
        var assembly = typeof(DefaultLibrary).Assembly;
        var groups = new List<(int Order, SnippetGroup Group)>();
        foreach (var name in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var parsed = ParseGroup(reader.ReadToEnd(), name);
            var existing = groups.FindIndex(g => g.Group.SourceId == parsed.Group.SourceId);
            if (existing >= 0)
            {
                // Large groups are split across several files that share an id.
                foreach (var snippet in parsed.Group.Snippets)
                {
                    groups[existing].Group.Snippets.Add(snippet);
                }

                groups[existing] = (Math.Min(groups[existing].Order, parsed.Order), groups[existing].Group);
                continue;
            }

            groups.Add(parsed);
        }

        return groups.OrderBy(g => g.Order).ThenBy(g => g.Group.Name, StringComparer.Ordinal).Select(g => g.Group).ToList();
    }

    public static SnippetLibrary CreateLibrary()
    {
        var library = new SnippetLibrary();
        foreach (var group in CreateGroups())
        {
            library.Groups.Add(group);
        }

        return library;
    }

    internal static (int Order, SnippetGroup Group) ParseGroup(string json, string sourceName)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        var root = document.RootElement;
        var group = new SnippetGroup
        {
            Name = root.GetProperty("name").GetString() ?? sourceName,
            Description = root.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "",
            Enabled = !root.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean(),
            SourceId = root.TryGetProperty("id", out var id) ? id.GetString() : sourceName,
        };
        var order = root.TryGetProperty("order", out var orderElement) ? orderElement.GetInt32() : 1000;
        root.TryGetProperty("defaults", out var defaults);

        foreach (var item in root.GetProperty("snippets").EnumerateArray())
        {
            var length = item.GetArrayLength();
            if (length < 2)
            {
                throw new InvalidDataException($"{sourceName}: every snippet needs a keyword and content.");
            }

            var snippet = new Snippet
            {
                Keyword = item[0].GetString() ?? "",
                Content = item[1].GetString() ?? "",
                Name = length > 2 ? item[2].GetString() ?? "" : "",
            };
            ApplyOptions(snippet, defaults);
            if (length > 3)
            {
                ApplyOptions(snippet, item[3]);
            }

            group.Snippets.Add(snippet);
        }

        return (order, group);
    }

    private static void ApplyOptions(Snippet snippet, JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in options.EnumerateObject())
        {
            switch (property.Name)
            {
                case "trigger":
                    snippet.Trigger = property.Value.GetString() switch
                    {
                        "immediate" => TriggerMode.Immediate,
                        "picker" => TriggerMode.PickerOnly,
                        _ => TriggerMode.Delimiter,
                    };
                    break;
                case "case":
                    snippet.CaseMode = property.Value.GetString() switch
                    {
                        "sensitive" => CaseMode.Sensitive,
                        "insensitive" => CaseMode.Insensitive,
                        _ => CaseMode.Adapt,
                    };
                    break;
                case "insideWords":
                    snippet.ExpandInsideWords = property.Value.GetBoolean();
                    break;
                case "omitDelimiter":
                    snippet.OmitDelimiter = property.Value.GetBoolean();
                    break;
                case "plainText":
                    snippet.PlainText = property.Value.GetBoolean();
                    break;
                case "enabled":
                    snippet.Enabled = property.Value.GetBoolean();
                    break;
                case "method":
                    snippet.InsertMethod = property.Value.GetString() switch
                    {
                        "auto" => InsertMethod.Auto,
                        "typing" => InsertMethod.Typing,
                        "clipboard" => InsertMethod.Clipboard,
                        _ => InsertMethod.Default,
                    };
                    break;
                default:
                    throw new InvalidDataException($"Unknown snippet option '{property.Name}'.");
            }
        }
    }
}

public sealed record MergeResult(int GroupsAdded, int SnippetsAdded, int SnippetsSkipped);

public static class LibraryMerger
{
    /// <summary>Adds built-in groups and snippets that are missing from the library (nothing is overwritten).</summary>
    public static MergeResult RestoreDefaults(SnippetLibrary library) =>
        Merge(library, DefaultLibrary.CreateGroups(), matchBySource: true, compareContent: false);

    /// <summary>
    /// Merges imported groups into the library. Groups with the same name are combined; snippets whose keyword and
    /// content already exist in that group are skipped.
    /// </summary>
    public static MergeResult MergeImport(SnippetLibrary library, IEnumerable<SnippetGroup> imported) =>
        Merge(library, imported, matchBySource: false, compareContent: true);

    private static MergeResult Merge(SnippetLibrary library, IEnumerable<SnippetGroup> incoming, bool matchBySource, bool compareContent)
    {
        int groupsAdded = 0, added = 0, skipped = 0;
        foreach (var source in incoming)
        {
            var target = (matchBySource && source.SourceId is not null
                             ? library.Groups.FirstOrDefault(g => g.SourceId == source.SourceId)
                             : null)
                         ?? library.Groups.FirstOrDefault(g => string.Equals(g.Name, source.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                library.Groups.Add(source);
                groupsAdded++;
                added += source.Snippets.Count;
                continue;
            }

            foreach (var snippet in source.Snippets)
            {
                var exists = target.Snippets.Any(s =>
                    string.Equals(s.Keyword, snippet.Keyword, StringComparison.OrdinalIgnoreCase) &&
                    (!compareContent || string.Equals(s.Content, snippet.Content, StringComparison.Ordinal)));
                if (exists)
                {
                    skipped++;
                    continue;
                }

                target.Snippets.Add(snippet);
                added++;
            }
        }

        return new MergeResult(groupsAdded, added, skipped);
    }
}
