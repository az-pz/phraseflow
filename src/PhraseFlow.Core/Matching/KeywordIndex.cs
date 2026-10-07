using PhraseFlow.Core.Models;
using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.Core.Matching;

/// <summary>An immutable snapshot of a snippet used by the matcher and the expansion pipeline.</summary>
public sealed class SnippetEntry
{
    public SnippetEntry(Snippet snippet, SnippetGroup group, AppFilter appFilter)
    {
        Snippet = snippet;
        Group = group;
        AppFilter = appFilter;
        Keyword = snippet.Keyword;
        Name = snippet.Name;
        Content = snippet.Content;
        Trigger = snippet.Trigger;
        CaseMode = snippet.CaseMode;
        ExpandInsideWords = snippet.ExpandInsideWords;
        OmitDelimiter = snippet.OmitDelimiter;
        PlainText = snippet.PlainText;
        InsertMethod = snippet.InsertMethod;
        GroupName = group.Name;
        IsStaticText = PlainText || !TemplateParser.ContainsPlaceholders(Content);
    }

    public Snippet Snippet { get; }

    public SnippetGroup Group { get; }

    public AppFilter AppFilter { get; }

    public string Keyword { get; }

    public string Name { get; }

    public string Content { get; }

    public string GroupName { get; }

    public TriggerMode Trigger { get; }

    public CaseMode CaseMode { get; }

    public bool ExpandInsideWords { get; }

    public bool OmitDelimiter { get; }

    public bool PlainText { get; }

    public InsertMethod InsertMethod { get; }

    public SnippetReference ToReference() => new(Keyword, Content, PlainText);

    /// <summary>True when the content has no placeholders, so its expansion is known in advance.</summary>
    public bool IsStaticText { get; } 

    /// <summary>Returns true when expanding <paramref name="typedKeyword"/> would reproduce exactly what was typed.</summary>
    public bool WouldBeNoOp(string typedKeyword)
    {
        if (!IsStaticText || Content.Length != typedKeyword.Length)
        {
            return false;
        }

        var expected = CaseMode == CaseMode.Adapt
            ? CaseAdapter.Apply(ExpansionOutput.FromText(Content), CaseAdapter.Detect(typedKeyword, Keyword)).Text
            : Content;
        return string.Equals(expected, typedKeyword, StringComparison.Ordinal);
    }

    public override string ToString() => $"{Keyword} ({GroupName})";
}

public readonly record struct MatchCandidate(SnippetEntry Entry, int Length);

/// <summary>
/// Keyword lookup built as a trie over reversed keywords, so every keystroke checks only the suffixes of the
/// typed text that can still be keywords (independent of the number of snippets).
/// </summary>
public sealed class KeywordIndex
{
    private readonly Node _root = new();
    private readonly Dictionary<string, SnippetEntry> _byExactKeyword = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SnippetEntry> _byKeyword = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SnippetEntry> _entries = [];

    private KeywordIndex()
    {
    }

    public static KeywordIndex Empty { get; } = new();

    /// <summary>All enabled snippets in enabled groups, in library order (used by the picker).</summary>
    public IReadOnlyList<SnippetEntry> Entries => _entries;

    public int MaxKeywordLength { get; private set; }

    public int Count => _entries.Count;

    public static KeywordIndex Build(IEnumerable<SnippetGroup> groups)
    {
        var index = new KeywordIndex();
        foreach (var group in groups)
        {
            if (!group.Enabled)
            {
                continue;
            }

            var filter = AppFilter.FromText(group.AppFilter, group.Apps);
            foreach (var snippet in group.Snippets)
            {
                if (!snippet.Enabled)
                {
                    continue;
                }

                var entry = new SnippetEntry(snippet, group, filter);
                index._entries.Add(entry);
                if (string.IsNullOrEmpty(entry.Keyword))
                {
                    continue;
                }

                index._byExactKeyword.TryAdd(entry.Keyword, entry);
                index._byKeyword.TryAdd(entry.Keyword, entry);
                if (entry.Trigger != TriggerMode.PickerOnly)
                {
                    index.Insert(entry);
                }
            }
        }

        return index;
    }

    public SnippetEntry? FindByKeyword(string keyword)
    {
        keyword = keyword.Trim();
        return _byExactKeyword.TryGetValue(keyword, out var exact) ? exact
            : _byKeyword.TryGetValue(keyword, out var entry) ? entry
            : null;
    }

    /// <summary>
    /// Finds the longest keyword with the given trigger mode that ends at the end of <paramref name="text"/>.
    /// </summary>
    public MatchCandidate? FindSuffixMatch(ReadOnlySpan<char> text, TriggerMode mode, Func<SnippetEntry, bool>? filter = null)
    {
        MatchCandidate? best = null;
        var node = _root;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (node.Children is null || !node.Children.TryGetValue(char.ToLowerInvariant(text[i]), out var next))
            {
                break;
            }

            node = next;
            if (node.Entries is null)
            {
                continue;
            }

            var length = text.Length - i;
            foreach (var entry in node.Entries)
            {
                if (entry.Trigger != mode)
                {
                    continue;
                }

                if (entry.CaseMode == CaseMode.Sensitive && !text.Slice(i, length).SequenceEqual(entry.Keyword))
                {
                    continue;
                }

                if (!entry.ExpandInsideWords && WordChars.IsWordChar(entry.Keyword[0]) && i > 0 && WordChars.IsWordChar(text[i - 1]))
                {
                    continue;
                }

                if (filter is not null && !filter(entry))
                {
                    continue;
                }

                best = new MatchCandidate(entry, length);
                break;
            }
        }

        return best;
    }

    private void Insert(SnippetEntry entry)
    {
        var node = _root;
        var keyword = entry.Keyword;
        for (var i = keyword.Length - 1; i >= 0; i--)
        {
            var key = char.ToLowerInvariant(keyword[i]);
            node.Children ??= [];
            if (!node.Children.TryGetValue(key, out var next))
            {
                next = new Node();
                node.Children[key] = next;
            }

            node = next;
        }

        node.Entries ??= [];
        node.Entries.Add(entry);
        MaxKeywordLength = Math.Max(MaxKeywordLength, keyword.Length);
    }

    private sealed class Node
    {
        public Dictionary<char, Node>? Children;
        public List<SnippetEntry>? Entries;
    }
}
