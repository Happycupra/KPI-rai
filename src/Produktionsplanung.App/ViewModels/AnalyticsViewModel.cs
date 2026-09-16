using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class AnalyticsViewModel : ObservableObject
{
    public IReadOnlyList<string> PeriodModes { get; } = new[] { "Woche", "Monat" };
    public ObservableCollection<AnalyticsDailyRow> DailyRows { get; } = new();
    public ObservableCollection<EmployeeHoursRow> EmployeeRows { get; } = new();
    public ObservableCollection<WorkstationAnalyticsRow> WorkstationRows { get; } = new();
    public ObservableCollection<OrderStatusAnalyticsRow> OrderStatusRows { get; } = new();

    [ObservableProperty] private string selectedPeriodMode = "Woche";
    [ObservableProperty] private DateTime anchorDate = DateTime.Today;
    [ObservableProperty] private int activeEmployees;
    [ObservableProperty] private int plannedEmployeeCount;
    [ObservableProperty] private int absenceDays;
    [ObservableProperty] private int totalOrders;
    [ObservableProperty] private int openOrders;
    [ObservableProperty] private int completedOrders;
    [ObservableProperty] private int runningOrders;
    [ObservableProperty] private int problemOrders;
    [ObservableProperty] private int understaffedOrders;
    [ObservableProperty] private double targetHours;
    [ObservableProperty] private double plannedHours;
    [ObservableProperty] private double hoursDifference;
    [ObservableProperty] private double absenceRatePercent;
    [ObservableProperty] private double personnelCoveragePercent;
    [ObservableProperty] private double completionRatePercent;
    [ObservableProperty] private string statusMessage = string.Empty;

    private bool initialized;

    public AnalyticsViewModel()
    {
        initialized = true;
        Load();
    }

    public string PeriodText
    {
        get
        {
            var (start, end) = GetPeriod();
            if (SelectedPeriodMode == "Monat")
                return start.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-CH"));

            return $"KW {ISOWeek.GetWeekOfYear(start)} · {start:dd.MM.yyyy}–{end:dd.MM.yyyy}";
        }
    }

    public string PlannedHoursText => $"{PlannedHours:N1} h";
    public string TargetHoursText => $"{TargetHours:N1} h";
    public string HoursDifferenceText => $"{HoursDifference:+0.0;-0.0;0.0} h";
    public string AbsenceRateText => $"{AbsenceRatePercent:N1} %";
    public string PersonnelCoverageText => $"{PersonnelCoveragePercent:N0} %";
    public string CompletionRateText => $"{CompletionRatePercent:N0} %";

    partial void OnSelectedPeriodModeChanged(string value)
    {
        OnPropertyChanged(nameof(PeriodText));
        if (initialized) Load();
    }

    partial void OnAnchorDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(PeriodText));
        if (initialized) Load();
    }

    [RelayCommand]
    private void PreviousPeriod()
    {
        AnchorDate = SelectedPeriodMode == "Monat" ? AnchorDate.AddMonths(-1) : AnchorDate.AddDays(-7);
    }

    [RelayCommand]
    private void CurrentPeriod() => AnchorDate = DateTime.Today;

    [RelayCommand]
    private void NextPeriod()
    {
        AnchorDate = SelectedPeriodMode == "Monat" ? AnchorDate.AddMonths(1) : AnchorDate.AddDays(7);
    }

    [RelayCommand]
    private void Refresh()
    {
        Load();
        StatusMessage = $"Auswertung aktualisiert: {DateTime.Now:HH:mm}";
    }

    private void Load()
    {
        var (start, end) = GetPeriod();
        using var db = new AppDbContext();

        var employees = db.Employees.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToList();

        var employeeIds = employees.Select(x => x.Id).ToHashSet();
        var assignments = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date >= start && x.Date.Date <= end)
            .ToList();

        var absences = db.Absences.AsNoTracking()
            .Where(x => x.StartDate.Date <= end && x.EndDate.Date >= start)
            .ToList();

        var orders = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.PlannedDate.Date >= start && x.PlannedDate.Date <= end)
            .ToList();

        var coverageRows = ProductionOrderCoverageService.Load(start, end);
        var workstations = db.Workstations.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();

        ActiveEmployees = employees.Count;
        PlannedEmployeeCount = assignments.Where(x => employeeIds.Contains(x.EmployeeId)).Select(x => x.EmployeeId).Distinct().Count();

        var workingDays = CountWorkingDays(start, end);
        TargetHours = employees.Sum(x => x.WeeklyTargetHours / 5.0 * workingDays);
        PlannedHours = assignments.Where(x => employeeIds.Contains(x.EmployeeId)).Sum(CalculateHours);
        HoursDifference = PlannedHours - TargetHours;

        var absenceSet = BuildAbsenceDaySet(absences, employeeIds, start, end);
        AbsenceDays = absenceSet.Count;
        var possibleEmployeeDays = ActiveEmployees * workingDays;
        AbsenceRatePercent = possibleEmployeeDays > 0 ? AbsenceDays * 100.0 / possibleEmployeeDays : 0;

        TotalOrders = orders.Count;
        CompletedOrders = orders.Count(x => x.Status == "Abgeschlossen");
        RunningOrders = orders.Count(x => x.Status == "Läuft");
        ProblemOrders = orders.Count(x => x.Status == "Problem");
        OpenOrders = TotalOrders - CompletedOrders;
        CompletionRatePercent = TotalOrders > 0 ? CompletedOrders * 100.0 / TotalOrders : 0;

        UnderstaffedOrders = coverageRows.Count(x => x.Difference < 0);
        var requiredStaff = coverageRows.Sum(x => x.RequiredStaff);
        var coveredStaff = coverageRows.Sum(x => Math.Min(x.PlannedStaff, x.RequiredStaff));
        PersonnelCoveragePercent = requiredStaff > 0 ? coveredStaff * 100.0 / requiredStaff : 100;

        BuildEmployeeRows(employees, assignments, absenceSet, workingDays);
        BuildDailyRows(start, end, assignments, absences, orders, coverageRows, employeeIds);
        BuildWorkstationRows(workstations, assignments, orders, coverageRows);
        BuildStatusRows(orders);

        OnPropertyChanged(nameof(PeriodText));
        OnPropertyChanged(nameof(PlannedHoursText));
        OnPropertyChanged(nameof(TargetHoursText));
        OnPropertyChanged(nameof(HoursDifferenceText));
        OnPropertyChanged(nameof(AbsenceRateText));
        OnPropertyChanged(nameof(PersonnelCoverageText));
        OnPropertyChanged(nameof(CompletionRateText));
    }

    private void BuildEmployeeRows(
        List<Employee> employees,
        List<PlanningAssignment> assignments,
        HashSet<(int EmployeeId, DateTime Day)> absenceSet,
        int workingDays)
    {
        EmployeeRows.Clear();
        foreach (var employee in employees)
        {
            var target = employee.WeeklyTargetHours / 5.0 * workingDays;
            var planned = assignments.Where(x => x.EmployeeId == employee.Id).Sum(CalculateHours);
            var absenceDays = absenceSet.Count(x => x.EmployeeId == employee.Id);
            var coverage = target > 0 ? planned * 100.0 / target : 0;

            EmployeeRows.Add(new EmployeeHoursRow
            {
                EmployeeName = $"{employee.LastName}, {employee.FirstName}",
                Role = employee.Role,
                TargetHours = target,
                PlannedHours = planned,
                DifferenceHours = planned - target,
                AbsenceDays = absenceDays,
                HoursCoveragePercent = Math.Clamp(coverage, 0, 100)
            });
        }
    }

    private void BuildDailyRows(
        DateTime start,
        DateTime end,
        List<PlanningAssignment> assignments,
        List<Absence> absences,
        List<ProductionOrder> orders,
        List<ProductionOrderCoverageRow> coverageRows,
        HashSet<int> employeeIds)
    {
        var temp = new List<AnalyticsDailyRow>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var date = day.Date;
            var dayAssignments = assignments.Where(x => x.Date.Date == date && employeeIds.Contains(x.EmployeeId)).ToList();
            var dayOrders = orders.Where(x => x.PlannedDate.Date == date).ToList();
            var dayCoverage = coverageRows.Where(x => x.Date.Date == date).ToList();
            var required = dayCoverage.Sum(x => x.RequiredStaff);
            var covered = dayCoverage.Sum(x => Math.Min(x.PlannedStaff, x.RequiredStaff));
            var coverage = required > 0 ? covered * 100.0 / required : 100;
            var absent = absences
                .Where(x => employeeIds.Contains(x.EmployeeId) && x.StartDate.Date <= date && x.EndDate.Date >= date)
                .Select(x => x.EmployeeId)
                .Distinct()
                .Count();

            temp.Add(new AnalyticsDailyRow
            {
                Date = date,
                DayText = date.ToString("ddd dd.MM.", CultureInfo.GetCultureInfo("de-CH")),
                PlannedEmployees = dayAssignments.Select(x => x.EmployeeId).Distinct().Count(),
                PlannedHours = dayAssignments.Sum(CalculateHours),
                AbsentEmployees = absent,
                Orders = dayOrders.Count,
                CompletedOrders = dayOrders.Count(x => x.Status == "Abgeschlossen"),
                PersonnelCoveragePercent = coverage
            });
        }

        var maxHours = temp.Count == 0 ? 0 : temp.Max(x => x.PlannedHours);
        DailyRows.Clear();
        foreach (var row in temp)
        {
            row.HoursChartPercent = maxHours > 0 ? row.PlannedHours * 100.0 / maxHours : 0;
            DailyRows.Add(row);
        }
    }

    private void BuildWorkstationRows(
        List<Workstation> workstations,
        List<PlanningAssignment> assignments,
        List<ProductionOrder> orders,
        List<ProductionOrderCoverageRow> coverageRows)
    {
        WorkstationRows.Clear();
        foreach (var workstation in workstations)
        {
            var workstationOrders = orders.Where(x => x.WorkstationId == workstation.Id).ToList();
            var workstationCoverage = coverageRows.Where(x => x.WorkstationId == workstation.Id).ToList();
            var required = workstationCoverage.Sum(x => x.RequiredStaff);
            var covered = workstationCoverage.Sum(x => Math.Min(x.PlannedStaff, x.RequiredStaff));
            var coverage = required > 0 ? covered * 100.0 / required : 100;
            var plannedHours = assignments.Where(x => x.WorkstationId == workstation.Id).Sum(CalculateHours);

            WorkstationRows.Add(new WorkstationAnalyticsRow
            {
                WorkstationName = workstation.Name,
                Area = workstation.Area,
                Orders = workstationOrders.Count,
                CompletedOrders = workstationOrders.Count(x => x.Status == "Abgeschlossen"),
                RequiredStaff = required,
                CoveredStaff = covered,
                PersonnelCoveragePercent = coverage,
                PlannedHours = plannedHours,
                StatusText = workstationOrders.Count == 0
                    ? "Keine Aufträge"
                    : coverage < 100
                        ? "Unterbesetzt"
                        : "OK"
            });
        }
    }

    private void BuildStatusRows(List<ProductionOrder> orders)
    {
        OrderStatusRows.Clear();
        var statuses = new[] { "Geplant", "Bereit", "Läuft", "Pausiert", "Abgeschlossen", "Problem" };
        foreach (var status in statuses)
        {
            var count = orders.Count(x => x.Status == status);
            var percent = orders.Count > 0 ? count * 100.0 / orders.Count : 0;
            OrderStatusRows.Add(new OrderStatusAnalyticsRow
            {
                Status = status,
                Count = count,
                Percent = percent
            });
        }
    }

    private (DateTime Start, DateTime End) GetPeriod()
    {
        if (SelectedPeriodMode == "Monat")
        {
            var start = new DateTime(AnchorDate.Year, AnchorDate.Month, 1);
            return (start, start.AddMonths(1).AddDays(-1));
        }

        var diff = ((int)AnchorDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = AnchorDate.Date.AddDays(-diff);
        return (monday, monday.AddDays(6));
    }

    private static int CountWorkingDays(DateTime start, DateTime end)
    {
        var count = 0;
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;
        }
        return count;
    }

    private static HashSet<(int EmployeeId, DateTime Day)> BuildAbsenceDaySet(
        List<Absence> absences,
        HashSet<int> employeeIds,
        DateTime start,
        DateTime end)
    {
        var set = new HashSet<(int EmployeeId, DateTime Day)>();
        foreach (var absence in absences.Where(x => employeeIds.Contains(x.EmployeeId)))
        {
            var from = absence.StartDate.Date < start ? start : absence.StartDate.Date;
            var to = absence.EndDate.Date > end ? end : absence.EndDate.Date;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                set.Add((absence.EmployeeId, day));
            }
        }
        return set;
    }

    private static double CalculateHours(PlanningAssignment assignment)
    {
        var start = assignment.Date.Date + assignment.StartTime;
        var end = assignment.Date.Date + assignment.EndTime;
        if (end <= start) end = end.AddDays(1);
        return Math.Max(0, (end - start).TotalHours - assignment.BreakMinutes / 60.0);
    }
}

public class AnalyticsDailyRow
{
    public DateTime Date { get; set; }
    public string DayText { get; set; } = string.Empty;
    public int PlannedEmployees { get; set; }
    public double PlannedHours { get; set; }
    public int AbsentEmployees { get; set; }
    public int Orders { get; set; }
    public int CompletedOrders { get; set; }
    public double PersonnelCoveragePercent { get; set; }
    public double HoursChartPercent { get; set; }
    public string PlannedHoursText => $"{PlannedHours:N1} h";
    public string CoverageText => $"{PersonnelCoveragePercent:N0} %";
}

public class EmployeeHoursRow
{
    public string EmployeeName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public double TargetHours { get; set; }
    public double PlannedHours { get; set; }
    public double DifferenceHours { get; set; }
    public int AbsenceDays { get; set; }
    public double HoursCoveragePercent { get; set; }
    public string TargetHoursText => $"{TargetHours:N1} h";
    public string PlannedHoursText => $"{PlannedHours:N1} h";
    public string DifferenceText => $"{DifferenceHours:+0.0;-0.0;0.0} h";
}

public class WorkstationAnalyticsRow
{
    public string WorkstationName { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public int Orders { get; set; }
    public int CompletedOrders { get; set; }
    public int RequiredStaff { get; set; }
    public int CoveredStaff { get; set; }
    public double PersonnelCoveragePercent { get; set; }
    public double PlannedHours { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string CoverageText => $"{PersonnelCoveragePercent:N0} %";
    public string PlannedHoursText => $"{PlannedHours:N1} h";
}

public class OrderStatusAnalyticsRow
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percent { get; set; }
    public string PercentText => $"{Percent:N0} %";
}
