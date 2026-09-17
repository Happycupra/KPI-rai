using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class PlanningCalendarViewModel : ObservableObject
{
    private readonly CultureInfo culture = CultureInfo.GetCultureInfo("de-CH");
    private List<PlanningAssignment> assignments = new();
    private List<ProductionOrder> orders = new();
    private List<Absence> absences = new();
    private List<OperatingCalendarDay> operatingDays = new();
    private List<Employee> employees = new();
    private Dictionary<int, ProductionOrderCoverageRow> coverageByOrderId = new();

    public ObservableCollection<CalendarEntryRow> DayAllDayEntries { get; } = new();
    public ObservableCollection<CalendarEntryRow> DayTimedEntries { get; } = new();
    public ObservableCollection<CalendarDayColumn> WeekDays { get; } = new();
    public ObservableCollection<CalendarMonthDay> MonthDays { get; } = new();
    public ObservableCollection<CalendarEmployeeRow> DayEmployees { get; } = new();

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;
    [ObservableProperty] private int selectedViewIndex = 1;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private bool showAssignments = true;
    [ObservableProperty] private bool showOrders = true;
    [ObservableProperty] private bool showAbsences = true;
    [ObservableProperty] private bool showOperatingCalendar = true;
    [ObservableProperty] private bool showWeekends = true;
    [ObservableProperty] private CalendarEntryRow? selectedEntry;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private int plannedEmployeesCount;
    [ObservableProperty] private int orderCount;
    [ObservableProperty] private int absenceCount;
    [ObservableProperty] private int availableEmployeeCount;
    [ObservableProperty] private int understaffedOrderCount;

    public PlanningCalendarViewModel() => ReloadData();

    public string HeaderText => SelectedViewIndex switch
    {
        0 => SelectedDate.ToString("dddd, dd. MMMM yyyy", culture),
        1 => $"KW {ISOWeek.GetWeekOfYear(SelectedDate)} · {GetMonday(SelectedDate):dd.MM.yyyy}–{GetMonday(SelectedDate).AddDays(6):dd.MM.yyyy}",
        _ => SelectedDate.ToString("MMMM yyyy", culture)
    };

    public string SelectedDateText => SelectedDate.ToString("dddd, dd. MMMM", culture);
    public string SelectedDateShortText => SelectedDate.ToString("dd.MM.yyyy", culture);
    public string DayCoverageText => UnderstaffedOrderCount == 0
        ? "Alle offenen Aufträge sind personell gedeckt oder haben keinen Fehlbestand."
        : $"{UnderstaffedOrderCount} Auftrag/Aufträge sind noch unterbesetzt.";
    public string VisibleEntryText
    {
        get
        {
            var dayCount = DayAllDayEntries.Count + DayTimedEntries.Count;
            return string.IsNullOrWhiteSpace(SearchText)
                ? $"{dayCount} Kalendereinträge am gewählten Tag"
                : $"{dayCount} Treffer am gewählten Tag";
        }
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(SelectedDateText));
        OnPropertyChanged(nameof(SelectedDateShortText));
        ReloadData();
    }

    partial void OnSelectedViewIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HeaderText));
        RebuildViews();
    }

    partial void OnSearchTextChanged(string value) => RebuildViews();
    partial void OnShowAssignmentsChanged(bool value) => RebuildViews();
    partial void OnShowOrdersChanged(bool value) => RebuildViews();
    partial void OnShowAbsencesChanged(bool value) => RebuildViews();
    partial void OnShowOperatingCalendarChanged(bool value) => RebuildViews();
    partial void OnShowWeekendsChanged(bool value) => RebuildViews();

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
        ReloadData();
        StatusMessage = $"Kalender aktualisiert · {DateTime.Now:HH:mm}.";
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        ShowAssignments = true;
        ShowOrders = true;
        ShowAbsences = true;
        ShowOperatingCalendar = true;
        ShowWeekends = true;
        StatusMessage = "Filter zurückgesetzt.";
    }

    public void SelectDate(DateTime date) => SelectedDate = date.Date;

    public void SelectEntry(CalendarEntryRow? entry)
    {
        SelectedEntry = entry;
        if (entry is not null)
            StatusMessage = $"Ausgewählt: {entry.Title}";
    }

    private void ReloadData()
    {
        var (rangeStart, rangeEnd) = GetLoadRange();
        using var db = new AppDbContext();

        assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date >= rangeStart && x.Date.Date <= rangeEnd)
            .ToList();

        orders = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.PlannedDate.Date >= rangeStart && x.PlannedDate.Date <= rangeEnd)
            .ToList();

        absences = db.Absences.AsNoTracking()
            .Include(x => x.Employee)
            .Where(x => x.StartDate.Date <= rangeEnd && x.EndDate.Date >= rangeStart)
            .ToList();

        operatingDays = db.OperatingCalendarDays.AsNoTracking()
            .Where(x => x.Date.Date >= rangeStart && x.Date.Date <= rangeEnd)
            .ToList();

        employees = db.Employees.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToList();

        coverageByOrderId = ProductionOrderCoverageService.Load(rangeStart, rangeEnd)
            .ToDictionary(x => x.OrderId);

        RebuildViews();
    }

    private void RebuildViews()
    {
        var selectedKey = SelectedEntry is null
            ? null
            : $"{SelectedEntry.EntryType}:{SelectedEntry.EntryId}:{SelectedEntry.Date:yyyyMMdd}";

        var selectedDateEntries = GetFilteredEntries(SelectedDate.Date);
        DayAllDayEntries.ReplaceWith(selectedDateEntries.Where(x => x.IsAllDay));
        DayTimedEntries.ReplaceWith(selectedDateEntries.Where(x => !x.IsAllDay));

        var monday = GetMonday(SelectedDate);
        WeekDays.Clear();
        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            if (!ShowWeekends && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var entries = GetFilteredEntries(date);
            WeekDays.Add(new CalendarDayColumn
            {
                Date = date,
                DayName = date.ToString("ddd", culture),
                DateText = date.ToString("dd.MM."),
                IsToday = date == DateTime.Today,
                IsSelected = date == SelectedDate.Date,
                AllDayEntries = entries.Where(x => x.IsAllDay).ToList(),
                TimedEntries = entries.Where(x => !x.IsAllDay).ToList()
            });
        }

        var monthStart = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
        var gridStart = GetMonday(monthStart);
        MonthDays.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var entries = GetFilteredEntries(date);
            MonthDays.Add(new CalendarMonthDay
            {
                Date = date,
                DayNumber = date.Day.ToString(culture),
                IsCurrentMonth = date.Month == SelectedDate.Month,
                IsToday = date == DateTime.Today,
                IsSelected = date == SelectedDate.Date,
                Entries = entries.Take(3).ToList(),
                HiddenEntryCount = Math.Max(0, entries.Count - 3)
            });
        }

        RebuildDayEmployees();
        RebuildDaySummary();

        if (selectedKey is not null)
        {
            SelectedEntry = selectedDateEntries
                .Concat(WeekDays.SelectMany(x => x.AllDayEntries.Concat(x.TimedEntries)))
                .FirstOrDefault(x => $"{x.EntryType}:{x.EntryId}:{x.Date:yyyyMMdd}" == selectedKey);
        }

        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(VisibleEntryText));
    }

    private List<CalendarEntryRow> GetFilteredEntries(DateTime date)
    {
        var entries = BuildEntriesForDate(date);
        var term = SearchText.Trim();

        return entries.Where(x =>
                (x.EntryType != "Einsatz" || ShowAssignments) &&
                (x.EntryType != "Auftrag" || ShowOrders) &&
                (x.EntryType != "Abwesenheit" || ShowAbsences) &&
                (x.EntryType != "Betriebskalender" || ShowOperatingCalendar) &&
                (string.IsNullOrWhiteSpace(term) ||
                 x.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                 x.Subtitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                 x.Detail.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.IsAllDay)
            .ThenBy(x => x.SortTime)
            .ThenBy(x => x.Title)
            .ToList();
    }

    private List<CalendarEntryRow> BuildEntriesForDate(DateTime date)
    {
        var result = new List<CalendarEntryRow>();

        foreach (var x in operatingDays.Where(x => x.Date.Date == date.Date))
        {
            result.Add(new CalendarEntryRow
            {
                Date = date.Date,
                EntryId = x.Id,
                EntryType = "Betriebskalender",
                TypeLabel = "BETRIEB",
                Accent = x.IsWorkingDay ? "#7C3AED" : "#64748B",
                Background = x.IsWorkingDay ? "#F5F3FF" : "#F8FAFC",
                IsAllDay = true,
                SortTime = TimeSpan.Zero,
                TimeText = "Ganztägig",
                Title = x.Name,
                Subtitle = x.IsWorkingDay ? $"Sonderarbeitstag · Soll {x.TargetHoursFactor:0.##}×" : "Betriebsfrei",
                Detail = x.Comment ?? "Keine zusätzliche Notiz."
            });
        }

        foreach (var x in assignments.Where(x => x.Date.Date == date.Date))
        {
            result.Add(new CalendarEntryRow
            {
                Date = date.Date,
                EntryId = x.Id,
                EntryType = "Einsatz",
                TypeLabel = "EINSATZ",
                EmployeeId = x.EmployeeId,
                Accent = "#2563EB",
                Background = "#EFF6FF",
                StartTime = x.StartTime,
                SortTime = x.StartTime,
                TimeText = $"{x.StartTime:hh\\:mm}–{x.EndTime:hh\\:mm}",
                Title = $"{x.Employee.LastName}, {x.Employee.FirstName}",
                Subtitle = $"{x.Workstation.Name} · {x.Shift?.Name ?? "Individuell"}",
                Detail = string.IsNullOrWhiteSpace(x.Comment) ? $"{x.Employee.Role} · Pause {x.BreakMinutes} Min." : x.Comment!
            });
        }

        foreach (var x in orders.Where(x => x.PlannedDate.Date == date.Date))
        {
            var start = x.PlannedStart ?? x.Shift?.StartTime ?? TimeSpan.FromHours(12);
            coverageByOrderId.TryGetValue(x.Id, out var coverage);
            var isUnderstaffed = coverage?.CoverageStatus == "Unterbesetzt";
            result.Add(new CalendarEntryRow
            {
                Date = date.Date,
                EntryId = x.Id,
                EntryType = "Auftrag",
                TypeLabel = isUnderstaffed ? "AUFTRAG · PERSONAL FEHLT" : "AUFTRAG",
                Accent = isUnderstaffed ? "#D97706" : "#0F766E",
                Background = isUnderstaffed ? "#FFF7ED" : "#ECFDF5",
                StartTime = start,
                SortTime = start,
                TimeText = x.PlannedStart.HasValue ? $"ab {x.PlannedStart.Value:hh\\:mm}" : x.Shift?.Name ?? "Ganztägig",
                Title = $"{x.OrderNumber} · {x.Product}",
                Subtitle = $"{x.Workstation.Name} · {x.Shift?.Name ?? "ohne Schicht"}",
                BadgeText = coverage?.CoverageText ?? $"0/{x.RequiredStaff}",
                Detail = $"{x.Status} · Priorität {x.Priority} · Personal {coverage?.CoverageText ?? $"0/{x.RequiredStaff}"}"
            });
        }

        foreach (var x in absences.Where(x => x.StartDate.Date <= date.Date && x.EndDate.Date >= date.Date))
        {
            result.Add(new CalendarEntryRow
            {
                Date = date.Date,
                EntryId = x.Id,
                EntryType = "Abwesenheit",
                TypeLabel = "ABWESEND",
                EmployeeId = x.EmployeeId,
                Accent = "#DC2626",
                Background = "#FEF2F2",
                IsAllDay = true,
                SortTime = TimeSpan.Zero,
                TimeText = "Ganztägig",
                Title = $"{x.Employee.LastName}, {x.Employee.FirstName}",
                Subtitle = x.Type,
                Detail = x.Comment ?? $"{x.StartDate:dd.MM.yyyy}–{x.EndDate:dd.MM.yyyy}"
            });
        }

        return result;
    }

    private void RebuildDayEmployees()
    {
        DayEmployees.Clear();
        var date = SelectedDate.Date;
        var absentEmployeeIds = absences
            .Where(x => x.StartDate.Date <= date && x.EndDate.Date >= date)
            .Select(x => x.EmployeeId)
            .ToHashSet();

        foreach (var employee in employees.Where(x => !absentEmployeeIds.Contains(x.Id)))
        {
            var employeeAssignments = assignments
                .Where(x => x.EmployeeId == employee.Id && x.Date.Date == date)
                .OrderBy(x => x.StartTime)
                .ToList();

            var first = employeeAssignments.FirstOrDefault();
            var assignmentText = first is null
                ? "Noch ohne Einsatz"
                : $"{first.Workstation.Name} · {first.Shift?.Name ?? "Individuell"}" +
                  (employeeAssignments.Count > 1 ? $" · +{employeeAssignments.Count - 1}" : string.Empty);

            DayEmployees.Add(new CalendarEmployeeRow
            {
                EmployeeId = employee.Id,
                EmployeeName = $"{employee.LastName}, {employee.FirstName}",
                Initials = BuildInitials(employee.FirstName, employee.LastName),
                Role = employee.Role,
                AssignmentText = assignmentText,
                StatusText = first is null ? "Noch frei" : "Eingeplant",
                StatusBrush = first is null ? "#16A34A" : "#2563EB",
                SortBucket = first is null ? 0 : 1
            });
        }

        var sorted = DayEmployees.OrderBy(x => x.SortBucket).ThenBy(x => x.EmployeeName).ToList();
        DayEmployees.Clear();
        foreach (var row in sorted)
            DayEmployees.Add(row);
    }

    private void RebuildDaySummary()
    {
        var date = SelectedDate.Date;
        PlannedEmployeesCount = assignments
            .Where(x => x.Date.Date == date)
            .Select(x => x.EmployeeId)
            .Distinct()
            .Count();
        OrderCount = orders.Count(x => x.PlannedDate.Date == date);
        AbsenceCount = absences
            .Where(x => x.StartDate.Date <= date && x.EndDate.Date >= date)
            .Select(x => x.EmployeeId)
            .Distinct()
            .Count();
        AvailableEmployeeCount = Math.Max(0, employees.Count - AbsenceCount);
        UnderstaffedOrderCount = coverageByOrderId.Values.Count(x =>
            x.Date.Date == date && x.CoverageStatus == "Unterbesetzt");

        OnPropertyChanged(nameof(DayCoverageText));
        OnPropertyChanged(nameof(VisibleEntryText));
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

    private static string BuildInitials(string firstName, string lastName)
    {
        var first = string.IsNullOrWhiteSpace(firstName) ? string.Empty : firstName.Trim()[0].ToString();
        var last = string.IsNullOrWhiteSpace(lastName) ? string.Empty : lastName.Trim()[0].ToString();
        return (first + last).ToUpperInvariant();
    }

    private static DateTime GetMonday(DateTime date)
    {
        var days = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-days);
    }
}

public sealed class CalendarEntryRow
{
    public DateTime Date { get; set; }
    public int EntryId { get; set; }
    public int? EmployeeId { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    public string Accent { get; set; } = "#2563EB";
    public string Background { get; set; } = "#EFF6FF";
    public TimeSpan StartTime { get; set; }
    public TimeSpan SortTime { get; set; }
    public bool IsAllDay { get; set; }
    public string TimeText { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
}

public sealed class CalendarDayColumn
{
    public DateTime Date { get; set; }
    public string DayName { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public bool IsToday { get; set; }
    public bool IsSelected { get; set; }
    public List<CalendarEntryRow> AllDayEntries { get; set; } = new();
    public List<CalendarEntryRow> TimedEntries { get; set; } = new();
    public int EntryCount => AllDayEntries.Count + TimedEntries.Count;
    public string EntryCountText => EntryCount == 1 ? "1 Eintrag" : $"{EntryCount} Einträge";
}

public sealed class CalendarMonthDay
{
    public DateTime Date { get; set; }
    public string DayNumber { get; set; } = string.Empty;
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public bool IsSelected { get; set; }
    public List<CalendarEntryRow> Entries { get; set; } = new();
    public int HiddenEntryCount { get; set; }
    public string MoreText => HiddenEntryCount > 0 ? $"+ {HiddenEntryCount} weitere" : string.Empty;
}

public sealed class CalendarEmployeeRow
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string AssignmentText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string StatusBrush { get; set; } = "#64748B";
    public int SortBucket { get; set; }
}

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
