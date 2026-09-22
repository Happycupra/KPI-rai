using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed record BatchComparisonRow(BatchRow Batch, double? GoodQuantity, double? ScrapQuantity,
    double? ScrapPercent, double? RunMinutes, double? DowntimeMinutes, string DataNote)
{
    public double? GoodPerHour
    {
        get
        {
            var rate = RunMinutes > 0 ? GoodQuantity / (RunMinutes / 60) : null;
            return rate.HasValue && double.IsFinite(rate.Value) ? rate : null;
        }
    }
    public string GoodText => Format(GoodQuantity, Batch.Unit);
    public string ScrapText => Format(ScrapQuantity, Batch.Unit);
    public string ScrapPercentText => Format(ScrapPercent, "%");
    public string RunText => Format(RunMinutes, "min");
    public string DowntimeText => Format(DowntimeMinutes, "min");
    public string RateText => Format(GoodPerHour, Batch.Unit + "/h");
    private static string Format(double? value, string unit) => value.HasValue
        ? $"{value.Value.ToString("N2", CultureInfo.GetCultureInfo("de-CH"))} {unit}" : "Nicht erfasst";
}

public static class BatchComparisonService
{
    public static List<BatchComparisonRow> Load(int articleId, int limit = 10)
    {
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));
        var batches = BatchService.Search(new("Abgeschlossen", ArticleId: articleId)).Take(limit).ToList();
        var ids = batches.Select(x => x.Id).ToArray();
        using var db = new AppDbContext();
        var actuals = db.ProductionActuals.AsNoTracking().Include(x => x.Downtimes)
            .Where(x => ids.Contains(x.ProductionOrderId)).ToList().ToLookup(x => x.ProductionOrderId);
        var slots = db.ProductionRunSlots.AsNoTracking().Where(x => ids.Contains(x.ProductionOrderId))
            .Select(x => new { x.Id, x.ProductionOrderId }).ToList().ToLookup(x => x.ProductionOrderId);
        return batches.Select(b => Summarize(b, actuals[b.Id].ToList(), slots[b.Id].Select(x => x.Id).ToArray())).ToList();
    }

    public static BatchComparisonRow Summarize(BatchRow batch, IReadOnlyList<ProductionActual> actuals, IReadOnlyList<int> slotIds)
    {
        if (actuals.Count == 0) return new(batch, null, null, null, null, null, "Keine Ist-Erfassungen");
        // Historical mismatches must not appear as comparable output for the selected article/batch.
        var inconsistent = actuals.Any(x =>
            (!string.IsNullOrWhiteSpace(x.UnitSnapshot) && x.UnitSnapshot != batch.Unit) ||
            (!string.IsNullOrWhiteSpace(x.ArticleNumberSnapshot) && x.ArticleNumberSnapshot != batch.ArticleNumber) ||
            (!string.IsNullOrWhiteSpace(x.BatchNumber) && x.BatchNumber != batch.BatchNumber) ||
            new[] { x.GoodQuantity, x.ScrapQuantity, x.TotalQuantity, x.RunMinutes }.Any(v => !double.IsFinite(v) || v < 0) ||
            Math.Abs(x.TotalQuantity - x.GoodQuantity - x.ScrapQuantity) > 0.01 ||
            x.Downtimes.Any(d => !double.IsFinite(d.Minutes) || d.Minutes < 0));
        if (inconsistent) return new(batch, null, null, null, null, null, "Abweichende oder ungültige Altdaten - Details prüfen");
        var good = actuals.Sum(x => x.GoodQuantity);
        var scrap = actuals.Sum(x => x.ScrapQuantity);
        var run = actuals.Sum(x => x.RunMinutes);
        var downtime = actuals.Sum(x => x.Downtimes.Sum(d => d.Minutes));
        if (new[] { good, scrap, good + scrap, run, downtime }.Any(x => !double.IsFinite(x)))
            return new(batch, null, null, null, null, null, "Summen ausserhalb des gültigen Zahlenbereichs");
        var covered = slotIds.Count(id => actuals.Any(x => x.ProductionRunSlotId == id));
        var note = slotIds.Count == 0 ? "Schichtabdeckung unbekannt" : $"{covered}/{slotIds.Count} Schichten erfasst";
        if (covered < slotIds.Count) note += " - unvollständig";
        if (actuals.Any(x => !x.ProductionRunSlotId.HasValue)) note += "; Erfassung ohne Schichtzuordnung";
        if (actuals.Where(x => x.ProductionRunSlotId.HasValue).GroupBy(x => x.ProductionRunSlotId).Any(g => g.Count() > 1))
            note += "; mehrere Erfassungen je Schicht";
        if (actuals.Any(x => string.IsNullOrWhiteSpace(x.ArticleNumberSnapshot) || string.IsNullOrWhiteSpace(x.BatchNumber) || string.IsNullOrWhiteSpace(x.UnitSnapshot)))
            note += "; historische Angaben fehlen";
        return new(batch, good, scrap, good + scrap > 0 ? scrap / (good + scrap) * 100 : null, run, downtime, note);
    }
}
