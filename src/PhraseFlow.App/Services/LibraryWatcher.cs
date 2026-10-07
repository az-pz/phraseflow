using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using PhraseFlow.Core.Models;

namespace PhraseFlow.App.Services;

/// <summary>
/// Observes every group and snippet in the library and raises debounced notifications: one when the keyword
/// index must be rebuilt, and one when the library must be saved.
/// </summary>
internal sealed class LibraryWatcher : IDisposable
{
    private static readonly HashSet<string> StatisticProperties = [nameof(Snippet.UseCount), nameof(Snippet.LastUsed), nameof(Snippet.Preview)];

    private readonly SnippetLibrary _library;
    private readonly DispatcherTimer _indexTimer;
    private readonly DispatcherTimer _saveTimer;

    public LibraryWatcher(SnippetLibrary library, Action rebuildIndex, Action save)
    {
        _library = library;
        _indexTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _indexTimer.Tick += (_, _) =>
        {
            _indexTimer.Stop();
            rebuildIndex();
        };
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            save();
        };

        library.Groups.CollectionChanged += OnGroupsChanged;
        library.Statistics.PropertyChanged += OnStatisticsChanged;
        foreach (var group in library.Groups)
        {
            Attach(group);
        }
    }

    public bool HasPendingSave => _saveTimer.IsEnabled;

    /// <summary>Requests a save without an index rebuild (e.g. counters changed).</summary>
    public void MarkDirty() => Restart(_saveTimer);

    public void Dispose()
    {
        _indexTimer.Stop();
        _saveTimer.Stop();
        _library.Groups.CollectionChanged -= OnGroupsChanged;
        _library.Statistics.PropertyChanged -= OnStatisticsChanged;
        foreach (var group in _library.Groups)
        {
            Detach(group);
        }
    }

    private static void Restart(DispatcherTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private void StructureChanged()
    {
        Restart(_indexTimer);
        Restart(_saveTimer);
    }

    private void OnStatisticsChanged(object? sender, PropertyChangedEventArgs e) => Restart(_saveTimer);

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var group in e.OldItems?.OfType<SnippetGroup>() ?? [])
        {
            Detach(group);
        }

        foreach (var group in e.NewItems?.OfType<SnippetGroup>() ?? [])
        {
            Attach(group);
        }

        StructureChanged();
    }

    private void OnSnippetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var snippet in e.OldItems?.OfType<Snippet>() ?? [])
        {
            snippet.PropertyChanged -= OnSnippetChanged;
        }

        foreach (var snippet in e.NewItems?.OfType<Snippet>() ?? [])
        {
            snippet.PropertyChanged += OnSnippetChanged;
        }

        StructureChanged();
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e) => StructureChanged();

    private void OnSnippetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && StatisticProperties.Contains(e.PropertyName))
        {
            if (e.PropertyName != nameof(Snippet.Preview))
            {
                Restart(_saveTimer);
            }

            return;
        }

        StructureChanged();
    }

    private void Attach(SnippetGroup group)
    {
        group.PropertyChanged += OnGroupChanged;
        group.Snippets.CollectionChanged += OnSnippetsChanged;
        foreach (var snippet in group.Snippets)
        {
            snippet.PropertyChanged += OnSnippetChanged;
        }
    }

    private void Detach(SnippetGroup group)
    {
        group.PropertyChanged -= OnGroupChanged;
        group.Snippets.CollectionChanged -= OnSnippetsChanged;
        foreach (var snippet in group.Snippets)
        {
            snippet.PropertyChanged -= OnSnippetChanged;
        }
    }
}
