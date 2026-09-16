using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.ViewModels;

public partial class WeekPlanningViewModel : ObservableObject
{
    public ObservableCollection<EmployeeWeekRow> EmployeeRows { get; } = new();
    public ObservableCollection<WeekStaffingRow> StaffingRows { get; } = new();
    public ObservableCollection<PlanningAlert> Alerts { get; } = new();

    [ObservableProperty] private DateTime weekStart = GetMonday(DateTime.Today);
    [ObservableProperty] private string statusMessage = string.Empty;

    private bool initialized;

    public WeekPlanningViewModel()
    {
        initialized = true;
        LoadWeek();
    }

    public string WeekRangeText
    {
        get
        {
            var weekEnd = WeekStart.AddDays(6);
            var weekNumber = ISOWeek.GetWeekOfYear(WeekStart);
            return $"KW {weekNumber} · {WeekStart:dd.MM.yyyy}–{weekEnd:dd.MM.yyyy}";
        }
    }

    public string MondayHeader => $"Mo\n{WeekStart:dd.MM.}";
    public string TuesdayHeader => $"Di\n{WeekStart.AddDays(1):dd.MM.}";
    public string WednesdayHeader => $"Mi\n{WeekStart.AddDays(2):dd.MM.}";
    public string ThursdayHeader => $"Do\n{WeekStart.AddDays(3):dd.MM.}";
    public string FridayHeader => $"Fr\n{WeekStart.AddDays(4):dd.MM.}";
    public string SaturdayHeader => $"Sa\n{WeekStart.AddDays(5):dd.MM.}";
    public string SundayHeader => $"So\n{WeekStart.AddDays(6):dd.MM.}";

    partial void OnWeekStartChanged(DateTime value)
    {
        var monday = GetMonday(value);
        if (value.Date != monday)
        {
            WeekStart = monday;
            return;
        }

        OnPropertyChanged(nameof(WeekRangeText));
        OnPropertyChanged(nameof(MondayHeader));
        OnPropertyChanged(nameof(TuesdayHeader));
        OnPropertyChanged(nameof(WednesdayHeader));
        OnPropertyChanged(nameof(ThursdayHeader));
        OnPropertyChanged(nameof(FridayHeader));
        OnPropertyChanged(nameof(SaturdayHeader));
        OnPropertyChanged(nameof(SundayHeader));

        if (initialized)
            LoadWeek();
    }

    [RelayCommand]
    private void PreviousWeek() => WeekStart = WeekStart.AddDays(-7);

    [RelayCommand]
    private void CurrentWeek() => WeekStart = GetMonday(DateTime.Today);

    [RelayCommand]
    private void NextWeek() => WeekStart = WeekStart.AddDays(7);

    [RelayCommand]
    private void Refresh()
    {
        LoadWeek();
        StatusMessage = "Wochenplanung aktualisiert.";
    }

    [RelayCommand]
    private void CopyPreviousWeek()
    {
        using var db = new AppDbContext();
        var targetStart = WeekStart.Date;
        var targetEnd = targetStart.AddDays(6);

        var targetHasPlanning = db.PlanningAssignments.AsNoTracking()
            .Any(x => x.Date.Date >= targetStart && x.Date.Date <= targetEnd);

        if (targetHasPlanning)
        {
            StatusMessage = "Die Zielwoche enthält bereits Planungen. Kopieren wurde abgebrochen, damit keine Doppelbelegungen entstehen.";
            return;
        }

        var sourceStart = targetStart.AddDays(-7);
        var sourceEnd = targetEnd.AddDays(-7);
        var source = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date >= sourceStart && x.Date.Date <= sourceEnd)
            .AsEnumerable() // SQLite cannot order TimeSpan values.
            .OrderBy(x => x.Date)
            .ThenBy(x => x.StartTime)
            .ToList();

        if (source.Count == 0)
        {
            StatusMessage = "In der Vorwoche gibt es keine Planungen zum Kopieren.";
            return;
        }

        var activeEmployeeIds = db.Employees.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToHashSet();

        var activeWorkstationIds = db.Workstations.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToHashSet();

        var absences = db.Absences.AsNoTracking()
            .Where(x => x.StartDate.Date <= targetEnd.AddDays(1) && x.EndDate.Date >= targetStart)
            .ToList();

        var copied = 0;
        var skipped = 0;

        foreach (var item in source)
        {
            var targetDate = item.Date.Date.AddDays(7);
            if (!activeEmployeeIds.Contains(item.EmployeeId) || !activeWorkstationIds.Contains(item.WorkstationId))
            {
                skipped++;
                continue;
            }

            var (plannedStart, plannedEnd) = GetInterval(targetDate, item.StartTime, item.EndTime);
            var absent = absences.Any(x =>
                x.EmployeeId == item.EmployeeId &&
                x.StartDate.Date <= plannedEnd.Date &&
                x.EndDate.Date >= plannedStart.Date);

            if (absent)
            {
                skipped++;
                continue;
            }

            db.PlanningAssignments.Add(new PlanningAssignment
            {
                EmployeeId = item.EmployeeId,
                WorkstationId = item.WorkstationId,
                ShiftId = item.ShiftId,
                Date = targetDate,
                StartTime = item.StartTime,
                EndTime = item.EndTime,
                BreakMinutes = item.BreakMinutes,
                Comment = item.Comment
            });
            copied++;
        }

        db.SaveChanges();
        LoadWeek();
        StatusMessage = skipped == 0
            ? $"Vorwoche erfolgreich kopiert: {copied} Einsätze."
            : $"Vorwoche kopiert: {copied} Einsätze; {skipped} wegen Abwesenheit oder inaktiven Stammdaten übersprungen.";
    }

    private void LoadWeek()
    {
        using var db = new AppDbContext();
        var weekEnd = WeekStart.AddDays(6).Date;

        var employees = db.Employees.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToList();

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date >= WeekStart.Date && x.Date.Date <= weekEnd)
            .AsEnumerable() // SQLite cannot order TimeSpan values.
            .OrderBy(x => x.Date)
            .ThenBy(x => x.StartTime)
            .ToList();

        var absences = db.Absences.AsNoTracking()
            .Where(x => x.StartDate.Date <= weekEnd.AddDays(1) && x.EndDate.Date >= WeekStart.Date)
            .ToList();

        EmployeeRows.Clear();
        foreach (var employee in employees)
        {
            EmployeeRows.Add(new EmployeeWeekRow
            {
                EmployeeId = employee.Id,
                EmployeeName = $"{employee.LastName}, {employee.FirstName}",
                Role = employee.Role,
                Monday = BuildDayCell(employee.Id, WeekStart, assignments, absences),
                Tuesday = BuildDayCell(employee.Id, WeekStart.AddDays(1), assignments, absences),
                Wednesday = BuildDayCell(employee.Id, WeekStart.AddDays(2), assignments, absences),
                Thursday = BuildDayCell(employee.Id, WeekStart.AddDays(3), assignments, absences),
                Friday = BuildDayCell(employee.Id, WeekStart.AddDays(4), assignments, absences),
                Saturday = BuildDayCell(employee.Id, WeekStart.AddDays(5), assignments, absences),
                Sunday = BuildDayCell(employee.Id, WeekStart.AddDays(6), assignments, absences)
            });
        }

        BuildStaffing(assignments, db);
        BuildAlerts(assignments, absences);
    }

    private static string BuildDayCell(
        int employeeId,
        DateTime day,
        List<PlanningAssignment> assignments,
        List<Absence> absences)
    {
        var dayAssignments = assignments
            .Where(x => x.EmployeeId == employeeId && x.Date.Date == day.Date)
            .OrderBy(x => x.StartTime)
            .ToList();

        var absence = absences.FirstOrDefault(x =>
            x.EmployeeId == employeeId &&
            x.StartDate.Date <= day.Date &&
            x.EndDate.Date >= day.Date);

        var parts = new List<string>();
        if (absence is not null)
            parts.Add($"ABW: {absence.Type}");

        foreach (var item in dayAssignments)
        {
            var shift = item.Shift?.Name ?? "Individuell";
            parts.Add($"{item.Workstation.Name} · {shift}\n{item.StartTime:hh\\:mm}–{item.EndTime:hh\\:mm}");
        }

        return parts.Count == 0 ? "—" : string.Join("\n", parts);
    }

    private void BuildStaffing(List<PlanningAssignment> assignments, AppDbContext db)
    {
        StaffingRows.Clear();

        var workstationMap = db.Workstations.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionary(x => x.Id);

        var groups = assignments
            .Where(x => workstationMap.ContainsKey(x.WorkstationId))
            .GroupBy(x => new { Day = x.Date.Date, x.WorkstationId, x.ShiftId, x.StartTime, x.EndTime })
            .OrderBy(x => x.Key.Day)
            .ThenBy(x => x.Key.StartTime)
            .ThenBy(x => workstationMap[x.Key.WorkstationId].Name);

        foreach (var group in groups)
        {
            var workstation = workstationMap[group.Key.WorkstationId];
            var first = group.First();
            var planned = group.Select(x => x.EmployeeId).Distinct().Count();
            var status = planned < workstation.MinimumStaff
                ? "Unterbesetzt"
                : planned < workstation.OptimalStaff
                    ? "Knapp besetzt"
                    : planned > workstation.MaximumStaff
                        ? "Überbesetzt"
                        : "OK";

            StaffingRows.Add(new WeekStaffingRow
            {
                Date = group.Key.Day,
                DayText = group.Key.Day.ToString("ddd dd.MM.", CultureInfo.GetCultureInfo("de-CH")),
                WorkstationName = workstation.Name,
                ShiftName = first.Shift?.Name ?? "Individuell",
                TimeText = $"{group.Key.StartTime:hh\\:mm}–{group.Key.EndTime:hh\\:mm}",
                PlannedStaff = planned,
                MinimumStaff = workstation.MinimumStaff,
                OptimalStaff = workstation.OptimalStaff,
                MaximumStaff = workstation.MaximumStaff,
                StatusText = status
            });
        }
    }

    private void BuildAlerts(List<PlanningAssignment> assignments, List<Absence> absences)
    {
        Alerts.Clear();

        foreach (var row in StaffingRows)
        {
            if (row.PlannedStaff < row.MinimumStaff)
            {
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Rot",
                    Message = $"{row.DayText}: {row.WorkstationName} / {row.ShiftName} unterbesetzt ({row.PlannedStaff}/{row.MinimumStaff})."
                });
            }
            else if (row.PlannedStaff > row.MaximumStaff)
            {
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Gelb",
                    Message = $"{row.DayText}: {row.WorkstationName} / {row.ShiftName} überbesetzt ({row.PlannedStaff}/{row.MaximumStaff})."
                });
            }
        }

        foreach (var assignment in assignments)
        {
            var (start, end) = GetInterval(assignment.Date, assignment.StartTime, assignment.EndTime);
            var absence = absences.FirstOrDefault(x =>
                x.EmployeeId == assignment.EmployeeId &&
                x.StartDate.Date <= end.Date &&
                x.EndDate.Date >= start.Date);

            if (absence is null) continue;

            Alerts.Add(new PlanningAlert
            {
                Severity = "Rot",
                Message = $"{assignment.Date:ddd dd.MM.}: {assignment.Employee.LastName}, {assignment.Employee.FirstName} ist eingeplant, aber als {absence.Type} abwesend."
            });
        }

        if (assignments.Count == 0)
        {
            Alerts.Add(new PlanningAlert
            {
                Severity = "Hinweis",
                Message = "Für diese Woche sind noch keine Einsätze geplant."
            });
        }
    }

    private static DateTime GetMonday(DateTime date)
    {
        var day = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-day);
    }

    private static (DateTime Start, DateTime End) GetInterval(DateTime date, TimeSpan startTime, TimeSpan endTime)
    {
        var start = date.Date + startTime;
        var end = date.Date + endTime;
        if (end <= start)
            end = end.AddDays(1);
        return (start, end);
    }
}

public class EmployeeWeekRow
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Monday { get; set; } = string.Empty;
    public string Tuesday { get; set; } = string.Empty;
    public string Wednesday { get; set; } = string.Empty;
    public string Thursday { get; set; } = string.Empty;
    public string Friday { get; set; } = string.Empty;
    public string Saturday { get; set; } = string.Empty;
    public string Sunday { get; set; } = string.Empty;
}

public class WeekStaffingRow
{
    public DateTime Date { get; set; }
    public string DayText { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public int PlannedStaff { get; set; }
    public int MinimumStaff { get; set; }
    public int OptimalStaff { get; set; }
    public int MaximumStaff { get; set; }
    public string StatusText { get; set; } = string.Empty;
}
