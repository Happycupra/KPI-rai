using System.Windows;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyProfessionalUiResources();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppPaths.InitializeStorageMode(e.Args);
        if (!StartupHealthService.TryPrepare(out var startupError, out var startupWarning))
        {
            MessageBox.Show(startupError, "SolutionCompakt – Startprüfung", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (!string.IsNullOrWhiteSpace(startupWarning))
        {
            MessageBox.Show(startupWarning, "SolutionCompakt – Speicherhinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated();
            DatabaseSchemaUpdater.Apply(db);
            DemoDataSeeder.Seed(db);
        }

        CompanyIdentityService.EnsureExistingInstallationIdentity();

        var licenseGate = await LicenseService.EvaluateStartupAsync();
        if (!licenseGate.Allowed &&
            string.Equals(licenseGate.Status, "suspended", StringComparison.OrdinalIgnoreCase))
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

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
    }

    private void ApplyProfessionalUiResources()
    {
        var designSystem = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/SolutionCompakt;component/Themes/ProfessionalUi.xaml",
                UriKind.Absolute)
        };

        // App.xaml already contains the legacy keys. Promote the audited resources into the
        // primary application dictionary so existing StaticResource references keep working
        // without requiring a second parallel component system.
        foreach (System.Collections.DictionaryEntry entry in designSystem)
            Resources[entry.Key] = entry.Value;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (SessionService.IsAuthenticated)
                AuditService.Log("Abmeldung", "Session", SessionService.CurrentUser?.Id.ToString(), null);

            var settings = AppSettingsService.Load();
            if (settings.AutoBackupOnExit && !SessionService.RequiresRestart)
                BackupService.CreateAutomaticBackup(settings);
        }
        catch
        {
            // Ein Fehler beim Auto-Backup/Audit darf das Beenden der Anwendung nicht blockieren.
        }
        finally
        {
            SessionService.SignOut();
            StartupHealthService.Release();
        }

        base.OnExit(e);
    }
}