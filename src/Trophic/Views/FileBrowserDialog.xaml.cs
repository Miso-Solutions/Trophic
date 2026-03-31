using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trophic.Properties;

namespace Trophic.Views;

public enum BrowseMode { Folder, File }

public sealed class FileItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public bool IsDrive { get; init; }

    public string Icon => IsDirectory ? "\uE8B7" : "\uE8A5";

    private static readonly SolidColorBrush FolderBrush = new(Color.FromRgb(0x1A, 0x61, 0x5D));
    private static readonly SolidColorBrush FileBrush = new(Color.FromRgb(0x68, 0x5C, 0x46));

    public SolidColorBrush IconBrush => IsDirectory ? FolderBrush : FileBrush;
    public string? Modified { get; init; }
}

public partial class FileBrowserDialog : Window
{
    private string? _currentPath;
    private List<string> _filterExtensions = [];
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _refreshTimer;

    public string? SelectedPath { get; private set; }
    public BrowseMode Mode { get; set; } = BrowseMode.Folder;
    public string? InitialDirectory { get; set; }
    public string? FileFilter { get; set; }
    public string? FormatHint { get; set; }

    public string DialogTitle
    {
        set => TitleText.Text = value;
    }

    public FileBrowserDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ParseFilter();

        if (Mode == BrowseMode.File)
        {
            if (_filterExtensions.Count > 0 && !_filterExtensions.Contains(".*"))
                FilterText.Text = string.Format(Strings.ShowingFiles, string.Join(", ", _filterExtensions.Select(ext => "*" + ext)));

            if (!string.IsNullOrEmpty(FormatHint))
            {
                FormatHintText.Text = FormatHint;
                FormatHintText.Visibility = Visibility.Visible;
            }

            HintPanel.Visibility = Visibility.Visible;
        }

        if (Mode == BrowseMode.Folder)
            SelectButton.Content = Strings.SelectFolder;

        // Debounce timer for FileSystemWatcher events
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            NavigateTo(_currentPath);
        };

        var startDir = InitialDirectory;
        if (startDir != null && Directory.Exists(startDir))
            NavigateTo(startDir);
        else
            NavigateTo(null); // drives list
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopWatcher();
        _refreshTimer?.Stop();
    }

    private void ParseFilter()
    {
        if (string.IsNullOrEmpty(FileFilter)) return;

        // Parse WPF filter format: "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        var parts = FileFilter.Split('|');
        for (int i = 1; i < parts.Length; i += 2)
        {
            var pattern = parts[i].Trim();
            if (pattern == "*.*")
            {
                _filterExtensions.Add(".*");
            }
            else if (pattern.StartsWith("*."))
            {
                _filterExtensions.Add(pattern[1..]); // e.g. ".txt"
            }
        }
    }

    private static bool IsHiddenOrSystem(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch
        {
            // Attributes may throw for locked/in-progress files — show them rather than hide
            return false;
        }
    }

    private void StartWatcher(string path)
    {
        StopWatcher();
        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
        }
        catch
        {
            // Some paths (network, restricted) may not support watching
        }
    }

    private void StopWatcher()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: restart the timer on each event so we batch rapid changes
        Dispatcher.BeginInvoke(() =>
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Start();
        });
    }

    private void NavigateTo(string? path)
    {
        var items = new List<FileItem>();

        if (path == null)
        {
            StopWatcher();

            // Show drives
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var label = drive.VolumeLabel;
                var name = string.IsNullOrEmpty(label)
                    ? drive.Name
                    : $"{drive.Name.TrimEnd('\\')} ({label})";
                items.Add(new FileItem
                {
                    Name = name,
                    FullPath = drive.RootDirectory.FullName,
                    IsDirectory = true,
                    IsDrive = true,
                    Modified = null
                });
            }

            _currentPath = null;
            BackButton.IsEnabled = false;
            FileListView.ItemsSource = items;
            RebuildBreadcrumb();

            // In folder mode, allow selecting nothing (disabled until a folder is picked)
            SelectButton.IsEnabled = false;
            return;
        }

        try
        {
            var dirInfo = new DirectoryInfo(path);
            if (!dirInfo.Exists)
            {
                NavigateTo(null);
                return;
            }

            // Enumerate subdirectories — fault-tolerant attribute check
            try
            {
                foreach (var dir in dirInfo.EnumerateDirectories()
                    .Where(d => !IsHiddenOrSystem(d))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    string? modified = null;
                    try { modified = dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm"); } catch { }

                    items.Add(new FileItem
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Modified = modified
                    });
                }
            }
            catch (UnauthorizedAccessException) { }

            // In file mode, enumerate matching files
            if (Mode == BrowseMode.File)
            {
                try
                {
                    var files = dirInfo.EnumerateFiles()
                        .Where(f => !IsHiddenOrSystem(f));

                    if (_filterExtensions.Count > 0 && !_filterExtensions.Contains(".*"))
                    {
                        files = files.Where(f =>
                            _filterExtensions.Any(ext =>
                                f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)));
                    }

                    foreach (var file in files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        string? modified = null;
                        try { modified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"); } catch { }

                        items.Add(new FileItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false,
                            Modified = modified
                        });
                    }
                }
                catch (UnauthorizedAccessException) { }
            }

            _currentPath = dirInfo.FullName;
            BackButton.IsEnabled = true;
            FileListView.ItemsSource = items;
            RebuildBreadcrumb();

            SelectButton.IsEnabled = Mode == BrowseMode.Folder; // current dir selectable in folder mode
            SelectedPath = Mode == BrowseMode.Folder ? _currentPath : null;

            // Watch this directory for changes
            StartWatcher(_currentPath);
        }
        catch (Exception)
        {
            // If we can't access the directory, go to parent or drives
            var parent = Directory.GetParent(path);
            NavigateTo(parent?.FullName);
        }
    }

    private void RebuildBreadcrumb()
    {
        BreadcrumbPanel.Children.Clear();

        // "This PC" root
        var pcLink = CreateBreadcrumbLink(Strings.ThisPC, null);
        BreadcrumbPanel.Children.Add(pcLink);

        if (_currentPath == null) return;

        // Split path into segments
        var parts = _currentPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var builtPath = "";
        foreach (var part in parts)
        {
            builtPath += part + Path.DirectorySeparatorChar;

            BreadcrumbPanel.Children.Add(new TextBlock
            {
                Text = "  \u203A  ", // thin chevron
                Foreground = (SolidColorBrush)FindResource("TextTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            });

            var segPath = builtPath;
            var link = CreateBreadcrumbLink(part, segPath);
            BreadcrumbPanel.Children.Add(link);
        }
    }

    private TextBlock CreateBreadcrumbLink(string text, string? navigatePath)
    {
        var accentBrush = (SolidColorBrush)FindResource("AccentBrush");
        var textBrush = (SolidColorBrush)FindResource("TextSecondaryBrush");

        var link = new TextBlock
        {
            Text = text,
            Foreground = textBrush,
            Cursor = Cursors.Hand,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        link.MouseEnter += (_, _) => link.Foreground = accentBrush;
        link.MouseLeave += (_, _) => link.Foreground = textBrush;
        link.MouseLeftButtonDown += (_, _) => NavigateTo(navigatePath);

        return link;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(_currentPath);
    }

    private void DesktopButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null) return;

        var parent = Directory.GetParent(_currentPath);
        if (parent == null)
        {
            // At drive root, go to drives list
            NavigateTo(null);
        }
        else
        {
            NavigateTo(parent.FullName);
        }
    }

    private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is not FileItem item) return;

        if (item.IsDirectory)
        {
            NavigateTo(item.FullPath);
        }
        else
        {
            // File double-click in file mode = select
            SelectedPath = item.FullPath;
            DialogResult = true;
        }
    }

    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileListView.SelectedItem is not FileItem item)
        {
            // Nothing selected
            if (Mode == BrowseMode.Folder)
            {
                SelectedPath = _currentPath;
                SelectButton.IsEnabled = _currentPath != null;
            }
            else
            {
                SelectedPath = null;
                SelectButton.IsEnabled = false;
            }
            return;
        }

        if (Mode == BrowseMode.Folder)
        {
            if (item.IsDirectory)
            {
                SelectedPath = item.FullPath;
                SelectButton.IsEnabled = true;
            }
        }
        else // File mode
        {
            if (!item.IsDirectory)
            {
                SelectedPath = item.FullPath;
                SelectButton.IsEnabled = true;
            }
            else
            {
                SelectedPath = null;
                SelectButton.IsEnabled = false;
            }
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPath != null)
            DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
