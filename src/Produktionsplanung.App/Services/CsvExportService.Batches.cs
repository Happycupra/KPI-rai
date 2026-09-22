using System.Text;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static partial class CsvExportService
{
    private static void ExportBatchData(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var articles = new List<string> { Join(delimiter, "Artikel-ID", "Artikelnummer", "Name", "Einheit", "Standardmenge", "Sollrate/h", "Arbeitsplan-ID", "Aktiv", "Notizen") };
        foreach (var a in db.ArticleMasters.AsNoTracking().OrderBy(x => x.ArticleNumber))
            articles.Add(Join(delimiter, a.Id.ToString(C), a.ArticleNumber, a.Name, a.Unit, a.DefaultQuantity?.ToString(C) ?? "",
                a.DefaultIdealRatePerHour.ToString(C), a.DefaultRoutingId?.ToString(C) ?? "", Y(a.IsActive), a.Notes ?? ""));
        Write(directory, "artikel.csv", articles, encoding);

        var batches = new List<string> { Join(delimiter, "Auftrag-ID", "Auftrag", "Artikel-ID", "Artikelnummer", "Artikelname", "Charge", "Sollmenge", "Einheit", "Status", "Planungsdatum", "Produktionsbeginn UTC", "Abschluss UTC", "Arbeitsplan-ID", "Zuordnung") };
        foreach (var b in db.ProductionOrders.AsNoTracking().OrderBy(x => x.Id))
            batches.Add(Join(delimiter, b.Id.ToString(C), b.OrderNumber, b.ArticleMasterId?.ToString(C) ?? "", b.ArticleNumber, b.Product,
                b.BatchNumber, b.Quantity.ToString(C), b.Unit, b.Status, b.PlannedDate.ToString("yyyy-MM-dd"),
                b.StartedAtUtc?.ToString("O") ?? "", b.CompletedAtUtc?.ToString("O") ?? "", b.ManufacturingRoutingId?.ToString(C) ?? "",
                b.ArticleMasterId.HasValue ? "Zugeordnet" : "Artikelzuordnung offen"));
        Write(directory, "chargen.csv", batches, encoding);

        var actuals = new List<string> { Join(delimiter, "Ist-ID", "Auftrag-ID", "Artikelnummer historisch", "Charge historisch", "Datum", "Gutmenge", "Ausschuss", "Stillstand min") };
        foreach (var a in db.ProductionActuals.AsNoTracking().Include(x => x.Downtimes).OrderBy(x => x.Id))
            actuals.Add(Join(delimiter, a.Id.ToString(C), a.ProductionOrderId.ToString(C), a.ArticleNumberSnapshot ?? "", a.BatchNumber ?? "",
                a.Date.ToString("yyyy-MM-dd"), a.GoodQuantity.ToString(C), a.ScrapQuantity.ToString(C), a.Downtimes.Sum(x => x.Minutes).ToString(C)));
        Write(directory, "chargen_ist_historie.csv", actuals, encoding);
    }
}
