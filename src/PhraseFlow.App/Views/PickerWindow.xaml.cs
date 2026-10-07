using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PhraseFlow.App.Native;
using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.App.Views;

public sealed record PickerResult(SnippetEntry Entry)
{
    public string Keyword => Entry.Keyword.Length > 0 ? Entry.Keyword : "—";

    public string Title => Entry.Name.Length > 0 ? Entry.Name : Entry.Snippet.Preview;

    public string Preview => Entry.Snippet.Preview;

    public string Group => Entry.GroupName;
}

/// <summary>Spotlight-style window to find a snippet and insert it into the previously active application.</summary>
public partial class PickerWindow : Window
{
    private const int MaxResults = 200;
    private readonly KeywordIndex _index;
    private readonly IExpansionHost _host;
    private readonly nint _target;
    private bool _closing;

    internal PickerWindow(KeywordIndex index, IExpansionHost host, nint target)
    {
        InitializeComponent();
        _index = index;
        _host = host;
        _target = target;
        QueryBox.TextChanged += (_, _) => UpdateResults();
        ResultList.SelectionChanged += (_, _) => UpdatePreview();
        ResultList.MouseDoubleClick += (_, _) => Choose(copy: false);
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => Dispatcher.BeginInvoke(SafeClose);
        Loaded += (_, _) =>
        {
            PositionNearCaret(this, _target);
            WindowHelper.Activate(new WindowInteropHelper(this).Handle);
            QueryBox.Focus();
            UpdateResults();
        };
    }

    internal event Action<SnippetEntry, bool>? Chosen;

    /// <summary>Ranks entries: keyword matches first, then names, then content; popular snippets break ties.</summary>
    internal static IEnumerable<SnippetEntry> Search(IReadOnlyList<SnippetEntry> entries, string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return entries
                .OrderByDescending(e => e.Snippet.LastUsed ?? DateTimeOffset.MinValue)
                .ThenByDescending(e => e.Snippet.UseCount);
        }

        return entries
            .Select(e => (Entry: e, Score: Score(e, tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.Snippet.UseCount)
            .ThenBy(x => x.Entry.Keyword.Length)
            .Select(x => x.Entry);
    }

    private static int Score(SnippetEntry entry, string[] tokens)
    {
        var total = 0;
        foreach (var token in tokens)
        {
            var score = 0;
            var keyword = entry.Keyword.TrimStart(';', ':', '\\', '/');
            if (entry.Keyword.Equals(token, StringComparison.OrdinalIgnoreCase) || keyword.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                score = 100;
            }
            else if (keyword.StartsWith(token, StringComparison.OrdinalIgnoreCase) || entry.Keyword.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                score = 80;
            }
            else if (entry.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                score = 60;
            }
            else if (entry.Keyword.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score = 50;
            }
            else if (entry.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score = 40;
            }
            else if (entry.Content.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score = 20;
            }
            else if (entry.GroupName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score = 10;
            }

            if (score == 0)
            {
                return 0;
            }

            total += score;
        }

        return total;
    }

    /// <summary>Places a window below the caret of <paramref name="target"/>, or centred on its monitor.</summary>
    internal static void PositionNearCaret(Window window, nint target)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var caret = target != 0 ? WindowHelper.GetCaretPosition(target) : null;
        var screen = caret is { } c
            ? System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(c.X, c.Y))
            : target != 0 ? System.Windows.Forms.Screen.FromHandle(target) : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var area = screen.WorkingArea;
        double left = area.Left / dpi.DpiScaleX, top = area.Top / dpi.DpiScaleY;
        double width = area.Width / dpi.DpiScaleX, height = area.Height / dpi.DpiScaleY;
        var w = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var h = window.ActualHeight > 0 ? window.ActualHeight : 480;

        if (caret is { } p)
        {
            var x = p.X / dpi.DpiScaleX - 24;
            var y = p.Y / dpi.DpiScaleY + 4;
            if (y + h > top + height)
            {
                y = Math.Max(top, y - h - 32);
            }

            window.Left = Math.Clamp(x, left, Math.Max(left, left + width - w));
            window.Top = Math.Clamp(y, top, Math.Max(top, top + height - h));
        }
        else
        {
            window.Left = left + (width - w) / 2;
            window.Top = top + height * 0.22;
        }
    }

    private void UpdateResults()
    {
        QueryHint.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var results = Search(_index.Entries, QueryBox.Text).Take(MaxResults).Select(e => new PickerResult(e)).ToList();
        ResultList.ItemsSource = results;
        CountRun.Text = results.Count == MaxResults ? $"· first {MaxResults} of many" : $"· {results.Count:N0} found";
        if (results.Count > 0)
        {
            ResultList.SelectedIndex = 0;
            ResultList.ScrollIntoView(results[0]);
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (ResultList.SelectedItem is not PickerResult result)
        {
            PreviewText.Text = "";
            return;
        }

        var entry = result.Entry;
        var fields = TemplateEvaluator.GetFields(entry.Content, entry.PlainText, _host);
        PreviewText.Text = TemplateEvaluator.Expand(entry.Content, entry.PlainText, _host, FormField.PreviewValues(fields), EvaluationMode.Preview).ToPreviewString();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var count = ResultList.Items.Count;
        switch (e.Key)
        {
            case Key.Escape:
                SafeClose();
                e.Handled = true;
                break;
            case Key.Enter:
                Choose(copy: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                e.Handled = true;
                break;
            case Key.Down when count > 0:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                Move(-1);
                e.Handled = true;
                break;
            case Key.PageDown when count > 0:
                Move(8);
                e.Handled = true;
                break;
            case Key.PageUp when count > 0:
                Move(-8);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        var index = Math.Clamp(ResultList.SelectedIndex + delta, 0, ResultList.Items.Count - 1);
        ResultList.SelectedIndex = index;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    private void Choose(bool copy)
    {
        if (ResultList.SelectedItem is not PickerResult result)
        {
            return;
        }

        SafeClose();
        Chosen?.Invoke(result.Entry, copy);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing can also start from outside (Alt+F4, taskbar); never call Close() again while it is in progress.
        _closing = true;
        base.OnClosing(e);
    }

    private void SafeClose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }
}
