using System.Windows;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MessageBox.Show(
            "Eigentum von Irajet Ramadani - nur zu Testzwecken zu verwenden",
            "SolutionCompakt - Nutzungshinweis",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

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
