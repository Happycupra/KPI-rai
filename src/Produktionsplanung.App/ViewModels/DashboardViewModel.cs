using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    public ObservableCollection<DashboardIssue> Issues { get; } = new();
    public ObservableCollection<DashboardOrderRow> TodayOrders { get; } = new();
    public ObservableCollection<DashboardWorkstationRow> WorkstationStatuses { get; } = new();

    [ObservableProperty] private int activeEmployees;
    [ObservableProperty] private int availableEmployees;
    [ObservableProperty] private int plannedEmployees;
    [ObservableProperty] private int absentEmployees;
    [ObservableProperty] private int ordersToday;
    [ObservableProperty] private int runningOrders;
    [ObservableProperty] private int completedOrdersToday;
    [ObservableProperty] private int understaffedOrders;
    [ObservableProperty] private int personnelCoveragePercent;
    [ObservableProperty] private int openProblems;
    [ObservableProperty] private string lastUpdatedText = string.Empty;

    public string TodayText => DateTime.Today.ToString("dddd, dd.MM.yyyy");

    public DashboardViewModel() => Load();

    [RelayCommand]
    private void Refresh() => Load();

    private void Load()
    {
        var today = DateTime.Today;
        using var db = new AppDbContext();

        var activeEmployeeIds = db.Employees.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToList();

        var absentEmployeeIds = db.Absences.AsNoTracking()
            .Where(x => x.StartDate.Date <= today && x.EndDate.Date >= today)
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToList();

        var plannedEmployeeIds = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date == today)
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToList();

        ActiveEmployees = activeEmployeeIds.Count;
        AbsentEmployees = absentEmployeeIds.Count(x => activeEmployeeIds.Contains(x));
        AvailableEmployees = Math.Max(0, ActiveEmployees - AbsentEmployees);
        PlannedEmployees = plannedEmployeeIds.Count(x => activeEmployeeIds.Contains(x));

        var orders = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.PlannedDate.Date == today)
            .OrderBy(x => x.Shift!.StartTime)
            .ThenBy(x => x.Workstation.Name)
            .ThenBy(x => x.OrderNumber)
            .ToList();

        OrdersToday = orders.Count;
        RunningOrders = orders.Count(x => x.Status == "Läuft");
        CompletedOrdersToday = orders.Count(x => x.Status == "Abgeschlossen");

        var coverage = ProductionOrderCoverageService.Load(today, today);
        UnderstaffedOrders = coverage.Count(x => x.CoverageStatus == "Unterbesetzt");

        var requiredStaff = coverage.Sum(x => x.RequiredStaff);
        var coveredStaff = coverage.Sum(x => Math.Min(x.PlannedStaff, x.RequiredStaff));
        PersonnelCoveragePercent = requiredStaff <= 0
            ? 100
            : (int)Math.Round(coveredStaff * 100.0 / requiredStaff, MidpointRounding.AwayFromZero);

        BuildIssues(db, today, activeEmployeeIds, absentEmployeeIds, coverage);
        BuildOrders(orders, coverage);
        BuildWorkstationStatuses(db, orders, coverage);

        OpenProblems = Issues.Count;
        LastUpdatedText = $"Aktualisiert: {DateTime.Now:HH:mm}";
    }

    private void BuildIssues(
        AppDbContext db,
        DateTime today,
        List<int> activeEmployeeIds,
        List<int> absentEmployeeIds,
        List<ProductionOrderCoverageRow> coverage)
    {
        Issues.Clear();

        foreach (var row in coverage.Where(x => x.CoverageStatus == "Unterbesetzt"))
        {
            Issues.Add(new DashboardIssue
            {
                Severity = "Rot",
                Title = $"Personal fehlt · {row.OrderNumber}",
                Message = $"{row.WorkstationName} / {row.ShiftName}: {row.PlannedStaff}/{row.RequiredStaff} Personen eingeplant ({Math.Abs(row.Difference)} fehlen)."
            });
        }

        var absenceAssignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date == today &&
                        absentEmployeeIds.Contains(x.EmployeeId) &&
                        activeEmployeeIds.Contains(x.EmployeeId))
            .ToList();

        foreach (var assignment in absenceAssignments)
        {
            Issues.Add(new DashboardIssue
            {
                Severity = "Rot",
                Title = "Abwesender Mitarbeiter eingeplant",
                Message = $"{assignment.Employee.LastName}, {assignment.Employee.FirstName} ist auf {assignment.Workstation.Name} / {assignment.Shift?.Name ?? "Individuell"} eingeplant."
            });
        }

        var overdueOrders = db.ProductionOrders.AsNoTracking()
            .Where(x => x.PlannedDate.Date < today && x.Status != "Abgeschlossen")
            .OrderBy(x => x.PlannedDate)
            .Take(8)
            .ToList();

        foreach (var order in overdueOrders)
        {
            Issues.Add(new DashboardIssue
            {
                Severity = "Gelb",
                Title = $"Überfälliger Auftrag · {order.OrderNumber}",
                Message = $"Geplant für {order.PlannedDate:dd.MM.yyyy}, aktueller Status: {order.Status}."
            });
        }

        var problemOrders = db.ProductionOrders.AsNoTracking()
            .Where(x => x.PlannedDate.Date == today && x.Status == "Problem")
            .OrderBy(x => x.OrderNumber)
            .ToList();

        foreach (var order in problemOrders)
        {
            Issues.Add(new DashboardIssue
            {
                Severity = "Rot",
                Title = $"Auftragsproblem · {order.OrderNumber}",
                Message = string.IsNullOrWhiteSpace(order.Comment)
                    ? $"{order.Product} ist als Problem markiert."
                    : order.Comment!
            });
        }
    }

    private void BuildOrders(List<ProductionOrder> orders, List<ProductionOrderCoverageRow> coverage)
    {
        TodayOrders.Clear();
        var coverageByOrder = coverage.ToDictionary(x => x.OrderId);

        foreach (var order in orders)
        {
            coverageByOrder.TryGetValue(order.Id, out var orderCoverage);
            TodayOrders.Add(new DashboardOrderRow
            {
                OrderNumber = order.OrderNumber,
                Product = order.Product,
                WorkstationName = order.Workstation.Name,
                ShiftName = order.Shift?.Name ?? "Individuell",
                Priority = order.Priority,
                Status = order.Status,
                CoverageText = order.Status == "Abgeschlossen"
                    ? "—"
                    : orderCoverage?.CoverageText ?? $"0/{order.RequiredStaff}",
                CoverageStatus = order.Status == "Abgeschlossen"
                    ? "Abgeschlossen"
                    : orderCoverage?.CoverageStatus ?? "Unterbesetzt"
            });
        }
    }

    private void BuildWorkstationStatuses(
        AppDbContext db,
        List<ProductionOrder> orders,
        List<ProductionOrderCoverageRow> coverage)
    {
        WorkstationStatuses.Clear();
        var coverageByOrder = coverage.ToDictionary(x => x.OrderId);
        var workstations = db.Workstations.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();

        foreach (var workstation in workstations)
        {
            var workstationOrders = orders.Where(x => x.WorkstationId == workstation.Id).ToList();
            var openOrders = workstationOrders.Where(x => x.Status != "Abgeschlossen").ToList();
            var uncovered = openOrders.Count(x =>
                coverageByOrder.TryGetValue(x.Id, out var row) && row.CoverageStatus == "Unterbesetzt");
            var problemOrders = workstationOrders.Count(x => x.Status == "Problem");

            var required = openOrders.Sum(x => x.RequiredStaff);
            var planned = openOrders.Sum(x =>
                coverageByOrder.TryGetValue(x.Id, out var row) ? row.PlannedStaff : 0);

            var status = problemOrders > 0
                ? "Problem"
                : uncovered > 0
                    ? "Personal fehlt"
                    : openOrders.Count > 0
                        ? "Bereit"
                        : workstationOrders.Count > 0
                            ? "Abgeschlossen"
                            : "Kein Auftrag";

            WorkstationStatuses.Add(new DashboardWorkstationRow
            {
                WorkstationName = workstation.Name,
                Area = workstation.Area,
                OrderCount = workstationOrders.Count,
                OpenOrderCount = openOrders.Count,
                CoverageText = openOrders.Count == 0 ? "—" : $"{planned}/{required}",
                StatusText = status
            });
        }
    }
}

public class DashboardIssue
{
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class DashboardOrderRow
{
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CoverageText { get; set; } = string.Empty;
    public string CoverageStatus { get; set; } = string.Empty;
}

public class DashboardWorkstationRow
{
    public string WorkstationName { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public int OpenOrderCount { get; set; }
    public string CoverageText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
}
