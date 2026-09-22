using System.IO;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.Views;

internal static partial class Program
{
    private static void BatchComparisonMetrics()
    {
        Planner();
        var article = TestArticle();
        var id = BatchService.CreateFromArticle(BatchRequest(article, "COMPARE", 2));
        using (var db = new AppDbContext())
        {
            var slots = db.ProductionRunSlots.Where(x => x.ProductionOrderId == id).OrderBy(x => x.SequenceNumber).ToList();
            for (var i = 0; i < 2; i++)
            {
                var actual = new ProductionActual { ProductionOrderId = id, ProductionRunSlotId = slots[i].Id,
                    ArticleNumberSnapshot = "0001", BatchNumber = "COMPARE", UnitSnapshot = "Stück",
                    GoodQuantity = i == 0 ? 90 : 45, ScrapQuantity = i == 0 ? 10 : 5, TotalQuantity = i == 0 ? 100 : 50,
                    RunMinutes = 30, PlannedProductionMinutes = 60, Date = slots[i].Date };
                actual.Downtimes.Add(new DowntimeEntry { Reason = "Umrüstung", Minutes = 5 });
                db.ProductionActuals.Add(actual);
            }
            db.JobCards.Add(new JobCard { ProductionOrderId = id, WorkstationId = db.ProductionOrders.Single(x => x.Id == id).WorkstationId,
                SequenceNumber = 1, Status = "Fertig", GoodQuantity = 9999 });
            db.SaveChanges();
        }
        BatchService.Complete(id);
        var row = BatchComparisonService.Load(article).Single();
        Near(row.GoodQuantity!.Value, 135);
        Near(row.ScrapQuantity!.Value, 15);
        Near(row.ScrapPercent!.Value, 10);
        Near(row.RunMinutes!.Value, 60);
        Near(row.DowntimeMinutes!.Value, 10);
        Near(row.GoodPerHour!.Value, 135);
        Check(row.DataNote == "2/2 Schichten erfasst", "Coverage wrong: " + row.DataNote);
        RenderPreview(new BatchComparisonView(article), "chargenvergleich");
        BatchService.Reopen(id, "Vergleichstest");
        Check(BatchComparisonService.Load(article).Count == 0, "Reopened batch included");
    }

    private static void BatchComparisonMissingData()
    {
        Planner();
        var article = TestArticle();
        var id = BatchService.CreateFromArticle(BatchRequest(article, "MISSING", 2));
        BatchService.Complete(id);
        var empty = BatchComparisonService.Load(article).Single();
        Check(empty.GoodQuantity is null && empty.GoodText == "Nicht erfasst", "Missing values shown as zero");
        using (var db = new AppDbContext())
        {
            var slot = db.ProductionRunSlots.First(x => x.ProductionOrderId == id);
            db.ProductionActuals.Add(new ProductionActual { ProductionOrderId = id, ProductionRunSlotId = slot.Id, UnitSnapshot = "Stück",
                ArticleNumberSnapshot = "0001", BatchNumber = "MISSING", Date = slot.Date });
            db.SaveChanges();
        }
        var zero = BatchComparisonService.Load(article).Single();
        Check(zero.GoodQuantity == 0 && zero.ScrapPercent is null && zero.GoodPerHour is null, "Zero denominator mishandled");
        Check(zero.DataNote.Contains("unvollständig"), "Partial coverage not disclosed");
        using (var db = new AppDbContext())
        {
            db.ProductionActuals.Single(x => x.ProductionOrderId == id).UnitSnapshot = "kg";
            db.SaveChanges();
        }
        var mixed = BatchComparisonService.Load(article).Single();
        Check(mixed.GoodQuantity is null && mixed.DataNote.Contains("Altdaten"), "Mixed units added");
        for (var i = 0; i < 11; i++) BatchService.Complete(BatchService.CreateFromArticle(BatchRequest(article, "RECENT-" + i)));
        var recent = BatchComparisonService.Load(article);
        Check(recent.Count == 10 && recent[0].Batch.BatchNumber == "RECENT-10", "Latest ten ordering wrong");
        Check(BatchComparisonService.Load(article, 25).Count == 12, "Limit selection wrong");
        var otherArticle = TestArticle("0002");
        Check(BatchComparisonService.Load(otherArticle).Count == 0, "Other article mixed into comparison");
        _ = new BatchComparisonView(article);
    }

    private static void BatchReportExports()
    {
        Planner();
        var article = TestArticle();
        var id = BatchService.CreateFromArticle(BatchRequest(article, "LP-2026-REPORT"));
        using (var db = new AppDbContext())
        {
            var order = db.ProductionOrders.Single(x => x.Id == id);
            order.Description = "Leviaprost - Chargendokumentation für den Testbetrieb";
            order.Comment = string.Join(" ", Enumerable.Repeat("Bemerkung: Menge geprüft, Abweichung dokumentiert. Umlaute äöü bleiben lesbar.", 14)) + " " + new string('X', 180);
            order.StartedAtUtc = DateTime.UtcNow.AddHours(-3);
            for (var i = 1; i <= 4; i++)
                db.JobCards.Add(new JobCard { ProductionOrderId = id, SequenceNumber = i, OperationCode = "AG-" + i,
                    OperationName = "Mischen, prüfen und abfüllen", WorkstationId = order.WorkstationId, PlannedMinutes = 30,
                    RunMinutes = 25, GoodQuantity = 150, ScrapQuantity = 5, Status = "Fertig", CompletedAtUtc = DateTime.UtcNow,
                    Comment = string.Join(" ", Enumerable.Repeat("Arbeitsgang abgeschlossen und Ergebnis kontrolliert.", 5)) });
            var actual = new ProductionActual { ProductionOrderId = id, ProductionRunSlotId = db.ProductionRunSlots.Single(x => x.ProductionOrderId == id).Id,
                ArticleNumberSnapshot = "0001", BatchNumber = "LP-2026-REPORT", ProductSnapshot = "Leviaprost", UnitSnapshot = "Stück",
                TotalQuantity = 155, GoodQuantity = 150, ScrapQuantity = 5, RunMinutes = 100, PlannedProductionMinutes = 120,
                Date = DateTime.Today, Comment = "Ist-Erfassung: 150 Gutstücke, 5 Ausschussstücke." };
            actual.Downtimes.Add(new DowntimeEntry { Reason = "Umrüstung", Minutes = 20, Comment = "Werkzeugwechsel" });
            db.ProductionActuals.Add(actual);
            db.SaveChanges();
        }
        BatchService.Complete(id);
        var directory = Environment.GetEnvironmentVariable("KPI_REPORT_PREVIEW_DIRECTORY") ?? AppPaths.ExportsDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "chargenbericht-test.pdf");
        var result = BatchReportPdfService.Export(id, path);
        using (var document = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            Check(document.PageCount == result.PageCount && result.PageCount >= 2, "Report pagination or save failed");
        var missingId = BatchService.CreateFromArticle(BatchRequest(article, "NO-DATA"));
        var missingPath = Path.Combine(directory, "chargenbericht-ohne-ist.pdf");
        var missing = BatchReportPdfService.Export(missingId, missingPath);
        Check(missing.PageCount == 1, "Empty report unexpectedly long");
        // Read-only observers can export an existing report; no database mutations are needed.
        SessionService.SignIn(new UserAccount { Username = "observer", Role = UserRoles.Observer });
        BatchReportPdfService.Export(id, path);
        Check(BatchService.GetDetails(id).Batch.Status == "Abgeschlossen", "Export changed batch");
        Check(!Directory.GetFiles(directory, "*.tmp").Any(), "Temporary report files left behind");
    }
}
