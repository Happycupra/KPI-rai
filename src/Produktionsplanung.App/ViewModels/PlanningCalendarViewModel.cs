using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.ViewModels;

public partial class PlanningCalendarViewModel : ObservableObject
{
    private readonly CultureInfo culture = CultureInfo.GetCultureInfo("de-CH");

    public ObservableCollection<CalendarEntryRow> DayEntries { get; } = new();
    public ObservableCollection<CalendarDayColumn> WeekDays { get; } = new();
    public ObservableCollection<CalendarMonthDay> MonthDays { get; } = new();

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;
    [ObservableProperty] private int selectedViewIndex = 1;
    [ObservableProperty] private string statusMessage = string.Empty;

    public PlanningCalendarViewModel() => Load();

    public string HeaderText => SelectedViewIndex switch
    {
        0 => SelectedDate.ToString("dddd, dd. MMMM yyyy", culture),
        1 => $"KW {ISOWeek.GetWeekOfYear(SelectedDate)} · {GetMonday(SelectedDate):dd.MM.yyyy}–{GetMonday(SelectedDate).AddDays(6):dd.MM.yyyy}",
        _ => SelectedDate.ToString("MMMM yyyy", culture)
    };

    public string ModeText => SelectedViewIndex switch
    {
        0 => "Tag",
        1 => "Woche",
        _ => "Monat"
    };

    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(HeaderText));
        Load();
    }

    partial void OnSelectedViewIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(ModeText));
        Load();
    }

    [RelayCommand] private void ShowDay() => SelectedViewIndex = 0;
    [RelayCommand] private void ShowWeek() => SelectedViewIndex = 1;
    [RelayCommand] private void ShowMonth() => SelectedViewIndex = 2;
    [RelayCommand] private void Today() => SelectedDate = DateTime.Today;

    [RelayCommand]
    private void Previous()
    {
        SelectedDate = SelectedViewIndex switch
        {
            0 => SelectedDate.AddDays(-1),
            1 => SelectedDate.AddDays(-7),
            _ => SelectedDate.AddMonths(-1)
        };
    }

    [RelayCommand]
    private void Next()
    {
        SelectedDate = SelectedViewIndex switch
        {
            0 => SelectedDate.AddDays(1),
            1 => SelectedDate.AddDays(7),
            _ => SelectedDate.AddMonths(1)
        };
    }

    [RelayCommand]
    private void Refresh()
    {
        Load();
        StatusMessage = $"Kalender aktualisiert · {DateTime.Now:HH:mm}.";
    }

    public void SelectDate(DateTime date) => SelectedDate = date.Date;

    private void Load()
    {
        var (rangeStart, rangeEnd) = GetLoadRange();
        using var db = new AppDbContext();

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date >= rangeStart && x.Date.Date <= rangeEnd)
            .ToList();

        var orders = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.PlannedDate.Date >= rangeStart && x.PlannedDate.Date <= rangeEnd)
            .ToList();

        var absences = db.Absences.AsNoTracking()
            .Include(x => x.Employee)
            .Where(x => x.StartDate.Date <= rangeEnd && x.EndDate.Date >= rangeStart)
            .ToList();

        var operatingDays = db.OperatingCalendarDays.AsNoTracking()
            .Where(x => x.Date.Date >= rangeStart && x.Date.Date <= rangeEnd)
            .ToList();

        DayEntries.Clear();
        foreach (var item in BuildEntriesForDate(SelectedDate.Date, assignments, orders, absences, operatingDays))
            DayEntries.Add(item);

        var monday = GetMonday(SelectedDate);
        WeekDays.Clear();
        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            WeekDays.Add(new CalendarDayColumn
            {
                Date = date,
                DayName = date.ToString("ddd", culture),
                DateText = date.ToString("dd.MM."),
                IsToday = date == DateTime.Today,
                Entries = BuildEntriesForDate(date, assignments, orders, absences, operatingDays)
            });
        }

        var monthStart = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
        var gridStart = GetMonday(monthStart);
        MonthDays.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var entries = BuildEntriesForDate(date, assignments, orders, absences, operatingDays);
            MonthDays.Add(new CalendarMonthDay
            {
                Date = date,
                DayNumber = date.Day.ToString(culture),
                IsCurrentMonth = date.Month == SelectedDate.Month,
                IsToday = date == DateTime.Today,
                Entries = entries.Take(4).ToList(),
                HiddenEntryCount = Math.Max(0, entries.Count - 4)
            });
        }

        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(ModeText));
    }

    private List<CalendarEntryRow> BuildEntriesForDate(
        DateTime date,
        IEnumerable<Models.PlanningAssignment> assignments,
        IEnumerable<Models.ProductionOrder> orders,
        IEnumerable<Models.Absence> absences,
        IEnumerable<Models.OperatingCalendarDay> operatingDays)
    {
        var result = new List<CalendarEntryRow>();

        foreach (var x in operatingDays.Where(x => x.Date.Date == date.Date))
        {
            result.Add(new CalendarEntryRow
            {
                EntryType = "Betriebskalender",
                Accent = x.IsWorkingDay ? "#7C3AED" : "#64748B",
                IsAllDay = true,
                SortTime = TimeSpan.Zero,
                TimeText = "ganztägig",
                Title = x.Name,
                Subtitle = x.IsWorkingDay ? $"Sonderarbeitstag · Soll {x.TargetHoursFactor:0.##}×" : "Betriebsfrei",
                Detail = x.Comment ?? string.Empty
            });
        }

        foreach (var x in assignments.Where(x => x.Date.Date == date.Date))
        {
            result.Add(new CalendarEntryRow
            {
                EntryType = "Einsatz",
                Accent = "#2563EB",
                StartTime = x.StartTime,
                SortTime = x.StartTime,
                TimeText = $"{x.StartTime:hh\\:mm}–{x.EndTime:hh\\:mm}",
                Title = $"{x.Employee.LastName}, {x.Employee.FirstName}",
                Subtitle = $"{x.Workstation.Name} · {x.Shift?.Name ?? "Individuell"}",
                Detail = x.Comment ?? string.Empty
            });
        }

        foreach (var x in orders.Where(x => x.PlannedDate.Date == date.Date))
        {
            var start = x.PlannedStart ?? x.Shift?.StartTime ?? TimeSpan.FromHours(12);
            result.Add(new CalendarEntryRow
            {
                EntryType = "Auftrag",
                Accent = x.Priority is "Dringend" or "Hoch" ? "#EA580C" : "#0F766E",
                StartTime = start,
                SortTime = start,
                TimeText = x.PlannedStart.HasValue ? $"ab {x.PlannedStart.Value:hh\\:mm}" : x.Shift?.Name ?? "ganztägig",
                Title = $"{x.OrderNumber} · {x.Product}",
                Subtitle = $"{x.Workstation.Name} · {x.Shift?.Name ?? "ohne Schicht"} · Bedarf {x.RequiredStaff}",
                Detail = $"{x.Status} · Priorität {x.Priority}"
            });
        }

        foreach (var x in absences.Where(x => x.StartDate.Date <= date.Date && x.EndDate.Date >= date.Date))
        {
            result.Add(new CalendarEntryRow
            {
                EntryType = "Abwesenheit",
                Accent = "#DC2626",
                IsAllDay = true,
                SortTime = TimeSpan.Zero,
                TimeText = "ganztägig",
                Title = $"{x.Employee.LastName}, {x.Employee.FirstName}",
                Subtitle = x.Type,
                Detail = x.Comment ?? string.Empty
            });
        }

        return result
            .OrderByDescending(x => x.IsAllDay)
            .ThenBy(x => x.SortTime)
            .ThenBy(x => x.Title)
            .ToList();
    }

    private (DateTime Start, DateTime End) GetLoadRange()
    {
        var monthStart = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
        var start = GetMonday(monthStart);
        var end = start.AddDays(41);
        var weekStart = GetMonday(SelectedDate);
        if (weekStart < start) start = weekStart;
        if (weekStart.AddDays(6) > end) end = weekStart.AddDays(6);
        if (SelectedDate.Date < start) start = SelectedDate.Date;
        if (SelectedDate.Date > end) end = SelectedDate.Date;
        return (start, end);
    }

    private static DateTime GetMonday(DateTime date)
    {
        var days = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-days);
    }
}

public sealed class CalendarEntryRow
{
    public string EntryType { get; set; } = string.Empty;
    public string Accent { get; set; } = "#2563EB";
    public TimeSpan StartTime { get; set; }
    public TimeSpan SortTime { get; set; }
    public bool IsAllDay { get; set; }
    public string TimeText { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class CalendarDayColumn
{
    public DateTime Date { get; set; }
    public string DayName { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public bool IsToday { get; set; }
    public List<CalendarEntryRow> Entries { get; set; } = new();
}

public sealed class CalendarMonthDay
{
    public DateTime Date { get; set; }
    public string DayNumber { get; set; } = string.Empty;
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public List<CalendarEntryRow> Entries { get; set; } = new();
    public int HiddenEntryCount { get; set; }
    public string MoreText => HiddenEntryCount > 0 ? $"+ {HiddenEntryCount} weitere" : string.Empty;
}
