using System.Windows;
using Microsoft.Win32;

namespace PhraseFlow.App.Views;

/// <summary>Message boxes and file dialogs owned by the active PhraseFlow window.</summary>
internal static class Dialogs
{
    private static Window? Owner => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    public static bool Confirm(string message, string title = "PhraseFlow") =>
        Owner is { IsVisible: true } owner
            ? MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK
            : MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public static void Info(string message, string title = "PhraseFlow") => Show(message, title, MessageBoxImage.Information);

    public static void Error(string message, string title = "PhraseFlow") => Show(message, title, MessageBoxImage.Error);

    public static string? OpenFile(string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Filter = filter, InitialDirectory = initialDirectory ?? "" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public static string? SaveFile(string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public static string? Prompt(string title, string label, string value = "")
    {
        var dialog = new InputDialog(title, label, value) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private static void Show(string message, string title, MessageBoxImage image)
    {
        if (Owner is { IsVisible: true } owner)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
        }
    }
}
