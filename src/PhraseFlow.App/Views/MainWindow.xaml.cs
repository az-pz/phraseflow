using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhraseFlow.App.Services;
using PhraseFlow.App.ViewModels;
using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var app = AppController.Current;
        _viewModel = new MainViewModel(app);
        DataContext = _viewModel;

        _viewModel.FocusKeywordRequested += () => Dispatcher.BeginInvoke(() =>
        {
            Tabs.SelectedIndex = 0;
            KeywordBox.Focus();
            KeywordBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
        _viewModel.ScrollIntoViewRequested += item => Dispatcher.BeginInvoke(() => SnippetGrid.ScrollIntoView(item), System.Windows.Threading.DispatcherPriority.Background);
        app.NewSnippetRequested += () => Dispatcher.BeginInvoke(() => _viewModel.NewSnippetCommand.Execute(null));

        InputBindings.Add(new KeyBinding(_viewModel.NewSnippetCommand, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommandAdapter(() =>
        {
            Tabs.SelectedIndex = 0;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }), Key.F, ModifierKeys.Control));

        Activated += (_, _) => app.Hook.SyncCapsLock(Keyboard.IsKeyToggled(Key.CapsLock));
    }

    private void SnippetGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.SelectedItems = SnippetGrid.SelectedItems.OfType<SnippetItem>().ToList();

    private void SnippetGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            _viewModel.DeleteSnippetsCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _viewModel.SelectedSnippet is not null)
        {
            ContentBox.Focus();
            e.Handled = true;
        }
    }

    private void GridMenu_Opened(object sender, RoutedEventArgs e)
    {
        MoveToMenu.Items.Clear();
        foreach (var group in _viewModel.Library.Groups)
        {
            MoveToMenu.Items.Add(new MenuItem
            {
                Header = group.Name,
                Command = _viewModel.MoveSelectedToCommand,
                CommandParameter = group,
            });
        }
    }

    private void InsertPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = InsertPlaceholderButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var category in PlaceholderCatalog.Categories)
        {
            var categoryItem = new MenuItem { Header = category };
            foreach (var info in PlaceholderCatalog.All.Where(p => p.Category == category))
            {
                var item = new MenuItem
                {
                    Header = info.Template,
                    ToolTip = info.Description,
                    FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
                };
                item.Click += (_, _) => InsertIntoContent(info.Template);
                categoryItem.Items.Add(item);
            }

            menu.Items.Add(categoryItem);
        }

        menu.IsOpen = true;
    }

    private void InsertIntoContent(string text)
    {
        if (_viewModel.SelectedSnippet is null)
        {
            return;
        }

        var caret = ContentBox.SelectionStart;
        ContentBox.SelectedText = text;
        ContentBox.Focus();
        ContentBox.CaretIndex = caret + text.Length;
    }

    private void Playground_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _viewModel.SetPlaygroundFocused(true);

    private void Playground_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _viewModel.SetPlaygroundFocused(false);

    private void PlaceholderGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaceholderGrid.SelectedItem is PlaceholderDoc doc)
        {
            Clipboard.SetText(doc.Template);
            _viewModel.LastExpansion = $"Copied {doc.Template} to the clipboard";
        }
    }

    private sealed class RelayCommandAdapter(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
