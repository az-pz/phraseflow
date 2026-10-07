using System.Windows;
using PhraseFlow.Core.Matching;
using PhraseFlow.Core.Models;

namespace PhraseFlow.App.Views;

public partial class GroupDialog : Window
{
    private readonly SnippetGroup _group;

    internal GroupDialog(SnippetGroup group, IReadOnlyList<string> runningApps)
    {
        InitializeComponent();
        _group = group;
        NameBox.Text = group.Name;
        DescriptionBox.Text = group.Description;
        EnabledBox.IsChecked = group.Enabled;
        AppsBox.Text = string.Join(Environment.NewLine, AppFilter.ParseList(group.Apps));
        RunningAppsBox.ItemsSource = runningApps;
        (group.AppFilter switch
        {
            AppFilterMode.OnlyListed => OnlyRadio,
            AppFilterMode.AllExceptListed => ExceptRadio,
            _ => AllAppsRadio,
        }).IsChecked = true;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var app = RunningAppsBox.Text.Trim();
        if (app.Length == 0)
        {
            return;
        }

        var apps = AppFilter.ParseList(AppsBox.Text).ToList();
        if (!apps.Contains(app, StringComparer.OrdinalIgnoreCase))
        {
            apps.Add(app);
        }

        AppsBox.Text = string.Join(Environment.NewLine, apps);
        RunningAppsBox.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Focus();
            return;
        }

        _group.Name = NameBox.Text.Trim();
        _group.Description = DescriptionBox.Text.Trim();
        _group.Enabled = EnabledBox.IsChecked == true;
        _group.Apps = string.Join("\n", AppFilter.ParseList(AppsBox.Text));
        _group.AppFilter = OnlyRadio.IsChecked == true ? AppFilterMode.OnlyListed
            : ExceptRadio.IsChecked == true ? AppFilterMode.AllExceptListed
            : AppFilterMode.AllApps;
        DialogResult = true;
    }
}
