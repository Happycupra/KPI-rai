using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class EmployeeQuickCardViewModel : ObservableObject
{
    public int EmployeeId { get; }
    public DateTime ContextDate { get; }
    public DateTime WeekStart { get; }
    public DateTime WeekEnd => WeekStart.AddDays(6);

    public ObservableCollection<EmployeeQuickSkillRow> Skills { get; } = new();
    public ObservableCollection<EmployeeQuickAssignmentRow> Assignments { get; } = new();

    [ObservableProperty] private string fullName = string.Empty;
    [ObservableProperty] private string initials = string.Empty;
    [ObservableProperty] private string personnelNumber = string.Empty;
    [ObservableProperty] private string role = string.Empty;
    [ObservableProperty] private string department = string.Empty;
    [ObservableProperty] private int workloadPercent;
    [ObservableProperty] private double weeklyTargetHours;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string statusBrush = "#16A34A";
    [ObservableProperty] private string contextDateText = string.Empty;
    [ObservableProperty] private string nextAbsenceText = "Keine bevorstehende Abwesenheit erfasst";
    [ObservableProperty] private double targetHours;
    [ObservableProperty] private double plannedHours;
    [ObservableProperty] private double actualHours;
    [ObservableProperty] private double balanceHours;

    public string WeekText => $"KW {System.Globalization.ISOWeek.GetWeekOfYear(WeekStart)} · {WeekStart:dd.MM.}–{WeekEnd:dd.MM.}";
    public string TargetHoursText => $"{TargetHours:0.0} h";
    public string PlannedHoursText => $"{PlannedHours:0.0} h";
    public string ActualHoursText => $"{ActualHours:0.0} h";
    public string BalanceHoursText => $"{BalanceHours:+0.0;-0.0;0.0} h";

    public EmployeeQuickCardViewModel(int employeeId, DateTime? contextDate = null)
    {
        EmployeeId = employeeId;
        ContextDate = (contextDate ?? DateTime.Today).Date;
        WeekStart = GetMonday(ContextDate);
        Load();
    }

    public void Refresh() => Load();

    private void Load()
    {
        using var db = new AppDbContext();
        var employee = db.Employees.AsNoTracking()
            .Include(x => x.Qualifications)
                .ThenInclude(x => x.Qualification)
            .FirstOrDefault(x => x.Id == EmployeeId);

        if (employee is null)
        {
            FullName = "Mitarbeiter nicht gefunden";
            StatusText = "Datensatz nicht mehr vorhanden";
            StatusBrush = "#DC2626";
            return;
        }

        FullName = $"{employee.FirstName} {employee.LastName}";
        Initials = EmployeeInitialsService.Build3(employee.FirstName, employee.LastName);
        PersonnelNumber = employee.PersonnelNumber;
        Role = employee.Role;
        Department = employee.Department;
        WorkloadPercent = employee.WorkloadPercent;
        WeeklyTargetHours = employee.WeeklyTargetHours;
        IsActive = employee.IsActive;
        ContextDateText = ContextDate.ToString("dddd, dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-CH"));

        Skills.Clear();
        foreach (var qualification in employee.Qualifications.OrderByDescending(x => x.Level).ThenBy(x => x.Qualification.Name))
        {
            Skills.Add(new EmployeeQuickSkillRow
            {
                Name = qualification.Qualification.Name,
                Level = qualification.Level,
                LevelText = $"Level {qualification.Level}"
            });
        }

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.EmployeeId == EmployeeId && x.Date.Date >= WeekStart && x.Date.Date <= WeekEnd)
            .AsEnumerable()
            .OrderBy(x => x.Date)
            .ThenBy(x => x.StartTime)
            .ToList();

        Assignments.Clear();
        foreach (var assignment in assignments)
        {
            Assignments.Add(new EmployeeQuickAssignmentRow
            {
                Date = assignment.Date.Date,
                DateText = assignment.Date.ToString("ddd dd.MM.", System.Globalization.CultureInfo.GetCultureInfo("de-CH")),
                Workstation = assignment.Workstation.Name,
                Shift = assignment.Shift?.Name ?? "Individuell",
                TimeText = $"{assignment.StartTime:hh\\:mm}–{assignment.EndTime:hh\\:mm}"
            });
        }

        PlannedHours = assignments.Sum(x => OperatingCalendarService.CalculateNetHours(x.StartTime, x.EndTime, x.BreakMinutes));

        var actualEntries = db.WorkTimeEntries.AsNoTracking()
            .Where(x => x.EmployeeId == EmployeeId && x.Date.Date >= WeekStart && x.Date.Date <= WeekEnd)
            .ToList();
        ActualHours = actualEntries.Sum(x => OperatingCalendarService.CalculateNetHours(x.StartTime, x.EndTime, x.BreakMinutes));

        var calendar = db.OperatingCalendarDays.AsNoTracking()
            .Where(x => x.Date.Date >= WeekStart && x.Date.Date <= WeekEnd)
            .ToList()
            .ToDictionary(x => x.Date.Date);
        TargetHours = OperatingCalendarService.GetTargetHours(employee, WeekStart, WeekEnd, calendar);
        BalanceHours = ActualHours - TargetHours;

        var currentAbsence = db.Absences.AsNoTracking()
            .FirstOrDefault(x => x.EmployeeId == EmployeeId && x.StartDate.Date <= ContextDate && x.EndDate.Date >= ContextDate);
        var nextAbsence = db.Absences.AsNoTracking()
            .Where(x => x.EmployeeId == EmployeeId && x.EndDate.Date >= ContextDate)
            .OrderBy(x => x.StartDate)
            .FirstOrDefault();

        NextAbsenceText = nextAbsence is null
            ? "Keine bevorstehende Abwesenheit erfasst"
            : $"{nextAbsence.Type} · {nextAbsence.StartDate:dd.MM.}–{nextAbsence.EndDate:dd.MM.yyyy}";

        if (!employee.IsActive)
        {
            StatusText = "Inaktiv";
            StatusBrush = "#64748B";
        }
        else if (currentAbsence is not null)
        {
            StatusText = $"Abwesend · {currentAbsence.Type}";
            StatusBrush = "#DC2626";
        }
        else if (assignments.Any(x => x.Date.Date == ContextDate))
        {
            var todayAssignments = assignments.Where(x => x.Date.Date == ContextDate).ToList();
            StatusText = $"Eingeplant · {string.Join(", ", todayAssignments.Select(x => x.Workstation.Name).Distinct())}";
            StatusBrush = "#2563EB";
        }
        else
        {
            StatusText = "Verfügbar";
            StatusBrush = "#16A34A";
        }

        OnPropertyChanged(nameof(WeekText));
        OnPropertyChanged(nameof(TargetHoursText));
        OnPropertyChanged(nameof(PlannedHoursText));
        OnPropertyChanged(nameof(ActualHoursText));
        OnPropertyChanged(nameof(BalanceHoursText));
    }

    private static DateTime GetMonday(DateTime date)
    {
        var days = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-days);
    }

}

public sealed class EmployeeQuickSkillRow
{
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public string LevelText { get; set; } = string.Empty;
}

public sealed class EmployeeQuickAssignmentRow
{
    public DateTime Date { get; set; }
    public string DateText { get; set; } = string.Empty;
    public string Workstation { get; set; } = string.Empty;
    public string Shift { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
}
