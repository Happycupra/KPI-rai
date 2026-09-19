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
            .Where(x =>
                x.Date.Date >= start &&
                x.Date.Date <= end &&
                x.ProductionOrder.Status != "Abgeschlossen")
            .AsEnumerable()
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Shift.StartTime)
            .ThenBy(x => x.ProductionOrder.Workstation.Name)
            .ThenBy(x => x.ProductionOrder.OrderNumber)
            .ToList();

        if (slots.Count == 0)
            return new();

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Where(x => x.Date.Date >= start && x.Date.Date <= end)
            .ToList();

        var employeeIds = assignments.Select(x => x.EmployeeId).Distinct().ToList();
        var activeIds = db.Employees.AsNoTracking()
            .Where(x => employeeIds.Contains(x.Id) && x.IsActive)
            .Select(x => x.Id)
            .ToHashSet();

        var absences = db.Absences.AsNoTracking()
            .Where(x =>
                employeeIds.Contains(x.EmployeeId) &&
                x.StartDate.Date <= end.AddDays(1) &&
                x.EndDate.Date >= start)
            .ToList();

        var rows = new List<ProductionOrderCoverageRow>();
        foreach (var slot in slots)
        {
            var order = slot.ProductionOrder;
            var matching = assignments
                .Where(x =>
                    x.Date.Date == slot.Date.Date &&
                    x.WorkstationId == order.WorkstationId &&
                    x.ShiftId == slot.ShiftId)
                .GroupBy(x => x.EmployeeId)
                .Select(x => x.First())
                .OrderBy(x => x.Employee.LastName)
                .ThenBy(x => x.Employee.FirstName)
                .ToList();

            var plannedStaff = matching.Count;
            var availableQualified = matching.Count(assignment =>
            {
                if (!activeIds.Contains(assignment.EmployeeId))
                    return false;

                var from = assignment.Date.Date + assignment.StartTime;
                var to = assignment.Date.Date + assignment.EndTime;
                if (to <= from)
                    to = to.AddDays(1);

                if (absences.Any(x =>
                        x.EmployeeId == assignment.EmployeeId &&
                        x.StartDate.Date <= to.Date &&
                        x.EndDate.Date >= from.Date))
                    return false;

                return QualificationPlanningService.CheckEmployee(
                    db,
                    assignment.EmployeeId,
                    order.WorkstationId).IsQualified;
            });

            var difference = availableQualified - order.RequiredStaff;
            var status = difference < 0
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
                AvailableQualifiedStaff = availableQualified,
                Difference = difference,
                CoverageStatus = status,
                Priority = order.Priority,
                OrderStatus = order.Status,
                TeamInitials = string.Join(" · ", matching.Select(x => BuildInitials(x.Employee.FirstName, x.Employee.LastName))),
                TeamNames = string.Join(", ", matching.Select(x => $"{x.Employee.FirstName} {x.Employee.LastName}"))
            });
        }

        return rows;
    }

    private static string BuildInitials(string firstName, string lastName)
    {
        var first = string.IsNullOrWhiteSpace(firstName)
            ? string.Empty
            : firstName.Trim()[0].ToString();
        var last = string.IsNullOrWhiteSpace(lastName)
            ? string.Empty
            : lastName.Trim()[0].ToString();
        return (first + last).ToUpperInvariant();
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
    public int AvailableQualifiedStaff { get; set; }
    public int Difference { get; set; }
    public string CoverageStatus { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string TeamInitials { get; set; } = string.Empty;
    public string TeamNames { get; set; } = string.Empty;

    public string CoverageText => PlannedStaff == AvailableQualifiedStaff
        ? $"{AvailableQualifiedStaff}/{RequiredStaff}"
        : $"{AvailableQualifiedStaff}/{RequiredStaff} verfügbar ({PlannedStaff} geplant)";

    public string DifferenceText => Difference < 0 ? $"{Difference}" : $"+{Difference}";
    public string RunText => SequenceNumber > 0 ? $"Schicht {SequenceNumber}" : string.Empty;
}
