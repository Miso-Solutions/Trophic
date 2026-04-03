using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Trophic.Core.Models;
using Trophic.Core.Services;
using Trophic.Properties;

namespace Trophic.Views;

public partial class TrophyCatalogDialog : Window
{
    private readonly CatalogService _catalog;

    public CatalogEntry? SelectedEntry { get; private set; }

    public TrophyCatalogDialog(CatalogService catalog)
    {
        InitializeComponent();
        _catalog = catalog;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var all = _catalog.Entries;
        ResultsListView.ItemsSource = all;
        ResultCountText.Text = string.Format(Strings.CatalogResultCount, all.Count);
        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

        var results = _catalog.Search(query);
        ResultsListView.ItemsSource = results;
        ResultCountText.Text = string.Format(Strings.CatalogResultCount, results.Count);
    }

    private void ResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DownloadButton.IsEnabled = ResultsListView.SelectedItem is CatalogEntry;
    }

    private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsListView.SelectedItem is CatalogEntry entry)
        {
            SelectedEntry = entry;
            DialogResult = true;
        }
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsListView.SelectedItem is CatalogEntry entry)
        {
            SelectedEntry = entry;
            DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
