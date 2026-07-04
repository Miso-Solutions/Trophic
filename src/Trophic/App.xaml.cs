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
    public ConsoleKeysService? ConsoleKeysService { get; private set; }
    public SaveResignService? SaveResignService { get; private set; }
    public TrophyResignService? TrophyResignService { get; private set; }
    public IProfileService? ProfileService { get; private set; }
    public GamesConfService? GamesConfService { get; private set; }
    public Core.Licensing.LicenseService? LicenseService { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Clean up temp directories and orphan resign profiles from previous sessions
        FileHelper.CleanupStaleTempDirectories();
        TrophyResignService.CleanupOrphanTempProfiles();

        // Wire up services manually — no DI container needed
        var settings = new SettingsService();
        var pfdTool = new NativePfdService();
        var consoleKeys = new ConsoleKeysService();
        ConsoleKeysService = consoleKeys;
        var trophyFile = new TrophyFileService(pfdTool, consoleKeys);
        var profiles = new ProfileService();
        ProfileService = profiles;
        var gamesConf = new GamesConfService();
        GamesConfService = gamesConf;
        SaveResignService = new SaveResignService(pfdTool, consoleKeys, gamesConf);
        TrophyResignService = new TrophyResignService(pfdTool, consoleKeys);
        var scraper = new WebScraperService();
        var catalog = new CatalogService(AppDomain.CurrentDomain.BaseDirectory);
        var licenseService = new Core.Licensing.LicenseService(AppDomain.CurrentDomain.BaseDirectory);
        LicenseService = licenseService;
        var downloadService = new TrophyDownloadService(licenseService);
        var updateChecker = new UpdateCheckService();
        var autoUpdater = new AutoUpdateService();
        var logger = new DiagnosticLogger(AppDomain.CurrentDomain.BaseDirectory);
        var backupService = new BackupService();
        var dialogService = new DialogService(settings, catalog, licenseService);

        _scraper = scraper;

        var recentFiles = new RecentFilesService();
        var viewModel = new MainViewModel(trophyFile, dialogService, scraper, profiles, settings, recentFiles,
            downloadService, updateChecker, autoUpdater, logger, backupService, licenseService);
        var mainWindow = new MainWindow(viewModel);
        mainWindow.Show();

        // Pre-warm the browser in the background so it's ready when needed
        _ = Task.Run(async () =>
        {
            try { await scraper.InitializeAsync(); }
            catch (Exception) { /* non-critical — will retry on first use */ }
        });

        // Quietly re-validate the license against the server so revocations and
        // expiries propagate. Only meaningful once catalog licensing is enabled;
        // skipped otherwise to avoid hitting an unconfigured server on every launch.
        if (Core.Licensing.LicensingOptions.Enabled)
        {
            _ = Task.Run(async () =>
            {
                try { await licenseService.RefreshAsync(); }
                catch (Exception) { /* non-critical — offline grace covers transient failures */ }
            });
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_scraper != null)
            await _scraper.DisposeAsync();

        base.OnExit(e);
    }
}
