using System.Windows;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureDirectories();
        using var db = new AppDbContext();
        db.Database.EnsureCreated();
        DatabaseSchemaUpdater.Apply(db);
        DemoDataSeeder.Seed(db);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var settings = AppSettingsService.Load();
            if (settings.AutoBackupOnExit)
                BackupService.CreateAutomaticBackup(settings);
        }
        catch
        {
            // Ein Fehler beim Auto-Backup darf das Beenden der Anwendung nicht blockieren.
        }

        base.OnExit(e);
    }
}
