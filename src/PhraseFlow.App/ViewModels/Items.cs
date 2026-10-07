using CommunityToolkit.Mvvm.ComponentModel;
using PhraseFlow.Core.Models;

namespace PhraseFlow.App.ViewModels;

public sealed record Option<T>(T Value, string Label);

/// <summary>Row in the snippet list: a snippet plus the group that contains it.</summary>
public sealed partial class SnippetItem(Snippet snippet, SnippetGroup group) : ObservableObject
{
    public Snippet Snippet { get; } = snippet;

    [ObservableProperty]
    private SnippetGroup _group = group;
}

/// <summary>Entry in the group list; <see cref="Group"/> is null for "All snippets".</summary>
public sealed partial class GroupItem : ObservableObject
{
    public GroupItem(SnippetGroup? group)
    {
        Group = group;
        if (group is not null)
        {
            group.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SnippetGroup.Name))
                {
                    OnPropertyChanged(nameof(Name));
                }
            };
        }
    }

    public SnippetGroup? Group { get; }

    public bool IsAll => Group is null;

    public string Name => Group?.Name ?? "All snippets";

    [ObservableProperty]
    private int _count;
}

public sealed record PlaceholderDoc(string Category, string Syntax, string Description, string Example, string Result, string Template);
