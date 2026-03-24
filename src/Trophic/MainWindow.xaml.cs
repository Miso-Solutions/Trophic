using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Trophic.ViewModels;

namespace Trophic;

public partial class MainWindow : Window
{
    private static readonly Color SaveSuccessColor = Color.FromRgb(0x1A, 0x61, 0x5D); // #1A615D Pine Teal (brand SuccessColor)
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        viewModel.PropertyChanged += ViewModel_PropertyChanged;

        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // F1 — toggle shortcuts help (always available)
        if (e.Key == Key.F1)
        {
            _viewModel.ShowShortcutsHelp = !_viewModel.ShowShortcutsHelp;
            e.Handled = true;
            return;
        }

        // Escape — dismiss help overlay, or clear search
        if (e.Key == Key.Escape)
        {
            if (_viewModel.ShowShortcutsHelp)
            {
                _viewModel.ShowShortcutsHelp = false;
                e.Handled = true;
                return;
            }

            if (SearchBox.IsFocused)
            {
                _viewModel.SearchText = string.Empty;
                TrophyListView.Focus();
                e.Handled = true;
                return;
            }
        }

        // Skip remaining shortcuts when SearchBox is focused
        if (SearchBox.IsFocused)
            return;

        // Ctrl+F — focus search
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _viewModel.IsFileOpen)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        // Ctrl+A — select all trophies
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && _viewModel.IsFileOpen)
        {
            TrophyListView.SelectAll();
            e.Handled = true;
            return;
        }

        // Delete — clear selected trophy timestamps
        if (e.Key == Key.Delete && _viewModel.IsFileOpen && TrophyListView.SelectedItems.Count > 0)
        {
            if (TrophyListView.SelectedItems.Count > 1)
                _viewModel.BatchClearCommand.Execute(null);
            else
                _viewModel.ClearTimestampCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Space — toggle earned state
        if (e.Key == Key.Space && _viewModel.IsFileOpen && TrophyListView.SelectedItems.Count > 0)
        {
            if (TrophyListView.SelectedItems.Count > 1)
                _viewModel.BatchUnlockCommand.Execute(null);
            else
                _viewModel.ToggleTrophyCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Enter — edit timestamp (single select only)
        if (e.Key == Key.Enter && _viewModel.IsFileOpen && TrophyListView.SelectedItems.Count == 1)
        {
            _viewModel.EditTimestampCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SearchText = string.Empty;
        SearchBox.Focus();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ViewModel_PropertyChanged(sender, e));
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.ShowToast))
        {
            if (_viewModel.ShowToast)
                ShowToastAnimation();
            else
                HideToastAnimation();
        }
        else if (e.PropertyName == nameof(MainViewModel.SaveSucceeded))
        {
            if (_viewModel.SaveSucceeded)
                ShowSaveSucceededAnimation();
            else
                HideSaveSucceededAnimation();
        }
    }

    private Border? GetSaveButtonBg()
    {
        return VisualTreeHelper.GetChild(SaveButton, 0) as Border;
    }

    private void ShowSaveSucceededAnimation()
    {
        var bg = GetSaveButtonBg();
        if (bg == null) return;

        bg.Background = new SolidColorBrush(Colors.Transparent);
        SavePanel.Visibility = Visibility.Collapsed;
        SavedPanel.Visibility = Visibility.Visible;

        var colorAnim = new ColorAnimation(
            SaveSuccessColor,
            TimeSpan.FromMilliseconds(200));
        bg.Background.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

        var opacityAnim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
        SavedPanel.BeginAnimation(OpacityProperty, opacityAnim);
    }

    private void HideSaveSucceededAnimation()
    {
        var bg = GetSaveButtonBg();
        if (bg == null) return;

        var colorAnim = new ColorAnimation(
            Colors.Transparent,
            TimeSpan.FromMilliseconds(300));
        colorAnim.Completed += (_, _) =>
        {
            bg.Background.BeginAnimation(SolidColorBrush.ColorProperty, null);
            bg.Background = new SolidColorBrush(Colors.Transparent);
            SavePanel.Visibility = Visibility.Visible;
            SavedPanel.Visibility = Visibility.Collapsed;
        };
        bg.Background.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

        var opacityAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        SavedPanel.BeginAnimation(OpacityProperty, opacityAnim);
    }

    private void ShowToastAnimation()
    {
        ToastBorder.Visibility = Visibility.Visible;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var sb = new Storyboard();

        // Fade in
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
        { EasingFunction = ease };
        Storyboard.SetTarget(fadeIn, ToastBorder);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));

        // Slide up from Y=30 to Y=0 with overshoot
        var slideUp = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(450))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
        Storyboard.SetTarget(slideUp, ToastBorder);
        Storyboard.SetTargetProperty(slideUp,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        // Checkmark icon pop (scale 0 -> 1.2 -> 1)
        var iconPopX = new DoubleAnimationUsingKeyFrames();
        iconPopX.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        iconPopX.KeyFrames.Add(new EasingDoubleKeyFrame(1.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350)),
            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }));
        iconPopX.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)),
            ease));
        Storyboard.SetTarget(iconPopX, ToastIcon);
        Storyboard.SetTargetProperty(iconPopX,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));

        var iconPopY = new DoubleAnimationUsingKeyFrames();
        iconPopY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        iconPopY.KeyFrames.Add(new EasingDoubleKeyFrame(1.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350)),
            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }));
        iconPopY.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)),
            ease));
        Storyboard.SetTarget(iconPopY, ToastIcon);
        Storyboard.SetTargetProperty(iconPopY,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

        sb.Children.Add(fadeIn);
        sb.Children.Add(slideUp);
        sb.Children.Add(iconPopX);
        sb.Children.Add(iconPopY);
        sb.Begin();
    }

    private void HideToastAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var sb = new Storyboard();

        // Fade out
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
        { EasingFunction = ease };
        Storyboard.SetTarget(fadeOut, ToastBorder);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));

        // Slide down
        var slideDown = new DoubleAnimation(20, TimeSpan.FromMilliseconds(300))
        { EasingFunction = ease };
        Storyboard.SetTarget(slideDown, ToastBorder);
        Storyboard.SetTargetProperty(slideDown,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        sb.Completed += (_, _) => ToastBorder.Visibility = Visibility.Collapsed;
        sb.Children.Add(fadeOut);
        sb.Children.Add(slideDown);
        sb.Begin();
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var lang in MainViewModel.AvailableLanguages)
        {
            var item = new MenuItem { Header = lang.Name, Tag = lang };
            item.Click += (_, _) => _viewModel.SelectLanguageCommand.Execute(lang);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (Button)sender;
        menu.IsOpen = true;
    }

    private void UnlockDropdown_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.IsOpen = true;
    }

    private void ClearDropdown_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.IsOpen = true;
    }

    private void UnlockAtTimestamp_Click(object sender, RoutedEventArgs e)
        => _viewModel.InstantUnlockAtTimestampCommand.Execute(null);

    private void UnlockRandomized_Click(object sender, RoutedEventArgs e)
        => _viewModel.InstantPlatinumCommand.Execute(null);

    private async void ImportTimestamps_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ImportTimestampsCommand.ExecuteAsync(null); }
        catch (Exception) { /* Handled by command */ }
    }

    private async void ImportFromFile_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ImportFromFileCommand.ExecuteAsync(null); }
        catch (Exception) { /* Handled by command */ }
    }

    private void ImportDropdown_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.IsOpen = true;
    }

    private void ClearAllIncludingSynced_Click(object sender, RoutedEventArgs e)
        => _viewModel.ClearAllIncludingSyncedCommand.Execute(null);

    private void TrophyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedItems = TrophyListView.SelectedItems;
        _viewModel.SelectedCount = TrophyListView.SelectedItems.Count;
    }

    private void ShortcutsOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        => _viewModel.ShowShortcutsHelp = false;

    private async void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: Trophic.Services.RecentFileEntry entry })
                await _viewModel.OpenRecentCommand.ExecuteAsync(entry);
        }
        catch (Exception) { /* Handled by command */ }
    }

    private void RemoveRecent_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // Prevent click from bubbling to parent row button
        if (sender is FrameworkElement { DataContext: Trophic.Services.RecentFileEntry entry })
            _viewModel.RemoveRecentCommand.Execute(entry);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            _viewModel.HandleDrop(paths);
    }
}
