using System.Windows;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppPaths.InitializeStorageMode(e.Args);
        CentralModeService.EnsureTemplateExists();

        if (!StartupHealthService.TryPrepare(out var startupError, out var startupWarning))
        {
            MessageBox.Show(startupError, "SolutionCompakt – Startprüfung", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (!string.IsNullOrWhiteSpace(startupWarning))
            MessageBox.Show(startupWarning, "SolutionCompakt – Speicherhinweis", MessageBoxButton.OK, MessageBoxImage.Warning);

        try
        {
            if (CentralModeService.IsEnabled)
            {
                var appSettings = AppSettingsService.Load();
                if (string.IsNullOrWhiteSpace(appSettings.CompanyId) || string.IsNullOrWhiteSpace(appSettings.CompanyCode))
                    throw new InvalidOperationException(
                        "Der Zentralbetrieb kann erst aktiviert werden, nachdem diese Installation lokal einer Firma zugeordnet und mindestens ein Benutzer angelegt wurde.");

                var initialization = await CentralDatabaseService.InitializeAsync();
                if (initialization.Migrated)
                {
                    MessageBox.Show(
                        "Der zentrale Mehrbenutzerbetrieb wurde eingerichtet.\n\n" +
                        "Die vorhandene lokale Datenbank wurde einmalig auf PostgreSQL übertragen. Ab jetzt arbeiten alle entsprechend konfigurierten PCs auf derselben zentralen Datenbank.",
                        "SolutionCompakt – Zentralbetrieb aktiviert",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
            {
                using var db = new AppDbContext(AppDatabaseMode.Local);
                db.Database.EnsureCreated();
                DatabaseSchemaUpdater.Apply(db);
                DemoDataSeeder.Seed(db);
                CompanyIdentityService.EnsureExistingInstallationIdentity();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "SolutionCompakt konnte den Datenspeicher nicht initialisieren.\n\n" + ex.Message +
                (CentralModeService.IsEnabled
                    ? $"\n\nZentralmodus-Konfiguration: {CentralModeService.ConfigPath}"
                    : string.Empty),
                "SolutionCompakt – Datenbank",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var licenseGate = await LicenseService.EvaluateStartupAsync();
        if (!licenseGate.Allowed && string.Equals(licenseGate.Status, "suspended", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                LicenseService.SuspendedMessage,
                "SolutionCompakt – Installation gesperrt",
                MessageBoxButton.OK,
                MessageBoxImage.Stop);
            Shutdown();
            return;
        }

        if (!licenseGate.Allowed)
        {
            var licenseWindow = new LicenseWindow(licenseGate.Message);
            if (licenseWindow.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            licenseGate = await LicenseService.EvaluateStartupAsync();
            if (!licenseGate.Allowed)
            {
                var suspended = string.Equals(licenseGate.Status, "suspended", StringComparison.OrdinalIgnoreCase);
                MessageBox.Show(
                    suspended ? LicenseService.SuspendedMessage : licenseGate.Message,
                    suspended ? "SolutionCompakt – Installation gesperrt" : "SolutionCompakt – Registrierung erforderlich",
                    MessageBoxButton.OK,
                    suspended ? MessageBoxImage.Stop : MessageBoxImage.Warning);
                Shutdown();
                return;
            }
        }

        if (licenseGate.IsTrial)
        {
            MessageBox.Show(
                $"DEMO-Version · {licenseGate.TrialDaysRemaining} Tag(e) Testphase verbleibend.\n\nEigentum von Irajet Ramadani – nur zu Testzwecken zu verwenden.",
                "SolutionCompakt – DEMO",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var login = new LoginWindow();
        if (login.ShowDialog() != true || !SessionService.IsAuthenticated)
        {
            Shutdown();
            return;
        }

        var main = new MainWindow();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();

        if (CentralModeService.IsEnabled)
        {
            var realtime = await CentralRealtimeService.StartAsync();
            if (!realtime.Success)
            {
                MessageBox.Show(
                    realtime.Message + "\n\nDie zentrale Datenbank bleibt nutzbar, aber automatische Echtzeit-Aktualisierungen zwischen PCs sind bis zur Wiederherstellung der Serververbindung eingeschränkt.",
                    "SolutionCompakt – Echtzeitverbindung",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (SessionService.IsAuthenticated)
                AuditService.Log("Abmeldung", "Session", SessionService.CurrentUser?.Id.ToString(), null);

            var settings = AppSettingsService.Load();
            if (!CentralModeService.IsEnabled && settings.AutoBackupOnExit && !SessionService.RequiresRestart)
                BackupService.CreateAutomaticBackup(settings);
        }
        catch
        {
            // Ein Fehler beim Auto-Backup/Audit darf das Beenden der Anwendung nicht blockieren.
        }
        finally
        {
            if (CentralModeService.IsEnabled)
            {
                try { CentralRealtimeService.StopAsync().GetAwaiter().GetResult(); } catch { }
            }
            SessionService.SignOut();
            StartupHealthService.Release();
        }

        base.OnExit(e);
    }
}
