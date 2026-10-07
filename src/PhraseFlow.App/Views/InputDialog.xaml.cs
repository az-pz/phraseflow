using System.Windows;

namespace PhraseFlow.App.Views;

public partial class InputDialog : Window
{
    public InputDialog(string title, string label, string value)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = value;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
