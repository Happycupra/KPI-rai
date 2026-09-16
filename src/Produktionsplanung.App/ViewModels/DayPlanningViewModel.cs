using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.ViewModels;

public partial class DayPlanningViewModel : ObservableObject
{
    public ObservableCollection<EmployeeOption> Employees { get; } = new();
    public ObservableCollection<WorkstationOption> Workstations { get; } = new();
    public ObservableCollection<ShiftOption> Shifts { get; } = new();
    public ObservableCollection<DayAssignmentRow> Assignments { get; } = new();
    public ObservableCollection<StaffingRow> Staffing { get; } = new();
    public ObservableCollection<PlanningAlert> Alerts { get; } = new();

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;
    [ObservableProperty] private DayAssignmentRow? selectedAssignment;
    [ObservableProperty] private EmployeeOption? selectedEmployee;
    [ObservableProperty] private WorkstationOption? selectedWorkstation;
    [ObservableProperty] private ShiftOption? selectedShift;
    [ObservableProperty] private string comment = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    private bool initialized;

    public DayPlanningViewModel()
    {
        LoadReferenceData();
        initialized = true;
        LoadDay();
    }

    public string SelectedDateText => SelectedDate.ToString("dddd, dd.MM.yyyy");

    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(SelectedDateText));
        if (initialized)
            LoadDay();
    }

    partial void OnSelectedAssignmentChanged(DayAssignmentRow? value)
    {
        if (value is null) return;

        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == value.EmployeeId);
        SelectedWorkstation = Workstations.FirstOrDefault(x => x.Id == value.WorkstationId);
        SelectedShift = Shifts.FirstOrDefault(x => x.Id == value.ShiftId);
        Comment = value.Comment ?? string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void PreviousDay() => SelectedDate = SelectedDate.AddDays(-1);

    [RelayCommand]
    private void Today() => SelectedDate = DateTime.Today;

    [RelayCommand]
    private void NextDay() => SelectedDate = SelectedDate.AddDays(1);

    [RelayCommand]
    private void Refresh()
    {
        LoadReferenceData();
        LoadDay(SelectedAssignment?.Id);
        StatusMessage = "Planung aktualisiert.";
    }

    [RelayCommand]
    private void NewAssignment()
    {
        SelectedAssignment = null;
        ResetEditor();
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedEmployee is null)
        {
            StatusMessage = "Bitte einen Mitarbeiter auswählen.";
            return;
        }

        if (SelectedWorkstation is null)
        {
            StatusMessage = "Bitte einen Arbeitsplatz auswählen.";
            return;
        }

        if (SelectedShift is null)
        {
            StatusMessage = "Bitte eine Schicht auswählen.";
            return;
        }

        var plannedStart = SelectedDate.Date + SelectedShift.StartTime;
        var plannedEnd = SelectedDate.Date + SelectedShift.EndTime;
        if (plannedEnd <= plannedStart)
            plannedEnd = plannedEnd.AddDays(1);

        using var db = new AppDbContext();
        var employeeId = SelectedEmployee.Id;
        var editingId = SelectedAssignment?.Id;

        var absenceConflict = db.Absences.AsNoTracking().Any(x =>
            x.EmployeeId == employeeId &&
            x.StartDate.Date <= plannedEnd.Date &&
            x.EndDate.Date >= plannedStart.Date);

        if (absenceConflict)
        {
            StatusMessage = $"{SelectedEmployee.DisplayName} ist im gewählten Zeitraum als abwesend erfasst.";
            return;
        }

        var candidateAssignments = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId &&
                        (!editingId.HasValue || x.Id != editingId.Value) &&
                        x.Date.Date >= SelectedDate.AddDays(-1).Date &&
                        x.Date.Date <= SelectedDate.AddDays(1).Date)
            .ToList();

        var overlapping = candidateAssignments.FirstOrDefault(x =>
        {
            var (existingStart, existingEnd) = GetInterval(x.Date, x.StartTime, x.EndTime);
            return plannedStart < existingEnd && existingStart < plannedEnd;
        });

        if (overlapping is not null)
        {
            StatusMessage = $"Doppelbelegung: {SelectedEmployee.DisplayName} ist in diesem Zeitraum bereits eingeplant.";
            return;
        }

        PlanningAssignment entity;
        if (SelectedAssignment is null)
        {
            entity = new PlanningAssignment();
            db.PlanningAssignments.Add(entity);
        }
        else
        {
            entity = db.PlanningAssignments.First(x => x.Id == SelectedAssignment.Id);
        }

        entity.EmployeeId = SelectedEmployee.Id;
        entity.WorkstationId = SelectedWorkstation.Id;
        entity.ShiftId = SelectedShift.Id;
        entity.Date = SelectedDate.Date;
        entity.StartTime = SelectedShift.StartTime;
        entity.EndTime = SelectedShift.EndTime;
        entity.BreakMinutes = SelectedShift.BreakMinutes;
        entity.Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();

        db.SaveChanges();
        LoadDay(entity.Id);
        StatusMessage = "Zuweisung gespeichert.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedAssignment is null)
        {
            StatusMessage = "Bitte zuerst eine Zuweisung auswählen.";
            return;
        }

        using var db = new AppDbContext();
        var entity = db.PlanningAssignments.FirstOrDefault(x => x.Id == SelectedAssignment.Id);
        if (entity is null)
        {
            StatusMessage = "Die Zuweisung wurde nicht mehr gefunden.";
            LoadDay();
            return;
        }

        db.PlanningAssignments.Remove(entity);
        db.SaveChanges();
        LoadDay();
        StatusMessage = "Zuweisung gelöscht.";
    }

    private void LoadReferenceData()
    {
        using var db = new AppDbContext();

        var selectedEmployeeId = SelectedEmployee?.Id;
        var selectedWorkstationId = SelectedWorkstation?.Id;
        var selectedShiftId = SelectedShift?.Id;

        Employees.Clear();
        foreach (var item in db.Employees.AsNoTracking()
                     .Where(x => x.IsActive)
                     .OrderBy(x => x.LastName)
                     .ThenBy(x => x.FirstName))
        {
            Employees.Add(new EmployeeOption
            {
                Id = item.Id,
                DisplayName = $"{item.LastName}, {item.FirstName}",
                Role = item.Role
            });
        }

        Workstations.Clear();
        foreach (var item in db.Workstations.AsNoTracking()
                     .Where(x => x.IsActive)
                     .OrderBy(x => x.Name))
        {
            Workstations.Add(new WorkstationOption { Id = item.Id, Name = item.Name });
        }

        Shifts.Clear();
        foreach (var item in db.Shifts.AsNoTracking().AsEnumerable().OrderBy(x => x.StartTime).ThenBy(x => x.Name))
        {
            Shifts.Add(new ShiftOption
            {
                Id = item.Id,
                Name = item.Name,
                StartTime = item.StartTime,
                EndTime = item.EndTime,
                BreakMinutes = item.BreakMinutes
            });
        }

        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == selectedEmployeeId) ?? Employees.FirstOrDefault();
        SelectedWorkstation = Workstations.FirstOrDefault(x => x.Id == selectedWorkstationId) ?? Workstations.FirstOrDefault();
        SelectedShift = Shifts.FirstOrDefault(x => x.Id == selectedShiftId) ?? Shifts.FirstOrDefault();
    }

    private void LoadDay(int? selectId = null)
    {
        using var db = new AppDbContext();

        var dayAssignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date == SelectedDate.Date)
            .AsEnumerable() // SQLite cannot order TimeSpan values.
            .OrderBy(x => x.Workstation.Name)
            .ThenBy(x => x.StartTime)
            .ThenBy(x => x.Employee.LastName)
            .ToList();

        Assignments.Clear();
        foreach (var item in dayAssignments)
        {
            Assignments.Add(new DayAssignmentRow
            {
                Id = item.Id,
                EmployeeId = item.EmployeeId,
                WorkstationId = item.WorkstationId,
                ShiftId = item.ShiftId,
                EmployeeName = $"{item.Employee.LastName}, {item.Employee.FirstName}",
                Role = item.Employee.Role,
                WorkstationName = item.Workstation.Name,
                ShiftName = item.Shift?.Name ?? "Individuell",
                TimeText = $"{item.StartTime:hh\\:mm}–{item.EndTime:hh\\:mm}",
                Comment = item.Comment
            });
        }

        BuildStaffing(dayAssignments, db);
        BuildAlerts(dayAssignments, db);

        SelectedAssignment = selectId.HasValue
            ? Assignments.FirstOrDefault(x => x.Id == selectId.Value)
            : null;

        if (SelectedAssignment is null)
            ResetEditor();
    }

    private void BuildStaffing(List<PlanningAssignment> dayAssignments, AppDbContext db)
    {
        Staffing.Clear();

        var workstationMap = db.Workstations.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionary(x => x.Id);

        var groups = dayAssignments
            .Where(x => workstationMap.ContainsKey(x.WorkstationId))
            .GroupBy(x => new { x.WorkstationId, x.ShiftId, x.StartTime, x.EndTime })
            .OrderBy(x => workstationMap[x.Key.WorkstationId].Name)
            .ThenBy(x => x.Key.StartTime);

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

            Staffing.Add(new StaffingRow
            {
                WorkstationId = workstation.Id,
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

    private void BuildAlerts(List<PlanningAssignment> dayAssignments, AppDbContext db)
    {
        Alerts.Clear();

        foreach (var row in Staffing)
        {
            if (row.PlannedStaff < row.MinimumStaff)
            {
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Rot",
                    Message = $"{row.WorkstationName} / {row.ShiftName} ist unterbesetzt: {row.PlannedStaff}/{row.MinimumStaff} Mindestbesetzung."
                });
            }
            else if (row.PlannedStaff > row.MaximumStaff)
            {
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Gelb",
                    Message = $"{row.WorkstationName} / {row.ShiftName} ist überbesetzt: {row.PlannedStaff}/{row.MaximumStaff} maximal vorgesehen."
                });
            }
        }

        if (dayAssignments.Count == 0)
        {
            Alerts.Add(new PlanningAlert
            {
                Severity = "Hinweis",
                Message = "Für diesen Tag sind noch keine Mitarbeiter eingeplant."
            });
            return;
        }

        var employeeIds = dayAssignments.Select(x => x.EmployeeId).Distinct().ToList();
        var absences = db.Absences.AsNoTracking()
            .Include(x => x.Employee)
            .Where(x => employeeIds.Contains(x.EmployeeId) &&
                        x.StartDate.Date <= SelectedDate.AddDays(1).Date &&
                        x.EndDate.Date >= SelectedDate.Date)
            .ToList();

        foreach (var assignment in dayAssignments)
        {
            var (start, end) = GetInterval(assignment.Date, assignment.StartTime, assignment.EndTime);
            var absence = absences.FirstOrDefault(x =>
                x.EmployeeId == assignment.EmployeeId &&
                x.StartDate.Date <= end.Date &&
                x.EndDate.Date >= start.Date);

            if (absence is not null)
            {
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Rot",
                    Message = $"{assignment.Employee.LastName}, {assignment.Employee.FirstName} ist eingeplant, aber als {absence.Type} abwesend."
                });
            }
        }

        var windowStart = SelectedDate.AddDays(-1).Date;
        var windowEnd = SelectedDate.AddDays(1).Date;
        var windowAssignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Where(x => employeeIds.Contains(x.EmployeeId) &&
                        x.Date.Date >= windowStart && x.Date.Date <= windowEnd)
            .ToList();

        foreach (var group in windowAssignments.GroupBy(x => x.EmployeeId))
        {
            var items = group.OrderBy(x => x.Date).ThenBy(x => x.StartTime).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var (startA, endA) = GetInterval(items[i].Date, items[i].StartTime, items[i].EndTime);
                for (var j = i + 1; j < items.Count; j++)
                {
                    var (startB, endB) = GetInterval(items[j].Date, items[j].StartTime, items[j].EndTime);
                    if (startA >= endB || startB >= endA) continue;
                    if (items[i].Date.Date != SelectedDate.Date && items[j].Date.Date != SelectedDate.Date) continue;

                    Alerts.Add(new PlanningAlert
                    {
                        Severity = "Rot",
                        Message = $"Doppelbelegung: {items[i].Employee.LastName}, {items[i].Employee.FirstName} hat überschneidende Einsätze."
                    });
                    break;
                }
            }
        }
    }

    private void ResetEditor()
    {
        SelectedEmployee ??= Employees.FirstOrDefault();
        SelectedWorkstation ??= Workstations.FirstOrDefault();
        SelectedShift ??= Shifts.FirstOrDefault();
        Comment = string.Empty;
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

public class EmployeeOption
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class WorkstationOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ShiftOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public string DisplayName => $"{Name} ({StartTime:hh\\:mm}–{EndTime:hh\\:mm})";
}

public class DayAssignmentRow
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int WorkstationId { get; set; }
    public int? ShiftId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public string? Comment { get; set; }
}

public class StaffingRow
{
    public int WorkstationId { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public int PlannedStaff { get; set; }
    public int MinimumStaff { get; set; }
    public int OptimalStaff { get; set; }
    public int MaximumStaff { get; set; }
    public string StatusText { get; set; } = string.Empty;
}

public class PlanningAlert
{
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
