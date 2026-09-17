using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static class ProductionOrderCoverageService
{
    public static List<ProductionOrderCoverageRow> Load(DateTime startDate, DateTime endDate)
    {
        using var db = new AppDbContext();
        var start = startDate.Date;
        var end = endDate.Date;
        ProductionScheduleService.EnsureMissingRunSlots(db);

        var slots = db.ProductionRunSlots.AsNoTracking()
            .Include(x => x.Shift)
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Workstation)
            .Where(x => x.Date.Date >= start &&
                        x.Date.Date <= end &&
                        x.ProductionOrder.Status != "Abgeschlossen")
            .AsEnumerable()
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Shift.StartTime)
            .ThenBy(x => x.ProductionOrder.Workstation.Name)
            .ThenBy(x => x.ProductionOrder.OrderNumber)
            .ToList();

        if (slots.Count == 0)
            return new List<ProductionOrderCoverageRow>();

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date >= start && x.Date.Date <= end)
            .ToList();

        var rows = new List<ProductionOrderCoverageRow>();
        foreach (var slot in slots)
        {
            var order = slot.ProductionOrder;
            var plannedStaff = assignments
                .Where(x => x.Date.Date == slot.Date.Date &&
                            x.WorkstationId == order.WorkstationId &&
                            x.ShiftId == slot.ShiftId)
                .Select(x => x.EmployeeId)
                .Distinct()
                .Count();

            var difference = plannedStaff - order.RequiredStaff;
            var coverageStatus = difference < 0
                ? "Unterbesetzt"
                : difference == 0
                    ? "Gedeckt"
                    : "Über Bedarf";

            rows.Add(new ProductionOrderCoverageRow
            {
                RunSlotId = slot.Id,
                SequenceNumber = slot.SequenceNumber,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Product = order.Product,
                Date = slot.Date.Date,
                WorkstationId = order.WorkstationId,
                WorkstationName = order.Workstation.Name,
                ShiftId = slot.ShiftId,
                ShiftName = slot.Shift.Name,
                RequiredStaff = order.RequiredStaff,
                PlannedStaff = plannedStaff,
                Difference = difference,
                CoverageStatus = coverageStatus,
                Priority = order.Priority,
                OrderStatus = order.Status
            });
        }

        return rows;
    }
}

public class ProductionOrderCoverageRow
{
    public int RunSlotId { get; set; }
    public int SequenceNumber { get; set; }
    public int WorkstationId { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public int? ShiftId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public int RequiredStaff { get; set; }
    public int PlannedStaff { get; set; }
    public int Difference { get; set; }
    public string CoverageStatus { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string CoverageText => $"{PlannedStaff}/{RequiredStaff}";
    public string DifferenceText => Difference < 0 ? $"{Difference}" : $"+{Difference}";
    public string RunText => SequenceNumber > 0 ? $"Schicht {SequenceNumber}" : string.Empty;
}
