using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhraseFlow.Core.Models;

/// <summary>A named collection of snippets that can be enabled, disabled or scoped to applications together.</summary>
public sealed partial class SnippetGroup : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private AppFilterMode _appFilter = AppFilterMode.AllApps;

    /// <summary>Process names (e.g. <c>chrome.exe</c>) separated by commas, semicolons or new lines.</summary>
    [ObservableProperty]
    private string _apps = "";

    /// <summary>Identifier of the built-in library group this group originates from, if any.</summary>
    [ObservableProperty]
    private string? _sourceId;

    public ObservableCollection<Snippet> Snippets { get; set; } = [];

    public SnippetGroup Clone()
    {
        var clone = new SnippetGroup
        {
            Name = Name,
            Description = Description,
            Enabled = Enabled,
            AppFilter = AppFilter,
            Apps = Apps,
            SourceId = SourceId,
        };
        foreach (var snippet in Snippets)
        {
            clone.Snippets.Add(snippet.Clone());
        }

        return clone;
    }

    public override string ToString() => Name;
}
