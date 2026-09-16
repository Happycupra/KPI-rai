using System.Windows;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using var db = new AppDbContext();
        db.Database.EnsureCreated();
        DemoDataSeeder.Seed(db);
    }
}
