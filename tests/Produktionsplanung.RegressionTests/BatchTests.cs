using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;
using Produktionsplanung.App.Views;

internal static partial class Program
{
    private static void Planner() => SessionService.SignIn(new UserAccount { Id = 1, Username = "batch-test", Role = UserRoles.Planner });
    private static int TestArticle(string number = "0001") => ArticleService.Save(new ArticleMaster
    { ArticleNumber = number, Name = "Leviaprost", Unit = "Stück", DefaultQuantity = 1000, DefaultIdealRatePerHour = 120 });

    private static CreateBatchRequest BatchRequest(int article, string batch, int count = 1)
    {
        using var db = new AppDbContext();
        var rule = db.WorkstationShiftRules.First();
        var date = DateTime.Today;
        for (var i = 0; i < 14 && ProductionScheduleService.BuildPreview(date, rule.WorkstationId, rule.ShiftId, count).Count != count; i++) date = date.AddDays(1);
        return new(article, "TEST-" + batch, batch, 750, date, rule.WorkstationId, rule.ShiftId, count, 1);
    }

    private static void Fails(Action action, string message)
    {
        var failed = false;
        try { action(); } catch (InvalidOperationException) { failed = true; }
        Check(failed, message);
    }

    private static void ArticleBatchCreation()
    {
        Planner();
        var article = TestArticle();
        var request = BatchRequest(article, "LP-001", 2);
        var first = BatchService.CreateFromArticle(request);
        var second = BatchService.CreateFromArticle(request with { OrderNumber = "TEST-2", BatchNumber = "LP-002", Quantity = 1200 });
        using var db = new AppDbContext();
        Check(db.ProductionRunSlots.Count(x => x.ProductionOrderId == first) == 2, "Run slots missing");
        Check(db.ProductionOrders.Single(x => x.Id == first).ArticleNumber == "0001", "Leading zeros lost");
        Check(BatchService.Search(new(ArticleId: article)).Count == 2, "Article does not group batches");
        Check(!db.JobCards.Any(x => x.ProductionOrderId == second) && !db.ProductionActuals.Any(x => x.ProductionOrderId == second), "History copied");
        Fails(() => BatchService.CreateFromArticle(request with { OrderNumber = "DUP" }), "Duplicate batch allowed");
        Fails(() => BatchService.CreateFromArticle(request with { OrderNumber = "NAN", BatchNumber = "NAN", Quantity = double.NaN }), "NaN allowed");
        var a = ArticleService.GetDetails(article);
        a.Name = "Leviaprost neu";
        ArticleService.Save(a);
        Check(BatchService.GetDetails(first).Batch.Product == "Leviaprost", "Historical name changed");
        a.ArticleNumber = "0002";
        Fails(() => ArticleService.Save(a), "Used article number editable");
        ArticleService.Deactivate(article);
        Fails(() => BatchService.CreateFromArticle(request with { OrderNumber = "INACTIVE", BatchNumber = "INACTIVE" }), "Inactive article usable");
        Check(BatchService.GetDetails(second).Batch.Quantity == 1200, "Independent quantities lost");
    }

    private static void BatchLifecycle()
    {
        Planner();
        var article = TestArticle();
        var id = BatchService.CreateFromArticle(BatchRequest(article, "FLOW"));
        using (var db = new AppDbContext())
        {
            var order = db.ProductionOrders.Single(x => x.Id == id);
            db.JobCards.Add(new JobCard { ProductionOrderId = id, SequenceNumber = 1, OperationCode = "TEST", OperationName = "Fertigen", WorkstationId = order.WorkstationId, PlannedMinutes = 30, RequiredStaff = 1 });
            db.SaveChanges();
        }
        Fails(() => BatchService.Complete(id), "Unfinished cards allowed completion");
        var vm = new ManufacturingControlViewModel();
        vm.SelectedProductionOrder = vm.ProductionOrders.Single(x => x.Id == id);
        vm.SelectedJobCard = vm.JobCards.Single();
        vm.StartJobCardCommand.Execute(null);
        var started = BatchService.GetDetails(id).Batch.StartedAtUtc;
        Check(started.HasValue, "Start timestamp missing");
        vm.SelectedJobCard = vm.JobCards.Single();
        vm.FinishGoodQuantity = 700;
        vm.FinishScrapQuantity = 50;
        vm.FinishJobCardCommand.Execute(null);
        var details = BatchService.GetDetails(id);
        Check(details.Batch.Status == "Abgeschlossen" && details.Batch.CompletedAtUtc.HasValue && details.Batch.CompletedSteps == 1, "Automatic completion missing");
        Check(BatchService.Search(new("Abgeschlossen", ArticleId: article)).Single().Id == id, "Archive mismatch");
        var editor = new ProductionOrderManagementViewModel();
        editor.SelectedOrder = editor.Orders.Single(x => x.Id == id);
        editor.Status = "Läuft";
        editor.SaveCommand.Execute(null);
        Check(BatchService.GetDetails(id).Batch.Status == "Abgeschlossen", "Editor bypassed archive lock");
        Fails(() => BatchService.Reopen(id, "  "), "Empty reopen reason accepted");
        BatchService.Reopen(id, "Menge kontrollieren");
        Check(BatchService.GetDetails(id).Batch.StartedAtUtc == started, "Reopen reset start");
        Check(BatchService.GetDetails(id).Batch.CompletedAtUtc is null, "Reopen kept completion");
        using (var db = new AppDbContext()) Check(db.AuditLogs.Any(x => x.Action == "Charge wieder geöffnet" && x.Details!.Contains("Menge kontrollieren")), "Reopen audit missing");
        BatchService.Complete(id);
        var manual = BatchService.CreateFromArticle(BatchRequest(article, "MANUAL"));
        BatchService.Complete(manual);
        Check(BatchService.GetDetails(manual).Batch.CompletedAtUtc.HasValue, "Manual completion missing");
    }

    private static void BatchRolesAndActuals()
    {
        Planner();
        var article = TestArticle();
        var id = BatchService.CreateFromArticle(BatchRequest(article, "ACTUAL"));
        var vm = new ProductionActualViewModel();
        vm.FocusOrder(id);
        vm.ArticleNumber = "wrong";
        vm.BatchNumber = "wrong";
        vm.TotalQuantity = vm.GoodQuantity = 50;
        vm.SaveCommand.Execute(null);
        using (var db = new AppDbContext())
        {
            var actual = db.ProductionActuals.Single(x => x.ProductionOrderId == id);
            Check(actual.ArticleNumberSnapshot == "0001" && actual.BatchNumber == "ACTUAL", "New actual identity not copied from order");
            actual.BatchNumber = "LEGACY-DIFFERENT";
            db.SaveChanges();
        }
        vm.RefreshCommand.Execute(null);
        vm.SelectedActual = vm.Actuals.Single(x => x.ProductionOrderId == id);
        vm.BatchNumber = "overwrite";
        vm.SaveCommand.Execute(null);
        Check(BatchService.GetDetails(id).Actuals.Single().BatchNumber == "LEGACY-DIFFERENT", "Historical snapshot overwritten");
        BatchService.Complete(id);
        vm.TotalQuantity = vm.GoodQuantity = 999;
        vm.SaveCommand.Execute(null);
        vm.DeleteActualCommand.Execute(null);
        Check(BatchService.GetDetails(id).Actuals.Single().GoodQuantity == 50, "Closed actual changed/deleted");
        SessionService.SignIn(new UserAccount { Username = "observer", Role = UserRoles.Observer });
        Check(BatchService.Search().Count > 0 && BatchService.GetDetails(id).Batch.Id == id, "Observer cannot read");
        Fails(() => ArticleService.Save(new ArticleMaster { ArticleNumber = "x", Name = "x" }), "Observer saved article");
        Fails(() => BatchService.Reopen(id, "unauthorized"), "Observer reopened");
        Fails(() => BatchService.CreateFromArticle(BatchRequest(article, "OBS")), "Observer created batch");
    }

    private static void BatchCountsAndViews()
    {
        Planner();
        var article = TestArticle();
        var id = BatchService.CreateFromArticle(BatchRequest(article, "COUNT", 2));
        using (var db = new AppDbContext())
        {
            foreach (var slot in db.ProductionRunSlots.Where(x => x.ProductionOrderId == id)) slot.Date = DateTime.Today;
            db.ProductionOrders.Single(x => x.Id == id).Status = "Läuft";
            db.SaveChanges();
        }
        Check(BatchService.Search(new("Heute", ArticleId: article)).Count == 1, "Charge counted per shift");
        var dashboard = new DashboardViewModel();
        using (var db = new AppDbContext())
        {
            Check(dashboard.OrdersToday == db.ProductionRunSlots.Where(x => x.Date == DateTime.Today).Select(x => x.ProductionOrderId).Distinct().Count(), "Dashboard count mismatch");
            foreach (var slot in db.ProductionRunSlots.Where(x => x.ProductionOrderId == id)) slot.Date = DateTime.Today.AddDays(-2);
            db.SaveChanges();
        }
        Check(BatchService.Search(new("Laufend", ArticleId: article)).Count == 1, "Running batch outside today hidden");
        var browser = new BatchBrowserViewModel("Alle", article) { SearchText = "COUNT" };
        browser.SelectedBatch = browser.Rows.Single();
        browser.Refresh();
        Check(browser.SelectedBatch?.Id == id && browser.SearchText == "COUNT", "Refresh lost selection or filter");
        var view = new BatchDetailsView(id);
        view.Refresh();
        Check(((BatchDetails)view.DataContext).Batch.Id == id, "Detail identity mismatch");
        _ = new NewBatchWindow(article);
        _ = new ArticleEditorWindow(article);
        var actualView = new ProductionActualView(id);
        Check(((ProductionActualViewModel)actualView.DataContext).SelectedOrder?.Id == id, "Actual deep link wrong");
        var controlView = new ManufacturingControlView(id);
        Check(((ManufacturingControlViewModel)controlView.DataContext).SelectedProductionOrder?.Id == id, "Manufacturing deep link wrong");
        RenderPreview(new DashboardView(), "dashboard");
        RenderPreview(view, "chargendetails");
        RenderPreview(new ArticlesView(), "artikel");
    }

    private static void RenderPreview(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("KPI_BATCH_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        view.Width = 1280;
        view.Height = 1000;
        view.Measure(new Size(1280, 1000));
        view.Arrange(new Rect(0, 0, 1280, 1000));
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        // Do not pump Application startup: the harness uses only isolated fixture databases.
        view.Measure(new Size(1280, 1000));
        view.Arrange(new Rect(0, 0, 1280, 1000));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1280, 1000, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private static void BatchBackupExportMigration()
    {
        Planner();
        var article = TestArticle();
        var id = BatchService.CreateFromArticle(BatchRequest(article, "BACKUP"));
        BatchService.Complete(id);
        using (var db = new AppDbContext())
        {
            DatabaseSchemaUpdater.Apply(db);
            DatabaseSchemaUpdater.Apply(db);
            Check(db.ProductionOrders.Single(x => x.Id == id).ArticleMasterId == article, "Repeat migration lost article");
        }
        var settings = AppSettingsService.Load();
        var export = CsvExportService.ExportAll(AppPaths.ExportsDirectory, settings);
        Check(File.ReadAllText(Path.Combine(export, "artikel.csv")).Contains("0001"), "Article export missing");
        Check(File.ReadAllText(Path.Combine(export, "chargen.csv")).Contains("BACKUP"), "Batch export missing");
        var backup = BackupService.CreateBackup(Path.Combine(AppPaths.BackupsDirectory, "batch-test.kpibackup"), settings);
        BatchService.Reopen(id, "test restore");
        BackupService.RestoreBackup(backup);
        Check(BatchService.GetDetails(id).Batch.Status == "Abgeschlossen", "Backup failed to restore completed batch");
        Check(ArticleService.GetDetails(article).ArticleNumber == "0001", "Backup lost article");
    }

    private static void LegacyBatchMigration()
    {
        // Exercise genuinely missing columns against the legacy schema, independent of EnsureCreated.
        var original = AppPaths.RootDirectoryOverride;
        AppPaths.RootDirectoryOverride = Path.Combine(original!, "legacy");
        try
        {
            using var db = new AppDbContext();
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE ArticleMasters (Id INTEGER PRIMARY KEY,ArticleNumber TEXT,Name TEXT,Unit TEXT);
                CREATE TABLE ManufacturingRoutings (Id INTEGER PRIMARY KEY);
                CREATE TABLE ProductionOrders (Id INTEGER PRIMARY KEY,ArticleNumber TEXT,Product TEXT,Unit TEXT,BatchNumber TEXT,Status TEXT);
                CREATE TABLE ProductionActuals (Id INTEGER PRIMARY KEY,ProductionOrderId INTEGER,BatchNumber TEXT);
                CREATE TABLE JobCards (Id INTEGER PRIMARY KEY,ProductionOrderId INTEGER,Status TEXT,CompletedAtUtc TEXT);
                INSERT INTO ArticleMasters VALUES (1,'0001','Leviaprost','Stück');
                INSERT INTO ProductionOrders VALUES (1,'0001','Leviaprost','Stück','OK','Abgeschlossen'),(2,'0001','Different','Stück','UNKNOWN','Abgeschlossen'),(3,'0001','Leviaprost','Stück','DUP','Geplant'),(4,'0001','Leviaprost','Stück','DUP','Geplant');
                INSERT INTO JobCards VALUES (1,1,'Fertig','2026-09-20 12:00:00');
                """);
            BatchSchemaUpdater.BackupBeforeUpgrade(db);
            BatchSchemaUpdater.Apply(db);
            BatchSchemaUpdater.Apply(db);
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ProductionOrders WHERE ArticleMasterId=1 AND CompletedAtUtc IS NOT NULL";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 1, "Unambiguous legacy order not linked/completed");
            command.CommandText = "SELECT COUNT(*) FROM ProductionOrders WHERE Id<>1 AND ArticleMasterId IS NULL AND CompletedAtUtc IS NULL";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 3, "Ambiguous data fabricated");
            Check(Directory.GetFiles(AppPaths.BackupsDirectory, "before-batches-*.kpibackup").Length == 1, "Migration backup missing");
        }
        finally { AppPaths.RootDirectoryOverride = original; }
    }

    private static void BatchRoutingAndArchiveFilters()
    {
        Planner();
        var article = TestArticle();
        int routingId;
        using (var db = new AppDbContext())
        {
            var workstation = db.Workstations.First();
            var operation = new OperationDefinition { Code = "BATCH-OP", Name = "Mischen", DefaultMinutes = 40 };
            var routing = new ManufacturingRouting { Name = "Leviaprost Standard", Product = "Different name intentionally" };
            db.OperationDefinitions.Add(operation);
            db.ManufacturingRoutings.Add(routing);
            db.SaveChanges();
            db.RoutingSteps.Add(new RoutingStep { ManufacturingRoutingId = routing.Id, OperationDefinitionId = operation.Id, SequenceNumber = 1, WorkstationId = workstation.Id, PlannedMinutes = 40, RequiredStaff = 1 });
            db.SaveChanges();
            routingId = routing.Id;
        }
        var a = ArticleService.GetDetails(article);
        a.DefaultRoutingId = routingId;
        ArticleService.Save(a);
        var id = BatchService.CreateFromArticle(BatchRequest(article, "ROUTING"));
        var vm = new ManufacturingControlViewModel();
        vm.SelectedProductionOrder = vm.ProductionOrders.Single(x => x.Id == id);
        vm.GenerateJobCardsCommand.Execute(null);
        Check(BatchService.GetDetails(id).JobCards.Single().OperationName == "Mischen", "Explicit routing not used");
        using (var db = new AppDbContext())
        {
            db.RoutingSteps.Single(x => x.ManufacturingRoutingId == routingId).PlannedMinutes = 90;
            db.SaveChanges();
        }
        Check(BatchService.GetDetails(id).JobCards.Single().PlannedMinutes == 40, "Existing card changed with routing");
        a.DefaultRoutingId = null;
        ArticleService.Save(a);
        using (var db = new AppDbContext()) Check(db.ProductionOrders.Single(x => x.Id == id).ManufacturingRoutingId == routingId, "Order routing changed with article");
        var manual = BatchService.CreateFromArticle(BatchRequest(article, "DATE"));
        BatchService.Complete(manual);
        Check(BatchService.Search(new("Abgeschlossen", ArticleId: article, CompletedFrom: DateTime.Today, CompletedTo: DateTime.Today)).Single().Id == manual, "Same-day archive filter wrong");
        Check(BatchService.Search(new("Abgeschlossen", ArticleId: article, CompletedTo: DateTime.Today.AddDays(-1))).Count == 0, "Archive end date wrong");
        using (var db = new AppDbContext())
        {
            db.ProductionOrders.Single(x => x.Id == manual).CompletedAtUtc = null;
            db.SaveChanges();
        }
        Check(BatchService.Search(new("Abgeschlossen", ArticleId: article)).Single().CompletedText == "Abschlussdatum unbekannt", "Unknown date not shown");
        var bad = BatchRequest(article, "ROLLBACK") with { WorkstationId = -1 };
        Fails(() => BatchService.CreateFromArticle(bad), "Invalid workstation accepted");
        using (var db = new AppDbContext()) Check(!db.ProductionOrders.Any(x => x.OrderNumber == bad.OrderNumber), "Failed creation left an order");
    }
}
