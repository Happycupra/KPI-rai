using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

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
        if (!initialized)
            return;

        RefreshAllowedShifts(SelectedShift?.Id);
        LoadDay();
    }

    partial void OnSelectedAssignmentChanged(DayAssignmentRow? value)
    {
        if (value is null)
            return;

        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == value.EmployeeId);
        SelectedWorkstation = Workstations.FirstOrDefault(x => x.Id == value.WorkstationId);
        SelectedShift = Shifts.FirstOrDefault(x => x.Id == value.ShiftId);
        Comment = value.Comment ?? string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand] private void PreviousDay() => SelectedDate = SelectedDate.AddDays(-1);
    [RelayCommand] private void Today() => SelectedDate = DateTime.Today;
    [RelayCommand] private void NextDay() => SelectedDate = SelectedDate.AddDays(1);

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
            StatusMessage = "Bitte eine für diesen Arbeitsplatz und Tag freigegebene Schicht auswählen.";
            return;
        }

        var ps = SelectedDate.Date + SelectedShift.StartTime;
        var pe = SelectedDate.Date + SelectedShift.EndTime;
        if (pe <= ps)
            pe = pe.AddDays(1);

        using var db = new AppDbContext();
        var employeeId = SelectedEmployee.Id;
        var editingId = SelectedAssignment?.Id;

        if (db.Absences.AsNoTracking().Any(x =>
                x.EmployeeId == employeeId &&
                x.StartDate.Date <= pe.Date &&
                x.EndDate.Date >= ps.Date))
        {
            StatusMessage = $"{SelectedEmployee.DisplayName} ist im gewählten Zeitraum als abwesend erfasst.";
            return;
        }

        var candidates = db.PlanningAssignments.AsNoTracking()
            .Where(x =>
                x.EmployeeId == employeeId &&
                (!editingId.HasValue || x.Id != editingId.Value) &&
                x.Date.Date >= SelectedDate.AddDays(-1).Date &&
                x.Date.Date <= SelectedDate.AddDays(1).Date)
            .ToList();

        if (candidates.Any(x =>
            {
                var interval = GetInterval(x.Date, x.StartTime, x.EndTime);
                return ps < interval.End && interval.Start < pe;
            }))
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
        var employeeId = SelectedEmployee?.Id;
        var workstationId = SelectedWorkstation?.Id;
        var shiftId = SelectedShift?.Id;

        Employees.Clear();
        foreach (var employee in db.Employees.AsNoTracking()
                     .Where(x => x.IsActive)
                     .OrderBy(x => x.LastName)
                     .ThenBy(x => x.FirstName))
        {
            Employees.Add(new EmployeeOption
            {
                Id = employee.Id,
                DisplayName = $"{employee.LastName}, {employee.FirstName}",
                ShortName = employee.LastName,
                Initials = BuildEmployeeInitials(employee.FirstName, employee.LastName),
                Role = employee.Role
            });
        }

        Workstations.Clear();
        foreach (var workstation in db.Workstations.AsNoTracking()
                     .Where(x => x.IsActive)
                     .OrderBy(x => x.Name))
        {
            Workstations.Add(new WorkstationOption { Id = workstation.Id, Name = workstation.Name });
        }

        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == employeeId) ?? Employees.FirstOrDefault();
        SelectedWorkstation = Workstations.FirstOrDefault(x => x.Id == workstationId) ?? Workstations.FirstOrDefault();
        RefreshAllowedShifts(shiftId);
    }

    private void RefreshAllowedShifts(int? preferredShiftId = null)
    {
        var desiredShiftId = preferredShiftId ?? SelectedShift?.Id;
        Shifts.Clear();

        if (SelectedWorkstation is null)
        {
            SelectedShift = null;
            return;
        }

        using var db = new AppDbContext();
        var allowed = ProductionScheduleService.LoadAllowedShiftsForDate(db, SelectedWorkstation.Id, SelectedDate);
        foreach (var shift in allowed)
        {
            Shifts.Add(new ShiftOption
            {
                Id = shift.Id,
                Name = shift.Name,
                StartTime = shift.StartTime,
                EndTime = shift.EndTime,
                BreakMinutes = shift.BreakMinutes
            });
        }

        SelectedShift = Shifts.FirstOrDefault(x => x.Id == desiredShiftId) ?? Shifts.FirstOrDefault();
    }

    private void LoadDay(int? selectId = null)
    {
        using var db = new AppDbContext();
        var list = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date == SelectedDate.Date)
            .AsEnumerable()
            .OrderBy(x => x.Workstation.Name)
            .ThenBy(x => x.StartTime)
            .ThenBy(x => x.Employee.LastName)
            .ToList();

        Assignments.Clear();
        foreach (var assignment in list)
        {
            Assignments.Add(new DayAssignmentRow
            {
                Id = assignment.Id,
                EmployeeId = assignment.EmployeeId,
                WorkstationId = assignment.WorkstationId,
                ShiftId = assignment.ShiftId,
                EmployeeName = $"{assignment.Employee.LastName}, {assignment.Employee.FirstName}",
                Role = assignment.Employee.Role,
                WorkstationName = assignment.Workstation.Name,
                ShiftName = assignment.Shift?.Name ?? "Individuell",
                TimeText = $"{assignment.StartTime:hh\\:mm}–{assignment.EndTime:hh\\:mm}",
                Comment = assignment.Comment
            });
        }

        BuildStaffing(list, db);
        BuildAlerts(list, db);
        SelectedAssignment = selectId.HasValue ? Assignments.FirstOrDefault(x => x.Id == selectId) : null;
        if (SelectedAssignment is null)
            ResetEditor();
        RefreshEmployeeSuggestions();
        RefreshSkillAlerts();
    }

    private void BuildStaffing(List<PlanningAssignment> list, AppDbContext db)
    {
        Staffing.Clear();
        var workstations = db.Workstations.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();

        foreach (var workstation in workstations)
        {
            var allowed = ProductionScheduleService.LoadAllowedShiftsForDate(db, workstation.Id, SelectedDate);
            foreach (var shift in allowed)
            {
                var planned = list
                    .Where(x => x.WorkstationId == workstation.Id && x.ShiftId == shift.Id)
                    .Select(x => x.EmployeeId)
                    .Distinct()
                    .Count();

                Staffing.Add(new StaffingRow
                {
                    WorkstationId = workstation.Id,
                    WorkstationName = workstation.Name,
                    ShiftName = shift.Name,
                    TimeText = $"{shift.StartTime:hh\\:mm}–{shift.EndTime:hh\\:mm}",
                    PlannedStaff = planned,
                    MinimumStaff = workstation.MinimumStaff,
                    OptimalStaff = workstation.OptimalStaff,
                    MaximumStaff = workstation.MaximumStaff,
                    StatusText = planned < workstation.MinimumStaff
                        ? "Unterbesetzt"
                        : planned < workstation.OptimalStaff
                            ? "Knapp besetzt"
                            : planned > workstation.MaximumStaff ? "Überbesetzt" : "OK"
                });
            }

            foreach (var group in list
                         .Where(x =>
                             x.WorkstationId == workstation.Id &&
                             (!x.ShiftId.HasValue || !allowed.Any(s => s.Id == x.ShiftId.Value)))
                         .GroupBy(x => new { x.ShiftId, x.StartTime, x.EndTime }))
            {
                var planned = group.Select(x => x.EmployeeId).Distinct().Count();
                Staffing.Add(new StaffingRow
                {
                    WorkstationId = workstation.Id,
                    WorkstationName = workstation.Name,
                    ShiftName = group.First().Shift?.Name ?? "Individuell / nicht freigegeben",
                    TimeText = $"{group.Key.StartTime:hh\\:mm}–{group.Key.EndTime:hh\\:mm}",
                    PlannedStaff = planned,
                    MinimumStaff = workstation.MinimumStaff,
                    OptimalStaff = workstation.OptimalStaff,
                    MaximumStaff = workstation.MaximumStaff,
                    StatusText = "Nicht freigegeben"
                });
            }
        }
    }

    private void BuildAlerts(List<PlanningAssignment> list, AppDbContext db)
    {
        Alerts.Clear();
        foreach (var row in Staffing)
        {
            if (row.PlannedStaff < row.MinimumStaff)
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Rot",
                    Message = $"{row.WorkstationName} / {row.ShiftName} ist unterbesetzt: {row.PlannedStaff}/{row.MinimumStaff} Mindestbesetzung."
                });
            else if (row.PlannedStaff > row.MaximumStaff)
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Gelb",
                    Message = $"{row.WorkstationName} / {row.ShiftName} ist überbesetzt: {row.PlannedStaff}/{row.MaximumStaff}."
                });
        }

        if (list.Count == 0)
            Alerts.Add(new PlanningAlert { Severity = "Hinweis", Message = "Für diesen Tag sind noch keine Mitarbeiter eingeplant." });

        var employeeIds = list.Select(x => x.EmployeeId).Distinct().ToList();
        var absences = db.Absences.AsNoTracking()
            .Where(x =>
                employeeIds.Contains(x.EmployeeId) &&
                x.StartDate.Date <= SelectedDate.AddDays(1) &&
                x.EndDate.Date >= SelectedDate)
            .ToList();

        foreach (var assignment in list)
        {
            var interval = GetInterval(assignment.Date, assignment.StartTime, assignment.EndTime);
            if (absences.Any(x =>
                    x.EmployeeId == assignment.EmployeeId &&
                    x.StartDate.Date <= interval.End.Date &&
                    x.EndDate.Date >= interval.Start.Date))
            {
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Rot",
                    Message = $"{assignment.Employee.LastName}, {assignment.Employee.FirstName} ist eingeplant, aber abwesend."
                });
            }

            if (!assignment.ShiftId.HasValue ||
                !ProductionScheduleService.IsShiftAllowedOnDate(
                    db,
                    assignment.WorkstationId,
                    assignment.ShiftId.Value,
                    assignment.Date))
            {
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Rot",
                    Message = $"{assignment.Workstation.Name} / {assignment.Shift?.Name ?? "Individuell"} ist an diesem Tag nicht freigegeben."
                });
            }
        }
    }

    private void ResetEditor()
    {
        SelectedEmployee ??= Employees.FirstOrDefault();
        SelectedWorkstation ??= Workstations.FirstOrDefault();
        if (SelectedShift is null || Shifts.All(x => x.Id != SelectedShift.Id))
            SelectedShift = Shifts.FirstOrDefault();
        Comment = string.Empty;
    }

    private static (DateTime Start, DateTime End) GetInterval(DateTime date, TimeSpan start, TimeSpan end)
    {
        var intervalStart = date.Date + start;
        var intervalEnd = date.Date + end;
        if (intervalEnd <= intervalStart)
            intervalEnd = intervalEnd.AddDays(1);
        return (intervalStart, intervalEnd);
    }
}

public class EmployeeOption
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
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
