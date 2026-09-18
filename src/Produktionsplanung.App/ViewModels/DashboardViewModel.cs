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
    [ObservableProperty] private string backupStatusText = string.Empty;
    [ObservableProperty] private string backupStatusSeverity = "OK";

    public string TodayText => DateTime.Today.ToString("dddd, dd.MM.yyyy");

    public DashboardViewModel() => Load();

    [RelayCommand]
    private void Refresh() => Load();

    private void Load()
    {
        var today = DateTime.Today;
        using var db = new AppDbContext();
        ProductionScheduleService.EnsureMissingRunSlots(db);

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

        var runSlots = db.ProductionRunSlots.AsNoTracking()
            .Include(x => x.Shift)
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Workstation)
            .Where(x => x.Date.Date == today)
            .AsEnumerable()
            .OrderBy(x => x.Shift.StartTime)
            .ThenBy(x => x.ProductionOrder.Workstation.Name)
            .ThenBy(x => x.ProductionOrder.OrderNumber)
            .ToList();

        OrdersToday = runSlots.Count;
        RunningOrders = runSlots.Count(x => x.ProductionOrder.Status == "Läuft");
        CompletedOrdersToday = runSlots.Count(x => x.ProductionOrder.Status == "Abgeschlossen");

        var coverage = ProductionOrderCoverageService.Load(today, today);
        UnderstaffedOrders = coverage.Count(x => x.CoverageStatus == "Unterbesetzt");

        var requiredStaff = coverage.Sum(x => x.RequiredStaff);
        var coveredStaff = coverage.Sum(x => Math.Min(x.PlannedStaff, x.RequiredStaff));
        PersonnelCoveragePercent = requiredStaff <= 0
            ? 100
            : (int)Math.Round(coveredStaff * 100.0 / requiredStaff, MidpointRounding.AwayFromZero);

        UpdateBackupStatus();
        BuildIssues(db, today, activeEmployeeIds, absentEmployeeIds, coverage, runSlots);
        BuildOrders(runSlots, coverage);
        BuildWorkstationStatuses(db, runSlots, coverage);

        OpenProblems = Issues.Count;
        LastUpdatedText = $"Aktualisiert: {DateTime.Now:HH:mm}";
    }

    private void UpdateBackupStatus()
    {
        var settings = AppSettingsService.Load();
        if (!settings.LastSuccessfulBackupAtLocal.HasValue)
        {
            BackupStatusSeverity = "Rot";
            BackupStatusText = "Noch kein erfolgreiches Backup protokolliert.";
            return;
        }

        var age = DateTime.Now - settings.LastSuccessfulBackupAtLocal.Value;
        var days = Math.Max(0, (int)Math.Floor(age.TotalDays));
        BackupStatusSeverity = days >= 14 ? "Rot" : days >= 7 ? "Gelb" : "OK";
        BackupStatusText = days == 0
            ? $"Letztes erfolgreiches Backup heute um {settings.LastSuccessfulBackupAtLocal.Value:HH:mm}."
            : $"Letztes erfolgreiches Backup vor {days} Tag(en) am {settings.LastSuccessfulBackupAtLocal.Value:dd.MM.yyyy HH:mm}.";
    }

    private void BuildIssues(
        AppDbContext db,
        DateTime today,
        List<int> activeEmployeeIds,
        List<int> absentEmployeeIds,
        List<ProductionOrderCoverageRow> coverage,
        List<ProductionRunSlot> runSlots)
    {
        Issues.Clear();

        if (BackupStatusSeverity != "OK")
        {
            Issues.Add(new DashboardIssue
            {
                Severity = BackupStatusSeverity,
                Title = BackupStatusSeverity == "Rot" ? "Backup erforderlich" : "Backup prüfen",
                Message = BackupStatusText,
                Route = "Settings"
            });
        }

        foreach (var row in coverage.Where(x => x.CoverageStatus == "Unterbesetzt"))
        {
            Issues.Add(new DashboardIssue
            {
                Severity = "Rot",
                Title = $"Personal fehlt · {row.OrderNumber} · {row.ShiftName}",
                Message = $"{row.WorkstationName}: {row.PlannedStaff}/{row.RequiredStaff} Personen eingeplant ({Math.Abs(row.Difference)} fehlen), Produktionsschicht {row.SequenceNumber}.",
                Route = "DayPlanning",
                Date = today
            });
        }

        var todayAssignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date == today && activeEmployeeIds.Contains(x.EmployeeId))
            .ToList();

        foreach (var assignment in todayAssignments)
        {
            if (absentEmployeeIds.Contains(assignment.EmployeeId))
            {
                Issues.Add(new DashboardIssue
                {
                    Severity = "Rot",
                    Title = "Abwesender Mitarbeiter eingeplant",
                    Message = $"{assignment.Employee.LastName}, {assignment.Employee.FirstName} ist auf {assignment.Workstation.Name} / {assignment.Shift?.Name ?? "Individuell"} eingeplant.",
                    Route = "DayPlanning",
                    Date = today
                });
            }

            var skill = QualificationPlanningService.CheckEmployee(db, assignment.EmployeeId, assignment.WorkstationId);
            if (!skill.IsQualified)
            {
                Issues.Add(new DashboardIssue
                {
                    Severity = "Rot",
                    Title = $"Qualifikation fehlt · {assignment.Employee.LastName}, {assignment.Employee.FirstName}",
                    Message = $"{assignment.Workstation.Name}: {skill.Message}",
                    Route = "DayPlanning",
                    Date = today
                });
            }

            if (!assignment.ShiftId.HasValue ||
                !ProductionScheduleService.IsShiftAllowedOnDate(db, assignment.WorkstationId, assignment.ShiftId.Value, today))
            {
                Issues.Add(new DashboardIssue
                {
                    Severity = "Rot",
                    Title = $"Schicht nicht freigegeben · {assignment.Workstation.Name}",
                    Message = $"{assignment.Employee.LastName}, {assignment.Employee.FirstName}: {assignment.Shift?.Name ?? "Individuell"} ist für diesen Arbeitsplatz/Tag nicht freigegeben.",
                    Route = "DayPlanning",
                    Date = today
                });
            }
        }

        var overdueOrders = db.ProductionOrders.AsNoTracking()
            .Include(x => x.RunSlots)
            .Where(x => x.Status != "Abgeschlossen")
            .AsEnumerable()
            .Where(x => x.RunSlots.Count > 0 && x.RunSlots.Max(s => s.Date.Date) < today)
            .OrderBy(x => x.RunSlots.Max(s => s.Date))
            .Take(8)
            .ToList();

        foreach (var order in overdueOrders)
        {
            var lastDate = order.RunSlots.Max(x => x.Date);
            Issues.Add(new DashboardIssue
            {
                Severity = "Gelb",
                Title = $"Überfälliger Auftrag · {order.OrderNumber}",
                Message = $"Letzte geplante Produktionsschicht war am {lastDate:dd.MM.yyyy}, aktueller Status: {order.Status}.",
                Route = "ProductionOrders",
                EntityId = order.Id
            });
        }

        foreach (var order in runSlots
                     .Select(x => x.ProductionOrder)
                     .Where(x => x.Status == "Problem")
                     .DistinctBy(x => x.Id)
                     .OrderBy(x => x.OrderNumber))
        {
            Issues.Add(new DashboardIssue
            {
                Severity = "Rot",
                Title = $"Auftragsproblem · {order.OrderNumber}",
                Message = string.IsNullOrWhiteSpace(order.Comment)
                    ? $"{order.Product} ist als Problem markiert."
                    : order.Comment!,
                Route = "ProductionOrders",
                EntityId = order.Id
            });
        }
    }

    private void BuildOrders(List<ProductionRunSlot> runSlots, List<ProductionOrderCoverageRow> coverage)
    {
        TodayOrders.Clear();
        var coverageBySlot = coverage
            .Where(x => x.RunSlotId > 0)
            .ToDictionary(x => x.RunSlotId);

        foreach (var slot in runSlots)
        {
            var order = slot.ProductionOrder;
            coverageBySlot.TryGetValue(slot.Id, out var slotCoverage);
            TodayOrders.Add(new DashboardOrderRow
            {
                OrderNumber = order.OrderNumber,
                Product = order.Product,
                WorkstationName = order.Workstation.Name,
                ShiftName = slot.Shift.Name,
                RunText = $"{slot.SequenceNumber}/{Math.Max(1, order.PlannedShiftCount)}",
                Priority = order.Priority,
                Status = order.Status,
                CoverageText = order.Status == "Abgeschlossen"
                    ? "—"
                    : slotCoverage?.CoverageText ?? $"0/{order.RequiredStaff}",
                CoverageStatus = order.Status == "Abgeschlossen"
                    ? "Abgeschlossen"
                    : slotCoverage?.CoverageStatus ?? "Unterbesetzt"
            });
        }
    }

    private void BuildWorkstationStatuses(
        AppDbContext db,
        List<ProductionRunSlot> runSlots,
        List<ProductionOrderCoverageRow> coverage)
    {
        WorkstationStatuses.Clear();
        var coverageBySlot = coverage
            .Where(x => x.RunSlotId > 0)
            .ToDictionary(x => x.RunSlotId);
        var workstations = db.Workstations.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();

        foreach (var workstation in workstations)
        {
            var workstationSlots = runSlots.Where(x => x.ProductionOrder.WorkstationId == workstation.Id).ToList();
            var openSlots = workstationSlots.Where(x => x.ProductionOrder.Status != "Abgeschlossen").ToList();
            var uncovered = openSlots.Count(x =>
                coverageBySlot.TryGetValue(x.Id, out var row) && row.CoverageStatus == "Unterbesetzt");
            var problemSlots = workstationSlots.Count(x => x.ProductionOrder.Status == "Problem");

            var required = openSlots.Sum(x => x.ProductionOrder.RequiredStaff);
            var planned = openSlots.Sum(x =>
                coverageBySlot.TryGetValue(x.Id, out var row) ? row.PlannedStaff : 0);

            var status = problemSlots > 0
                ? "Problem"
                : uncovered > 0
                    ? "Personal fehlt"
                    : openSlots.Count > 0
                        ? "Bereit"
                        : workstationSlots.Count > 0
                            ? "Abgeschlossen"
                            : "Kein Lauf";

            WorkstationStatuses.Add(new DashboardWorkstationRow
            {
                WorkstationName = workstation.Name,
                Area = workstation.Area,
                OrderCount = workstationSlots.Count,
                OpenOrderCount = openSlots.Count,
                CoverageText = openSlots.Count == 0 ? "—" : $"{planned}/{required}",
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
    public string Route { get; set; } = "Dashboard";
    public int? EntityId { get; set; }
    public DateTime? Date { get; set; }
}

public class DashboardOrderRow
{
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string RunText { get; set; } = string.Empty;
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
