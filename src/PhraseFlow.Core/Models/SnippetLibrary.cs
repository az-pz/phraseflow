using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhraseFlow.Core.Models;

/// <summary>The complete, persisted snippet collection.</summary>
public sealed class SnippetLibrary
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public ObservableCollection<SnippetGroup> Groups { get; set; } = [];

    /// <summary>Persistent values for <c>{counter:name}</c> placeholders.</summary>
    public Dictionary<string, long> Counters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public LibraryStatistics Statistics { get; set; } = new();

    public IEnumerable<Snippet> AllSnippets => Groups.SelectMany(g => g.Snippets);

    public SnippetGroup? FindGroupOf(Snippet snippet) => Groups.FirstOrDefault(g => g.Snippets.Contains(snippet));

    /// <summary>Repairs data loaded from older or hand-edited files.</summary>
    public void Normalize()
    {
        Groups ??= [];
        Statistics ??= new LibraryStatistics();
        Counters = new Dictionary<string, long>(Counters ?? [], StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<Guid>();
        foreach (var group in Groups)
        {
            group.Snippets ??= [];
            if (!seenIds.Add(group.Id))
            {
                group.Id = Guid.NewGuid();
            }

            foreach (var snippet in group.Snippets)
            {
                snippet.Keyword ??= "";
                snippet.Name ??= "";
                snippet.Content ??= "";
                if (!seenIds.Add(snippet.Id))
                {
                    snippet.Id = Guid.NewGuid();
                }
            }
        }
    }
}

public sealed partial class LibraryStatistics : ObservableObject
{
    [ObservableProperty]
    private long _totalExpansions;

    [ObservableProperty]
    private long _charactersSaved;
}
