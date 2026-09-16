using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static class OeeAnalyticsService
{
    public static OeeMetrics Calculate(
        double plannedProductionMinutes,
        double runMinutes,
        double totalQuantity,
        double goodQuantity,
        double idealRatePerHour)
    {
        var availability = plannedProductionMinutes > 0
            ? Math.Clamp(runMinutes / plannedProductionMinutes, 0, 1)
            : 0;

        var theoreticalQuantity = idealRatePerHour > 0 && runMinutes > 0
            ? idealRatePerHour * runMinutes / 60.0
            : 0;

        var rawPerformance = theoreticalQuantity > 0 ? totalQuantity / theoreticalQuantity : 0;
        var performance = Math.Clamp(rawPerformance, 0, 1);
        var quality = totalQuantity > 0 ? Math.Clamp(goodQuantity / totalQuantity, 0, 1) : 0;

        return new OeeMetrics
        {
            AvailabilityPercent = availability * 100,
            PerformancePercent = performance * 100,
            QualityPercent = quality * 100,
            OeePercent = availability * performance * quality * 100,
            TheoreticalQuantity = theoreticalQuantity,
            RawPerformancePercent = rawPerformance * 100
        };
    }

    public static OeePeriodSummary LoadPeriod(DateTime startDate, DateTime endDate)
    {
        using var db = new AppDbContext();
        var start = startDate.Date;
        var end = endDate.Date;

        var actuals = db.ProductionActuals.AsNoTracking()
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Workstation)
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Shift)
            .Include(x => x.Downtimes)
            .Where(x => x.Date.Date >= start && x.Date.Date <= end)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.ProductionOrder.Workstation.Name)
            .ThenBy(x => x.ProductionOrder.OrderNumber)
            .ToList();

        return Summarize(actuals);
    }

    public static OeePeriodSummary Summarize(IEnumerable<Produktionsplanung.App.Models.ProductionActual> actuals)
    {
        var rows = actuals.Select(x =>
        {
            var metrics = Calculate(
                x.PlannedProductionMinutes,
                x.RunMinutes,
                x.TotalQuantity,
                x.GoodQuantity,
                x.IdealRatePerHour);

            return new OeeActualMetricRow
            {
                ActualId = x.Id,
                Date = x.Date.Date,
                OrderNumber = x.ProductionOrder.OrderNumber,
                Product = x.ProductionOrder.Product,
                WorkstationId = x.ProductionOrder.WorkstationId,
                WorkstationName = x.ProductionOrder.Workstation.Name,
                Unit = x.ProductionOrder.Unit,
                ShiftName = x.ProductionOrder.Shift?.Name ?? "Individuell",
                TotalQuantity = x.TotalQuantity,
                GoodQuantity = x.GoodQuantity,
                ScrapQuantity = x.ScrapQuantity,
                PlannedProductionMinutes = x.PlannedProductionMinutes,
                RunMinutes = x.RunMinutes,
                DowntimeMinutes = x.Downtimes.Sum(d => d.Minutes),
                IdealRatePerHour = x.IdealRatePerHour,
                AvailabilityPercent = metrics.AvailabilityPercent,
                PerformancePercent = metrics.PerformancePercent,
                QualityPercent = metrics.QualityPercent,
                OeePercent = metrics.OeePercent
            };
        }).ToList();

        var summaryMetrics = Aggregate(rows);
        var workstationRows = rows
            .GroupBy(x => x.WorkstationId)
            .Select(group =>
            {
                var wsMetrics = Aggregate(group);
                return new OeeWorkstationRow
                {
                    WorkstationId = group.Key,
                    WorkstationName = group.First().WorkstationName,
                    Records = group.Count(),
                    TotalQuantityText = FormatQuantities(group, x => x.TotalQuantity),
                    GoodQuantityText = FormatQuantities(group, x => x.GoodQuantity),
                    ScrapQuantityText = FormatQuantities(group, x => x.ScrapQuantity),
                    DowntimeMinutes = group.Sum(x => x.DowntimeMinutes),
                    AvailabilityPercent = wsMetrics.AvailabilityPercent,
                    PerformancePercent = wsMetrics.PerformancePercent,
                    QualityPercent = wsMetrics.QualityPercent,
                    OeePercent = wsMetrics.OeePercent
                };
            })
            .OrderBy(x => x.WorkstationName)
            .ThenBy(x => x.WorkstationId)
            .ToList();

        return new OeePeriodSummary
        {
            Records = rows.Count,
            TotalQuantityText = FormatQuantities(rows, x => x.TotalQuantity),
            GoodQuantityText = FormatQuantities(rows, x => x.GoodQuantity),
            ScrapQuantityText = FormatQuantities(rows, x => x.ScrapQuantity),
            PlannedProductionMinutes = rows.Sum(x => x.PlannedProductionMinutes),
            RunMinutes = rows.Sum(x => x.RunMinutes),
            DowntimeMinutes = rows.Sum(x => x.DowntimeMinutes),
            AvailabilityPercent = summaryMetrics.AvailabilityPercent,
            PerformancePercent = summaryMetrics.PerformancePercent,
            QualityPercent = summaryMetrics.QualityPercent,
            OeePercent = summaryMetrics.OeePercent,
            ActualRows = rows,
            WorkstationRows = workstationRows
        };
    }

    private static OeeMetrics Aggregate(IEnumerable<OeeActualMetricRow> source)
    {
        var rows = source.ToList();
        var planned = rows.Sum(x => x.PlannedProductionMinutes);
        var run = rows.Sum(x => x.RunMinutes);
        // Convert each product's quantity to ideal production minutes before combining.
        // This remains invariant when a quantity AND its rate change from e.g. kg to g.
        var idealMinutes = rows.Sum(x => x.IdealRatePerHour > 0 ? x.TotalQuantity / x.IdealRatePerHour * 60 : 0);
        var goodMinutes = rows.Sum(x => x.IdealRatePerHour > 0 ? x.GoodQuantity / x.IdealRatePerHour * 60 : 0);
        var availability = planned > 0 ? Math.Clamp(run / planned, 0, 1) : 0;
        var performance = run > 0 ? Math.Clamp(idealMinutes / run, 0, 1) : 0;
        var quality = idealMinutes > 0 ? Math.Clamp(goodMinutes / idealMinutes, 0, 1) : 0;
        return new OeeMetrics
        {
            AvailabilityPercent = availability * 100,
            PerformancePercent = performance * 100,
            QualityPercent = quality * 100,
            OeePercent = availability * performance * quality * 100
        };
    }

    private static string FormatQuantities(IEnumerable<OeeActualMetricRow> rows, Func<OeeActualMetricRow, double> selector)
    {
        var parts = rows.GroupBy(x => x.Unit).OrderBy(x => x.Key)
            .Select(group => $"{group.Sum(selector):N2} {group.Key}").ToList();
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

}

public class OeeMetrics
{
    public double AvailabilityPercent { get; set; }
    public double PerformancePercent { get; set; }
    public double RawPerformancePercent { get; set; }
    public double QualityPercent { get; set; }
    public double OeePercent { get; set; }
    public double TheoreticalQuantity { get; set; }
}

public class OeeActualMetricRow
{
    public int WorkstationId { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int ActualId { get; set; }
    public DateTime Date { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public double TotalQuantity { get; set; }
    public double GoodQuantity { get; set; }
    public double ScrapQuantity { get; set; }
    public double PlannedProductionMinutes { get; set; }
    public double RunMinutes { get; set; }
    public double DowntimeMinutes { get; set; }
    public double IdealRatePerHour { get; set; }
    public double AvailabilityPercent { get; set; }
    public double PerformancePercent { get; set; }
    public double QualityPercent { get; set; }
    public double OeePercent { get; set; }
    public string OeeText => $"{OeePercent:N1} %";
}

public class OeeWorkstationRow
{
    public int WorkstationId { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public int Records { get; set; }
    public string TotalQuantityText { get; set; } = "—";
    public string GoodQuantityText { get; set; } = "—";
    public string ScrapQuantityText { get; set; } = "—";
    public double DowntimeMinutes { get; set; }
    public double AvailabilityPercent { get; set; }
    public double PerformancePercent { get; set; }
    public double QualityPercent { get; set; }
    public double OeePercent { get; set; }
    public string AvailabilityText => $"{AvailabilityPercent:N1} %";
    public string PerformanceText => $"{PerformancePercent:N1} %";
    public string QualityText => $"{QualityPercent:N1} %";
    public string OeeText => $"{OeePercent:N1} %";
}

public class OeePeriodSummary
{
    public int Records { get; set; }
    public string TotalQuantityText { get; set; } = "—";
    public string GoodQuantityText { get; set; } = "—";
    public string ScrapQuantityText { get; set; } = "—";
    public double PlannedProductionMinutes { get; set; }
    public double RunMinutes { get; set; }
    public double DowntimeMinutes { get; set; }
    public double AvailabilityPercent { get; set; }
    public double PerformancePercent { get; set; }
    public double QualityPercent { get; set; }
    public double OeePercent { get; set; }
    public List<OeeActualMetricRow> ActualRows { get; set; } = new();
    public List<OeeWorkstationRow> WorkstationRows { get; set; } = new();
}
