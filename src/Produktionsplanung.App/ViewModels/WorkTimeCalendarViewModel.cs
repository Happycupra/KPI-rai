using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class WorkTimeCalendarViewModel : ObservableObject
{
    public ObservableCollection<EmployeeOption> Employees { get; } = new();
    public ObservableCollection<WorkTimeEntryRow> Entries { get; } = new();
    public ObservableCollection<WorkTimeBalanceRow> Balances { get; } = new();
    public ObservableCollection<OperatingCalendarRow> CalendarDays { get; } = new();

    [ObservableProperty] private DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private EmployeeOption? selectedEmployee;
    [ObservableProperty] private WorkTimeEntryRow? selectedEntry;
    [ObservableProperty] private DateTime entryDate = DateTime.Today;
    [ObservableProperty] private string startTimeText = "06:00";
    [ObservableProperty] private string endTimeText = "14:00";
    [ObservableProperty] private int breakMinutes = 30;
    [ObservableProperty] private string entryComment = string.Empty;

    [ObservableProperty] private OperatingCalendarRow? selectedCalendarDay;
    [ObservableProperty] private DateTime calendarDate = DateTime.Today;
    [ObservableProperty] private string calendarName = string.Empty;
    [ObservableProperty] private bool calendarIsWorkingDay;
    [ObservableProperty] private double calendarTargetHoursFactor;
    [ObservableProperty] private string calendarComment = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public WorkTimeCalendarViewModel()
    {
        LoadEmployees();
        Load();
    }

    public string MonthText => Month.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-CH"));

    partial void OnMonthChanged(DateTime value)
    {
        var normalized = new DateTime(value.Year, value.Month, 1);
        if (value != normalized)
        {
            Month = normalized;
            return;
        }
        OnPropertyChanged(nameof(MonthText));
        Load();
    }

    partial void OnSelectedEntryChanged(WorkTimeEntryRow? value)
    {
        if (value is null) return;
        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == value.EmployeeId);
        EntryDate = value.Date;
        StartTimeText = value.StartTime.ToString(@"hh\:mm");
        EndTimeText = value.EndTime.ToString(@"hh\:mm");
        BreakMinutes = value.BreakMinutes;
        EntryComment = value.Comment ?? string.Empty;
        StatusMessage = string.Empty;
    }

    partial void OnSelectedCalendarDayChanged(OperatingCalendarRow? value)
    {
        if (value is null) return;
        CalendarDate = value.Date;
        CalendarName = value.Name;
        CalendarIsWorkingDay = value.IsWorkingDay;
        CalendarTargetHoursFactor = value.TargetHoursFactor;
        CalendarComment = value.Comment ?? string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void PreviousMonth() => Month = Month.AddMonths(-1);

    [RelayCommand]
    private void CurrentMonth() => Month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [RelayCommand]
    private void NextMonth() => Month = Month.AddMonths(1);

    [RelayCommand]
    private void Refresh()
    {
        LoadEmployees();
        Load();
        StatusMessage = "Arbeitszeit und Betriebskalender aktualisiert.";
    }

    [RelayCommand]
    private void NewEntry()
    {
        SelectedEntry = null;
        EntryDate = DateTime.Today.Month == Month.Month && DateTime.Today.Year == Month.Year ? DateTime.Today : Month;
        StartTimeText = "06:00";
        EndTimeText = "14:00";
        BreakMinutes = 30;
        EntryComment = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void SaveEntry()
    {
        if (SelectedEmployee is null)
        {
            StatusMessage = "Bitte einen Mitarbeiter auswählen.";
            return;
        }
        if (!TimeSpan.TryParse(StartTimeText, out var start) || !TimeSpan.TryParse(EndTimeText, out var end))
        {
            StatusMessage = "Start und Ende bitte als HH:mm eingeben.";
            return;
        }
        if (BreakMinutes < 0 || BreakMinutes > 240)
        {
            StatusMessage = "Pause muss zwischen 0 und 240 Minuten liegen.";
            return;
        }
        if (OperatingCalendarService.CalculateNetHours(start, end, BreakMinutes) <= 0)
        {
            StatusMessage = "Die resultierende Arbeitszeit muss grösser als 0 Stunden sein.";
            return;
        }

        using var db = new AppDbContext();
        WorkTimeEntry entity;
        if (SelectedEntry is null)
        {
            entity = new WorkTimeEntry();
            db.WorkTimeEntries.Add(entity);
        }
        else
        {
            entity = db.WorkTimeEntries.First(x => x.Id == SelectedEntry.Id);
        }

        entity.EmployeeId = SelectedEmployee.Id;
        entity.Date = EntryDate.Date;
        entity.StartTime = start;
        entity.EndTime = end;
        entity.BreakMinutes = BreakMinutes;
        entity.Comment = string.IsNullOrWhiteSpace(EntryComment) ? null : EntryComment.Trim();
        db.SaveChanges();

        if (EntryDate.Year != Month.Year || EntryDate.Month != Month.Month)
            Month = new DateTime(EntryDate.Year, EntryDate.Month, 1);
        else
            Load(entity.Id);
        StatusMessage = "Arbeitszeit gespeichert.";
    }

    [RelayCommand]
    private void DeleteEntry()
    {
        if (SelectedEntry is null) return;
        using var db = new AppDbContext();
        var entity = db.WorkTimeEntries.FirstOrDefault(x => x.Id == SelectedEntry.Id);
        if (entity is null) return;
        db.WorkTimeEntries.Remove(entity);
        db.SaveChanges();
        Load();
        NewEntry();
        StatusMessage = "Arbeitszeit gelöscht.";
    }

    [RelayCommand]
    private void NewCalendarDay()
    {
        SelectedCalendarDay = null;
        CalendarDate = DateTime.Today.Month == Month.Month && DateTime.Today.Year == Month.Year ? DateTime.Today : Month;
        CalendarName = string.Empty;
        CalendarIsWorkingDay = false;
        CalendarTargetHoursFactor = 0;
        CalendarComment = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void SaveCalendarDay()
    {
        if (string.IsNullOrWhiteSpace(CalendarName))
        {
            StatusMessage = "Bitte eine Bezeichnung eingeben, z. B. Feiertag, Betriebsferien oder Sonderarbeitstag.";
            return;
        }
        if (CalendarTargetHoursFactor < 0 || CalendarTargetHoursFactor > 2)
        {
            StatusMessage = "Sollstunden-Faktor muss zwischen 0 und 2 liegen.";
            return;
        }
        if (!CalendarIsWorkingDay)
            CalendarTargetHoursFactor = 0;

        using var db = new AppDbContext();
        var existingSameDate = db.OperatingCalendarDays.FirstOrDefault(x => x.Date.Date == CalendarDate.Date);
        OperatingCalendarDay entity;
        if (SelectedCalendarDay is null)
        {
            entity = existingSameDate ?? new OperatingCalendarDay();
            if (existingSameDate is null)
                db.OperatingCalendarDays.Add(entity);
        }
        else
        {
            entity = db.OperatingCalendarDays.First(x => x.Id == SelectedCalendarDay.Id);
            if (existingSameDate is not null && existingSameDate.Id != entity.Id)
            {
                StatusMessage = "Für dieses Datum existiert bereits eine Betriebskalender-Ausnahme.";
                return;
            }
        }

        entity.Date = CalendarDate.Date;
        entity.Name = CalendarName.Trim();
        entity.IsWorkingDay = CalendarIsWorkingDay;
        entity.TargetHoursFactor = CalendarIsWorkingDay ? CalendarTargetHoursFactor : 0;
        entity.Comment = string.IsNullOrWhiteSpace(CalendarComment) ? null : CalendarComment.Trim();
        db.SaveChanges();

        if (CalendarDate.Year != Month.Year || CalendarDate.Month != Month.Month)
            Month = new DateTime(CalendarDate.Year, CalendarDate.Month, 1);
        else
            Load(calendarSelectId: entity.Id);
        StatusMessage = "Betriebskalender gespeichert.";
    }

    [RelayCommand]
    private void DeleteCalendarDay()
    {
        if (SelectedCalendarDay is null) return;
        using var db = new AppDbContext();
        var entity = db.OperatingCalendarDays.FirstOrDefault(x => x.Id == SelectedCalendarDay.Id);
        if (entity is null) return;
        db.OperatingCalendarDays.Remove(entity);
        db.SaveChanges();
        Load();
        NewCalendarDay();
        StatusMessage = "Kalender-Ausnahme gelöscht; es gilt wieder der Standard Mo–Fr.";
    }

    private void LoadEmployees()
    {
        var selectedId = SelectedEmployee?.Id;
        using var db = new AppDbContext();
        Employees.Clear();
        foreach (var x in db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.LastName).ThenBy(x => x.FirstName))
        {
            Employees.Add(new EmployeeOption
            {
                Id = x.Id,
                DisplayName = $"{x.LastName}, {x.FirstName}",
                Role = x.Role
            });
        }
        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == selectedId) ?? Employees.FirstOrDefault();
    }

    private void Load(int? entrySelectId = null, int? calendarSelectId = null)
    {
        using var db = new AppDbContext();
        var start = Month.Date;
        var end = start.AddMonths(1).AddDays(-1);

        var entries = db.WorkTimeEntries.AsNoTracking()
            .Include(x => x.Employee)
            .Where(x => x.Date.Date >= start && x.Date.Date <= end)
            .AsEnumerable()
            .OrderBy(x => x.Date)
            .ThenBy(x => x.StartTime)
            .ThenBy(x => x.Employee.LastName)
            .ToList();

        Entries.Clear();
        foreach (var x in entries)
        {
            Entries.Add(new WorkTimeEntryRow
            {
                Id = x.Id,
                EmployeeId = x.EmployeeId,
                Date = x.Date,
                DateText = x.Date.ToString("ddd dd.MM."),
                EmployeeName = $"{x.Employee.LastName}, {x.Employee.FirstName}",
                StartTime = x.StartTime,
                EndTime = x.EndTime,
                TimeText = $"{x.StartTime:hh\\:mm}–{x.EndTime:hh\\:mm}",
                BreakMinutes = x.BreakMinutes,
                NetHours = OperatingCalendarService.CalculateNetHours(x.StartTime, x.EndTime, x.BreakMinutes),
                Comment = x.Comment
            });
        }

        var employees = db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToList();
        var planned = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date >= start && x.Date.Date <= end)
            .ToList();

        Balances.Clear();
        foreach (var employee in employees)
        {
            var target = OperatingCalendarService.GetTargetHours(db, employee, start, end);
            var actual = entries.Where(x => x.EmployeeId == employee.Id)
                .Sum(x => OperatingCalendarService.CalculateNetHours(x.StartTime, x.EndTime, x.BreakMinutes));
            var plannedHours = planned.Where(x => x.EmployeeId == employee.Id)
                .Sum(x => OperatingCalendarService.CalculateNetHours(x.StartTime, x.EndTime, x.BreakMinutes));
            Balances.Add(new WorkTimeBalanceRow
            {
                EmployeeName = $"{employee.LastName}, {employee.FirstName}",
                TargetHours = target,
                PlannedHours = plannedHours,
                ActualHours = actual,
                BalanceHours = actual - target
            });
        }

        var calendar = db.OperatingCalendarDays.AsNoTracking()
            .Where(x => x.Date.Date >= start && x.Date.Date <= end)
            .OrderBy(x => x.Date)
            .ToList();
        CalendarDays.Clear();
        foreach (var x in calendar)
        {
            CalendarDays.Add(new OperatingCalendarRow
            {
                Id = x.Id,
                Date = x.Date,
                DateText = x.Date.ToString("ddd dd.MM.yyyy"),
                Name = x.Name,
                IsWorkingDay = x.IsWorkingDay,
                WorkingDayText = x.IsWorkingDay ? "Arbeitstag" : "Frei",
                TargetHoursFactor = x.TargetHoursFactor,
                FactorText = x.IsWorkingDay ? $"{x.TargetHoursFactor:0.##}×" : "0×",
                Comment = x.Comment
            });
        }

        SelectedEntry = entrySelectId.HasValue ? Entries.FirstOrDefault(x => x.Id == entrySelectId) : null;
        SelectedCalendarDay = calendarSelectId.HasValue ? CalendarDays.FirstOrDefault(x => x.Id == calendarSelectId) : null;
        OnPropertyChanged(nameof(MonthText));
    }
}

public sealed class WorkTimeEntryRow
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime Date { get; set; }
    public string DateText { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string TimeText { get; set; } = string.Empty;
    public int BreakMinutes { get; set; }
    public double NetHours { get; set; }
    public string? Comment { get; set; }
}

public sealed class WorkTimeBalanceRow
{
    public string EmployeeName { get; set; } = string.Empty;
    public double TargetHours { get; set; }
    public double PlannedHours { get; set; }
    public double ActualHours { get; set; }
    public double BalanceHours { get; set; }
}

public sealed class OperatingCalendarRow
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string DateText { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsWorkingDay { get; set; }
    public string WorkingDayText { get; set; } = string.Empty;
    public double TargetHoursFactor { get; set; }
    public string FactorText { get; set; } = string.Empty;
    public string? Comment { get; set; }
}
