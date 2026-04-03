using System.Windows;
using Trophic.Core.Helpers;
using Trophic.Core.Interfaces;
using Trophic.Core.Services;
using Trophic.Infrastructure;
using Trophic.Services;
using Trophic.ViewModels;

namespace Trophic;

public partial class App : Application
{
    private IWebScraperService? _scraper;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Clean up temp directories from previous sessions
        FileHelper.CleanupStaleTempDirectories();

        // Wire up services manually — no DI container needed
        var settings = new SettingsService();
        var pfdTool = new NativePfdService();
        var trophyFile = new TrophyFileService(pfdTool);
        var profiles = new ProfileService();
        var scraper = new WebScraperService();
        var catalog = new CatalogService(AppDomain.CurrentDomain.BaseDirectory);
        var downloadService = new TrophyDownloadService();
        var updateChecker = new UpdateCheckService();
        var autoUpdater = new AutoUpdateService();
        var logger = new DiagnosticLogger(AppDomain.CurrentDomain.BaseDirectory);
        var backupService = new BackupService();
        var dialogService = new DialogService(settings, catalog);

        _scraper = scraper;

        var recentFiles = new RecentFilesService();
        var viewModel = new MainViewModel(trophyFile, dialogService, scraper, profiles, settings, recentFiles,
            downloadService, updateChecker, autoUpdater, logger, backupService);
        var mainWindow = new MainWindow(viewModel);
        mainWindow.Show();

        // Pre-warm the browser in the background so it's ready when needed
        _ = Task.Run(async () =>
        {
            try { await scraper.InitializeAsync(); }
            catch (Exception) { /* non-critical — will retry on first use */ }
        });
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_scraper != null)
            await _scraper.DisposeAsync();

        base.OnExit(e);
    }
}
