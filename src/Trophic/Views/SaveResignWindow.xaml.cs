using System.Windows;
using Trophic.ViewModels;

namespace Trophic.Views;

public partial class SaveResignWindow : Window
{
    private readonly SaveResignViewModel _viewModel;

    public SaveResignWindow(SaveResignViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Activated += (_, _) => _viewModel.RefreshKeysStatus();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Cancel any in-flight batch so the background task doesn't keep producing
        // output after the window has been dismissed. The MainWindow.Closing handler
        // is responsible for awaiting completion on full-app shutdown.
        _viewModel.CancelBatch();
    }

    /// <summary>
    /// Awaits the in-flight batch to reach a safe stop point. Called by MainWindow
    /// on app shutdown to avoid killing the background task mid-write.
    /// </summary>
    public Task WaitForBatchAsync() => _viewModel.WaitForBatchAsync();
}
