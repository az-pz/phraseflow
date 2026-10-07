using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PhraseFlow.App.Native;
using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Placeholders;

namespace PhraseFlow.App.Views;

/// <summary>Asks for the values of a snippet's fill-in fields and previews the result.</summary>
public partial class FillInWindow : Window
{
    private readonly SnippetEntry _entry;
    private readonly IExpansionHost _host;
    private readonly nint _target;
    private readonly List<(FormField Field, Func<string> Read)> _readers = [];

    internal FillInWindow(SnippetEntry entry, IReadOnlyList<FormField> fields, IExpansionHost host, nint target)
    {
        InitializeComponent();
        _entry = entry;
        _host = host;
        _target = target;
        TitleText.Text = entry.Name.Length > 0 ? entry.Name : entry.Keyword;
        SubtitleText.Text = entry.Keyword.Length > 0 ? $"{entry.Keyword} · {entry.GroupName}" : entry.GroupName;
        foreach (var field in fields)
        {
            AddField(field);
        }

        Loaded += (_, _) =>
        {
            PickerWindow.PositionNearCaret(this, _target);
            WindowHelper.Activate(new WindowInteropHelper(this).Handle);
            if (FieldsPanel.Children.OfType<FrameworkElement>().FirstOrDefault(c => c is not TextBlock) is { } first)
            {
                first.Focus();
                (first as TextBox)?.SelectAll();
            }

            UpdatePreview();
        };
    }

    public IReadOnlyDictionary<string, string> Values =>
        _readers.GroupBy(r => r.Field.Key).ToDictionary(g => g.Key, g => g.First().Read());

    private void AddField(FormField field)
    {
        if (field.Kind != FieldKind.Checkbox)
        {
            FieldsPanel.Children.Add(new TextBlock { Text = field.Label, Style = (Style)FindResource("FieldLabel") });
        }

        switch (field.Kind)
        {
            case FieldKind.Paragraph:
            {
                var box = new TextBox
                {
                    Text = field.DefaultValue,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 96,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalContentAlignment = VerticalAlignment.Top,
                };
                box.TextChanged += (_, _) => UpdatePreview();
                FieldsPanel.Children.Add(box);
                _readers.Add((field, () => box.Text));
                break;
            }

            case FieldKind.Choice:
            {
                var combo = new ComboBox { ItemsSource = field.Options, SelectedIndex = field.Options.Count > 0 ? 0 : -1, HorizontalAlignment = HorizontalAlignment.Stretch };
                combo.SelectionChanged += (_, _) => UpdatePreview();
                FieldsPanel.Children.Add(combo);
                _readers.Add((field, () => combo.SelectedItem as string ?? ""));
                break;
            }

            case FieldKind.Checkbox:
            {
                var check = new CheckBox { Content = field.Label, Margin = new Thickness(0, 12, 0, 0) };
                check.Checked += (_, _) => UpdatePreview();
                check.Unchecked += (_, _) => UpdatePreview();
                FieldsPanel.Children.Add(check);
                _readers.Add((field, () => check.IsChecked == true ? "true" : "false"));
                break;
            }

            case FieldKind.Date:
            {
                var picker = new DatePicker
                {
                    SelectedDate = DateTime.TryParse(field.DefaultValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateTime.Today,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                picker.SelectedDateChanged += (_, _) => UpdatePreview();
                FieldsPanel.Children.Add(picker);
                _readers.Add((field, () => (picker.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                break;
            }

            default:
            {
                var box = new TextBox { Text = field.DefaultValue };
                box.TextChanged += (_, _) => UpdatePreview();
                FieldsPanel.Children.Add(box);
                _readers.Add((field, () => box.Text));
                break;
            }
        }
    }

    private void UpdatePreview()
    {
        if (!IsLoaded)
        {
            return;
        }

        PreviewText.Text = TemplateEvaluator.Expand(_entry.Content, _entry.PlainText, _host, Values, EvaluationMode.Preview).ToPreviewString();
    }

    private void Insert_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
