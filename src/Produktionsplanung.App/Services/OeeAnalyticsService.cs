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
                WorkstationName = x.ProductionOrder.Workstation.Name,
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

        var plannedMinutes = rows.Sum(x => x.PlannedProductionMinutes);
        var runMinutes = rows.Sum(x => x.RunMinutes);
        var totalQuantity = rows.Sum(x => x.TotalQuantity);
        var goodQuantity = rows.Sum(x => x.GoodQuantity);
        var scrapQuantity = rows.Sum(x => x.ScrapQuantity);
        var theoreticalQuantity = actuals.Sum(x => x.IdealRatePerHour * Math.Max(0, x.RunMinutes) / 60.0);

        var availability = plannedMinutes > 0 ? Math.Clamp(runMinutes / plannedMinutes, 0, 1) : 0;
        var performance = theoreticalQuantity > 0 ? Math.Clamp(totalQuantity / theoreticalQuantity, 0, 1) : 0;
        var quality = totalQuantity > 0 ? Math.Clamp(goodQuantity / totalQuantity, 0, 1) : 0;

        var workstationRows = rows
            .GroupBy(x => x.WorkstationName)
            .Select(group =>
            {
                var wsPlanned = group.Sum(x => x.PlannedProductionMinutes);
                var wsRun = group.Sum(x => x.RunMinutes);
                var wsTotal = group.Sum(x => x.TotalQuantity);
                var wsGood = group.Sum(x => x.GoodQuantity);
                var wsTheoretical = group.Sum(x => x.IdealRatePerHour * x.RunMinutes / 60.0);
                var wsAvailability = wsPlanned > 0 ? Math.Clamp(wsRun / wsPlanned, 0, 1) : 0;
                var wsPerformance = wsTheoretical > 0 ? Math.Clamp(wsTotal / wsTheoretical, 0, 1) : 0;
                var wsQuality = wsTotal > 0 ? Math.Clamp(wsGood / wsTotal, 0, 1) : 0;

                return new OeeWorkstationRow
                {
                    WorkstationName = group.Key,
                    Records = group.Count(),
                    TotalQuantity = wsTotal,
                    GoodQuantity = wsGood,
                    ScrapQuantity = group.Sum(x => x.ScrapQuantity),
                    DowntimeMinutes = group.Sum(x => x.DowntimeMinutes),
                    AvailabilityPercent = wsAvailability * 100,
                    PerformancePercent = wsPerformance * 100,
                    QualityPercent = wsQuality * 100,
                    OeePercent = wsAvailability * wsPerformance * wsQuality * 100
                };
            })
            .OrderBy(x => x.WorkstationName)
            .ToList();

        return new OeePeriodSummary
        {
            Records = rows.Count,
            TotalQuantity = totalQuantity,
            GoodQuantity = goodQuantity,
            ScrapQuantity = scrapQuantity,
            PlannedProductionMinutes = plannedMinutes,
            RunMinutes = runMinutes,
            DowntimeMinutes = rows.Sum(x => x.DowntimeMinutes),
            AvailabilityPercent = availability * 100,
            PerformancePercent = performance * 100,
            QualityPercent = quality * 100,
            OeePercent = availability * performance * quality * 100,
            ActualRows = rows,
            WorkstationRows = workstationRows
        };
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
    public string WorkstationName { get; set; } = string.Empty;
    public int Records { get; set; }
    public double TotalQuantity { get; set; }
    public double GoodQuantity { get; set; }
    public double ScrapQuantity { get; set; }
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
    public double TotalQuantity { get; set; }
    public double GoodQuantity { get; set; }
    public double ScrapQuantity { get; set; }
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
