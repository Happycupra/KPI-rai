using System.Collections.ObjectModel;
using System.Globalization;
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
    public ObservableCollection<string> StatusOptions { get; } = new();
    public ObservableCollection<WorkTimeArticleOption> Articles { get; } = new();
    public ObservableCollection<WorkTimeOrderOption> Orders { get; } = new();

    private readonly List<WorkTimeOrderOption> allOrders = new();
    private int? currentEmployeeId;
    private bool syncingArticleOrder;

    [ObservableProperty] private DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private EmployeeOption? selectedEmployee;
    [ObservableProperty] private WorkTimeEntryRow? selectedEntry;
    [ObservableProperty] private DateTime entryDate = DateTime.Today;
    [ObservableProperty] private string startTimeText = DateTime.Now.ToString("HH:mm");
    [ObservableProperty] private string endTimeText = string.Empty;
    [ObservableProperty] private int breakMinutes;
    [ObservableProperty] private string entryStatus = string.Empty;
    [ObservableProperty] private WorkTimeArticleOption? selectedArticle;
    [ObservableProperty] private WorkTimeOrderOption? selectedOrder;
    [ObservableProperty] private string entryComment = string.Empty;
    [ObservableProperty] private string currentEmployeeName = "Nicht zugeordnet";
    [ObservableProperty] private bool canLogTime;
    [ObservableProperty] private bool hasRunningEntry;
    [ObservableProperty] private OperatingCalendarRow? selectedCalendarDay;
    [ObservableProperty] private DateTime calendarDate = DateTime.Today;
    [ObservableProperty] private string calendarName = string.Empty;
    [ObservableProperty] private bool calendarIsWorkingDay;
    [ObservableProperty] private double calendarTargetHoursFactor;
    [ObservableProperty] private string calendarComment = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public WorkTimeCalendarViewModel()
    {
        LoadCurrentEmployee();
        LoadStatuses();
        LoadArticlesAndOrders();
        Load();
        ResetEditorsForMonth();
        if (!CanLogTime)
            StatusMessage = "Dein Benutzerkonto ist noch keinem Mitarbeiter zugeordnet. Bitte in Benutzer / Audit einen Mitarbeiter verknüpfen.";
    }

    public string MonthText => Month.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-CH"));
    public bool CanManageOperatingCalendar => SessionService.IsAdministrator;

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
        ResetEditorsForMonth();
        StatusMessage = CanLogTime
            ? "Monat gewechselt. Eingabefelder wurden zurückgesetzt."
            : "Dein Benutzerkonto ist noch keinem Mitarbeiter zugeordnet.";
    }

    partial void OnCalendarIsWorkingDayChanged(bool value)
    {
        if (value && CalendarTargetHoursFactor <= 0) CalendarTargetHoursFactor = 1;
        else if (!value) CalendarTargetHoursFactor = 0;
    }

    partial void OnSelectedArticleChanged(WorkTimeArticleOption? value)
    {
        if (syncingArticleOrder) return;
        RefreshOrdersForArticle();
    }

    partial void OnSelectedOrderChanged(WorkTimeOrderOption? value)
    {
        if (syncingArticleOrder || value is null || string.IsNullOrWhiteSpace(value.ArticleNumber)) return;
        var article = Articles.FirstOrDefault(x =>
            string.Equals(x.ArticleNumber, value.ArticleNumber, StringComparison.OrdinalIgnoreCase));
        if (article is null || SelectedArticle?.Id == article.Id) return;

        syncingArticleOrder = true;
        SelectedArticle = article;
        RefreshOrdersForArticle(value.Id);
        SelectedOrder = Orders.FirstOrDefault(x => x.Id == value.Id);
        syncingArticleOrder = false;
    }

    partial void OnSelectedEntryChanged(WorkTimeEntryRow? value)
    {
        if (value is null) return;

        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == value.EmployeeId) ??
            new EmployeeOption { Id = value.EmployeeId, DisplayName = value.EmployeeName };
        EntryDate = value.Date;
        StartTimeText = value.StartTime.ToString(@"hh\:mm");
        EndTimeText = value.IsRunning ? string.Empty : value.EndTime.ToString(@"hh\:mm");
        BreakMinutes = value.BreakMinutes;
        EntryStatus = value.Status;
        EntryComment = value.Comment ?? string.Empty;

        syncingArticleOrder = true;
        SelectedArticle = Articles.FirstOrDefault(x =>
            string.Equals(x.ArticleNumber, value.ArticleNumber, StringComparison.OrdinalIgnoreCase));
        RefreshOrdersForArticle();
        SelectedOrder = Orders.FirstOrDefault(x =>
            string.Equals(x.OrderNumber, value.OrderNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.BatchNumber, value.BatchNumber, StringComparison.OrdinalIgnoreCase));
        syncingArticleOrder = false;

        StatusMessage = value.EmployeeId != currentEmployeeId && !SessionService.IsAdministrator
            ? "Dieser Eintrag gehört einem anderen Mitarbeiter und kann mit diesem Benutzerkonto nicht bearbeitet werden."
            : string.Empty;
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

    [RelayCommand] private void PreviousMonth() => Month = Month.AddMonths(-1);
    [RelayCommand] private void CurrentMonth() => Month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [RelayCommand] private void NextMonth() => Month = Month.AddMonths(1);

    [RelayCommand]
    private void Refresh()
    {
        LoadCurrentEmployee();
        LoadStatuses();
        LoadArticlesAndOrders();
        Load();
        ResetEditorsForMonth();
        StatusMessage = CanLogTime
            ? "Arbeitszeit / Betrieb und Betriebskalender aktualisiert."
            : "Dein Benutzerkonto ist noch keinem Mitarbeiter zugeordnet.";
    }

    [RelayCommand]
    private void NewEntry() => ResetEntryEditor();

    private void ResetEditorsForMonth()
    {
        ResetEntryEditor();
        ResetCalendarEditor();
    }

    private void ResetEntryEditor()
    {
        SelectedEntry = null;
        SelectedEmployee = currentEmployeeId.HasValue
            ? Employees.FirstOrDefault(x => x.Id == currentEmployeeId.Value)
            : null;
        EntryDate = DateTime.Today.Month == Month.Month && DateTime.Today.Year == Month.Year ? DateTime.Today : Month;
        StartTimeText = DateTime.Now.ToString("HH:mm");
        EndTimeText = string.Empty;
        BreakMinutes = 0;
        EntryStatus = string.Empty;
        SelectedArticle = null;
        SelectedOrder = null;
        EntryComment = string.Empty;
        RefreshOrdersForArticle();
    }

    private void ResetCalendarEditor()
    {
        SelectedCalendarDay = null;
        CalendarDate = DateTime.Today.Month == Month.Month && DateTime.Today.Year == Month.Year ? DateTime.Today : Month;
        CalendarName = string.Empty;
        CalendarIsWorkingDay = false;
        CalendarTargetHoursFactor = 0;
        CalendarComment = string.Empty;
    }

    [RelayCommand]
    private void StartNow()
    {
        if (!TryGetEntryEmployeeId(isNew: true, out var employeeId)) return;
        var status = NormalizeStatus();
        if (status is null) return;

        var now = DateTime.Now;
        using var db = new AppDbContext();
        if (db.WorkTimeEntries.Any(x => x.EmployeeId == employeeId && x.IsRunning))
        {
            StatusMessage = "Es läuft bereits ein Zeiteintrag. Bitte diesen zuerst stoppen.";
            return;
        }

        var existing = db.WorkTimeEntries.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId &&
                        x.Date.Date >= now.Date.AddDays(-1) &&
                        x.Date.Date <= now.Date.AddDays(1))
            .ToList();
        if (existing.Any(x =>
        {
            var interval = GetOverlapInterval(x);
            return interval.Start <= now && now < interval.End;
        }))
        {
            StatusMessage = "Zum aktuellen Zeitpunkt besteht bereits ein Arbeitszeiteintrag.";
            return;
        }

        var entity = new WorkTimeEntry
        {
            EmployeeId = employeeId,
            Date = now.Date,
            StartTime = now.TimeOfDay,
            EndTime = now.TimeOfDay,
            BreakMinutes = 0,
            Status = status,
            IsRunning = true,
            ArticleNumber = SelectedArticle?.ArticleNumber ?? SelectedOrder?.ArticleNumber ?? string.Empty,
            BatchNumber = SelectedOrder?.BatchNumber ?? string.Empty,
            OrderNumber = SelectedOrder?.OrderNumber ?? string.Empty,
            Comment = NormalizeComment()
        };
        db.WorkTimeEntries.Add(entity);
        SaveStatusCatalog(db, status);
        db.SaveChanges();

        Month = new DateTime(now.Year, now.Month, 1);
        Load(entity.Id);
        StatusMessage = $"{status} gestartet · {now:HH:mm}.";
    }

    [RelayCommand]
    private void StopRunning()
    {
        if (!TryGetEntryEmployeeId(isNew: true, out var employeeId)) return;
        using var db = new AppDbContext();
        var entity = db.WorkTimeEntries
            .Where(x => x.EmployeeId == employeeId && x.IsRunning)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.StartTime)
            .FirstOrDefault();
        if (entity is null)
        {
            StatusMessage = "Für dich läuft aktuell kein Zeiteintrag.";
            return;
        }

        var now = DateTime.Now;
        entity.EndTime = now.TimeOfDay;
        entity.IsRunning = false;
        db.SaveChanges();

        Month = new DateTime(entity.Date.Year, entity.Date.Month, 1);
        Load(entity.Id);
        StatusMessage = $"{entity.Status} gestoppt · {now:HH:mm}.";
    }

    [RelayCommand]
    private void SaveEntry()
    {
        var isNew = SelectedEntry is null;
        if (!TryGetEntryEmployeeId(isNew, out var employeeId)) return;
        var status = NormalizeStatus();
        if (status is null) return;

        if (!TryParseClock(StartTimeText, out var start))
        {
            StatusMessage = "Start bitte exakt als HH:mm (00:00–23:59) eingeben.";
            return;
        }

        var keepRunning = SelectedEntry?.IsRunning == true && string.IsNullOrWhiteSpace(EndTimeText);
        TimeSpan end;
        if (keepRunning)
        {
            end = start;
        }
        else
        {
            if (!TryParseClock(EndTimeText, out end))
            {
                StatusMessage = "Ende bitte exakt als HH:mm (00:00–23:59) eingeben.";
                return;
            }
            if (start == end)
            {
                StatusMessage = "Start und Ende dürfen bei einem abgeschlossenen Eintrag nicht identisch sein.";
                return;
            }
        }

        if (BreakMinutes < 0 || BreakMinutes > 240)
        {
            StatusMessage = "Pause muss zwischen 0 und 240 Minuten liegen.";
            return;
        }

        if (!keepRunning)
        {
            var duration = end > start ? end - start : end.Add(TimeSpan.FromDays(1)) - start;
            if (TimeSpan.FromMinutes(BreakMinutes) >= duration)
            {
                StatusMessage = "Die Pause muss kürzer als die erfasste Dauer sein.";
                return;
            }
        }

        using var db = new AppDbContext();
        var candidate = keepRunning
            ? (Start: EntryDate.Date + start, End: DateTime.MaxValue)
            : GetInterval(EntryDate.Date, start, end);
        var other = db.WorkTimeEntries.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId &&
                        x.Date.Date >= EntryDate.Date.AddDays(-1) &&
                        x.Date.Date <= EntryDate.Date.AddDays(1))
            .ToList();
        if (SelectedEntry is not null)
            other = other.Where(x => x.Id != SelectedEntry.Id).ToList();

        var conflict = other.FirstOrDefault(x => IntervalsOverlap(candidate, GetOverlapInterval(x)));
        if (conflict is not null)
        {
            StatusMessage = $"Arbeitszeit überschneidet sich mit einem bestehenden Eintrag vom {conflict.Date:dd.MM.yyyy} ({conflict.StartTime:hh\\:mm}–{(conflict.IsRunning ? "läuft" : conflict.EndTime.ToString(@"hh\:mm"))}).";
            return;
        }

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

        entity.EmployeeId = employeeId;
        entity.Date = EntryDate.Date;
        entity.StartTime = start;
        entity.EndTime = end;
        entity.BreakMinutes = BreakMinutes;
        entity.Status = status;
        entity.IsRunning = keepRunning;
        entity.ArticleNumber = SelectedArticle?.ArticleNumber ?? SelectedOrder?.ArticleNumber ?? string.Empty;
        entity.BatchNumber = SelectedOrder?.BatchNumber ?? string.Empty;
        entity.OrderNumber = SelectedOrder?.OrderNumber ?? string.Empty;
        entity.Comment = NormalizeComment();
        SaveStatusCatalog(db, status);
        db.SaveChanges();

        if (EntryDate.Year != Month.Year || EntryDate.Month != Month.Month)
            Month = new DateTime(EntryDate.Year, EntryDate.Month, 1);
        else
            Load(entity.Id);

        LoadStatuses();
        StatusMessage = "Arbeitszeit / Betrieb gespeichert.";
    }

    private bool TryGetEntryEmployeeId(bool isNew, out int employeeId)
    {
        employeeId = 0;
        if (!CanLogTime || !currentEmployeeId.HasValue)
        {
            StatusMessage = "Dein Benutzerkonto ist keinem Mitarbeiter zugeordnet.";
            return false;
        }

        if (!isNew && SelectedEntry is not null && SelectedEntry.EmployeeId != currentEmployeeId.Value)
        {
            if (!SessionService.IsAdministrator)
            {
                StatusMessage = "Du kannst nur deine eigenen Arbeitszeit-/Betriebseinträge bearbeiten.";
                return false;
            }
            employeeId = SelectedEntry.EmployeeId;
            return true;
        }

        employeeId = currentEmployeeId.Value;
        return true;
    }

    private string? NormalizeStatus()
    {
        var status = EntryStatus.Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            StatusMessage = "Status ist ein Pflichtfeld. Bitte z. B. Mischen, Produzieren, Pause oder Störung eingeben.";
            return null;
        }
        if (status.Length > 80)
        {
            StatusMessage = "Status darf maximal 80 Zeichen enthalten.";
            return null;
        }
        return status;
    }

    private string? NormalizeComment() =>
        string.IsNullOrWhiteSpace(EntryComment) ? null : EntryComment.Trim();

    private static void SaveStatusCatalog(AppDbContext db, string status)
    {
        var existing = db.WorkTimeStatuses.ToList()
            .FirstOrDefault(x => string.Equals(x.Name, status, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            db.WorkTimeStatuses.Add(new WorkTimeStatus { Name = status, LastUsedAtUtc = DateTime.UtcNow });
        }
        else
        {
            existing.Name = status;
            existing.LastUsedAtUtc = DateTime.UtcNow;
        }
    }

    private static bool TryParseClock(string? text, out TimeSpan value) =>
        TimeSpan.TryParseExact(text?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out value) &&
        value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);

    private static (DateTime Start, DateTime End) GetInterval(DateTime date, TimeSpan start, TimeSpan end)
    {
        var a = date.Date + start;
        var b = date.Date + end;
        if (b <= a) b = b.AddDays(1);
        return (a, b);
    }

    private static (DateTime Start, DateTime End) GetOverlapInterval(WorkTimeEntry entry)
    {
        if (entry.IsRunning)
            return (entry.Date.Date + entry.StartTime, DateTime.MaxValue);
        return GetInterval(entry.Date, entry.StartTime, entry.EndTime);
    }

    private static bool IntervalsOverlap((DateTime Start, DateTime End) a, (DateTime Start, DateTime End) b) =>
        a.Start < b.End && b.Start < a.End;

    private static double CalculateNetHours(WorkTimeEntry entry, DateTime now)
    {
        var start = entry.Date.Date + entry.StartTime;
        var end = entry.IsRunning ? now : entry.Date.Date + entry.EndTime;
        if (!entry.IsRunning && end <= start) end = end.AddDays(1);
        if (entry.IsRunning && end < start) end = start;
        var minutes = Math.Max(0, (end - start).TotalMinutes - entry.BreakMinutes);
        return minutes / 60d;
    }

    private static string DurationText(WorkTimeEntry entry, DateTime now)
    {
        var hours = CalculateNetHours(entry, now);
        return entry.IsRunning ? $"{hours:0.00} h · läuft" : $"{hours:0.00} h";
    }

    [RelayCommand]
    private void DeleteEntry()
    {
        if (SelectedEntry is null) return;
        if (SelectedEntry.EmployeeId != currentEmployeeId && !SessionService.IsAdministrator)
        {
            StatusMessage = "Du kannst nur deine eigenen Einträge entfernen.";
            return;
        }

        using var db = new AppDbContext();
        var entity = db.WorkTimeEntries.FirstOrDefault(x => x.Id == SelectedEntry.Id);
        if (entity is null) return;
        RecycleBinService.ArchiveDeletion(
            db, entity, entity.Id.ToString(),
            $"Arbeitszeit {SelectedEntry.EmployeeName} · {entity.Date:dd.MM.yyyy} · {entity.Status}");
        db.WorkTimeEntries.Remove(entity);
        db.SaveChanges();
        Load();
        ResetEntryEditor();
        StatusMessage = "Arbeitszeit / Betrieb entfernt und im Papierkorb archiviert.";
    }

    [RelayCommand] private void NewCalendarDay() => ResetCalendarEditor();

    [RelayCommand]
    private void SaveCalendarDay()
    {
        if (!SessionService.IsAdministrator)
        {
            StatusMessage = "Nur Administratoren dürfen den Betriebskalender ändern.";
            return;
        }
        if (string.IsNullOrWhiteSpace(CalendarName))
        {
            StatusMessage = "Bitte eine Bezeichnung eingeben.";
            return;
        }
        if (!double.IsFinite(CalendarTargetHoursFactor) || CalendarTargetHoursFactor < 0 || CalendarTargetHoursFactor > 2)
        {
            StatusMessage = "Sollstunden-Faktor muss eine gültige Zahl zwischen 0 und 2 sein.";
            return;
        }
        if (!CalendarIsWorkingDay) CalendarTargetHoursFactor = 0;

        using var db = new AppDbContext();
        var same = db.OperatingCalendarDays.FirstOrDefault(x => x.Date.Date == CalendarDate.Date);
        OperatingCalendarDay entity;
        if (SelectedCalendarDay is null)
        {
            entity = same ?? new OperatingCalendarDay();
            if (same is null) db.OperatingCalendarDays.Add(entity);
        }
        else
        {
            entity = db.OperatingCalendarDays.First(x => x.Id == SelectedCalendarDay.Id);
            if (same is not null && same.Id != entity.Id)
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
        if (!SessionService.IsAdministrator)
        {
            StatusMessage = "Nur Administratoren dürfen den Betriebskalender ändern.";
            return;
        }
        if (SelectedCalendarDay is null) return;
        using var db = new AppDbContext();
        var entity = db.OperatingCalendarDays.FirstOrDefault(x => x.Id == SelectedCalendarDay.Id);
        if (entity is null) return;
        RecycleBinService.ArchiveDeletion(db, entity, entity.Id.ToString(),
            $"Betriebskalender {entity.Date:dd.MM.yyyy} · {entity.Name}");
        db.OperatingCalendarDays.Remove(entity);
        db.SaveChanges();
        Load();
        ResetCalendarEditor();
        StatusMessage = "Kalender-Ausnahme entfernt und im Papierkorb archiviert; es gilt wieder der Standard Mo–Fr.";
    }

    private void LoadCurrentEmployee()
    {
        currentEmployeeId = null;
        CurrentEmployeeName = "Nicht zugeordnet";
        CanLogTime = false;
        Employees.Clear();

        var current = SessionService.CurrentUser;
        if (current is null) return;

        using var db = new AppDbContext();
        var account = db.UserAccounts.FirstOrDefault(x => x.Id == current.Id);
        var employeeId = account?.EmployeeId ?? current.EmployeeId;
        Employee? employee = null;

        if (employeeId.HasValue)
            employee = db.Employees.AsNoTracking().FirstOrDefault(x => x.Id == employeeId.Value && x.IsActive);

        if (employee is null)
        {
            var active = db.Employees.AsNoTracking().Where(x => x.IsActive).ToList();
            employee = active.FirstOrDefault(x =>
                string.Equals(x.PersonnelNumber, current.Username, StringComparison.OrdinalIgnoreCase));
            if (employee is null)
            {
                var display = current.DisplayName.Trim();
                var matches = active.Where(x =>
                    string.Equals($"{x.FirstName} {x.LastName}", display, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals($"{x.LastName}, {x.FirstName}", display, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 1) employee = matches[0];
            }

            if (employee is not null && account is not null)
            {
                account.EmployeeId = employee.Id;
                db.SaveChanges();
                current.EmployeeId = employee.Id;
            }
        }

        if (employee is null) return;

        currentEmployeeId = employee.Id;
        CurrentEmployeeName = $"{employee.FirstName} {employee.LastName} · {employee.PersonnelNumber}";
        CanLogTime = true;
        var option = new EmployeeOption
        {
            Id = employee.Id,
            DisplayName = $"{employee.LastName}, {employee.FirstName}",
            ShortName = employee.LastName,
            Initials = EmployeeInitialsService.Build3(employee.FirstName, employee.LastName),
            Role = employee.Role
        };
        Employees.Add(option);
        SelectedEmployee = option;
    }

    private void LoadStatuses()
    {
        using var db = new AppDbContext();
        var current = EntryStatus;
        StatusOptions.Clear();
        foreach (var status in db.WorkTimeStatuses.AsNoTracking()
                     .OrderByDescending(x => x.LastUsedAtUtc)
                     .ThenBy(x => x.Name)
                     .Select(x => x.Name)
                     .ToList())
            StatusOptions.Add(status);
        EntryStatus = current;
    }

    private void LoadArticlesAndOrders()
    {
        using var db = new AppDbContext();
        Articles.Clear();
        foreach (var article in db.ArticleMasters.AsNoTracking()
                     .Where(x => x.IsActive)
                     .OrderBy(x => x.ArticleNumber)
                     .ThenBy(x => x.Name))
        {
            Articles.Add(new WorkTimeArticleOption
            {
                Id = article.Id,
                ArticleNumber = article.ArticleNumber,
                Name = article.Name
            });
        }

        allOrders.Clear();
        foreach (var order in db.ProductionOrders.AsNoTracking()
                     .Include(x => x.ArticleMaster)
                     .OrderByDescending(x => x.PlannedDate)
                     .ThenBy(x => x.OrderNumber)
                     .ToList())
        {
            allOrders.Add(new WorkTimeOrderOption
            {
                Id = order.Id,
                ArticleNumber = !string.IsNullOrWhiteSpace(order.ArticleNumber)
                    ? order.ArticleNumber
                    : order.ArticleMaster?.ArticleNumber ?? string.Empty,
                OrderNumber = order.OrderNumber,
                BatchNumber = order.BatchNumber,
                Product = order.Product
            });
        }
        RefreshOrdersForArticle();
    }

    private void RefreshOrdersForArticle(int? preferredOrderId = null)
    {
        var desired = preferredOrderId ?? SelectedOrder?.Id;
        var articleNumber = SelectedArticle?.ArticleNumber;
        Orders.Clear();
        foreach (var order in allOrders.Where(x =>
                     string.IsNullOrWhiteSpace(articleNumber) ||
                     string.Equals(x.ArticleNumber, articleNumber, StringComparison.OrdinalIgnoreCase)))
            Orders.Add(order);
        SelectedOrder = desired.HasValue ? Orders.FirstOrDefault(x => x.Id == desired.Value) : null;
    }

    private void Load(int? entrySelectId = null, int? calendarSelectId = null)
    {
        using var db = new AppDbContext();
        var start = Month.Date;
        var end = start.AddMonths(1).AddDays(-1);
        var now = DateTime.Now;

        var query = db.WorkTimeEntries.AsNoTracking()
            .Include(x => x.Employee)
            .Where(x => x.Date.Date >= start && x.Date.Date <= end);
        if (!SessionService.IsAdministrator && currentEmployeeId.HasValue)
            query = query.Where(x => x.EmployeeId == currentEmployeeId.Value);

        var entries = query.AsEnumerable()
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
                TimeText = x.IsRunning ? $"{x.StartTime:hh\\:mm}–läuft" : $"{x.StartTime:hh\\:mm}–{x.EndTime:hh\\:mm}",
                BreakMinutes = x.BreakMinutes,
                NetHours = CalculateNetHours(x, now),
                DurationText = DurationText(x, now),
                ArticleNumber = x.ArticleNumber,
                BatchNumber = x.BatchNumber,
                OrderNumber = x.OrderNumber,
                Status = x.Status,
                IsRunning = x.IsRunning,
                Comment = x.Comment
            });
        }

        var employees = db.Employees.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToList();
        var allMonthEntries = db.WorkTimeEntries.AsNoTracking()
            .Where(x => x.Date.Date >= start && x.Date.Date <= end).ToList();
        var planned = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date >= start && x.Date.Date <= end).ToList();
        var calendar = db.OperatingCalendarDays.AsNoTracking()
            .Where(x => x.Date.Date >= start && x.Date.Date <= end).OrderBy(x => x.Date).ToList();
        var map = calendar.ToDictionary(x => x.Date.Date);

        Balances.Clear();
        foreach (var employee in employees)
        {
            var target = OperatingCalendarService.GetTargetHours(employee, start, end, map);
            var actual = allMonthEntries.Where(x => x.EmployeeId == employee.Id)
                .Sum(x => CalculateNetHours(x, now));
            var planHours = planned.Where(x => x.EmployeeId == employee.Id)
                .Sum(x => OperatingCalendarService.CalculateNetHours(x.StartTime, x.EndTime, x.BreakMinutes));
            Balances.Add(new WorkTimeBalanceRow
            {
                EmployeeName = $"{employee.LastName}, {employee.FirstName}",
                TargetHours = target,
                PlannedHours = planHours,
                ActualHours = actual,
                BalanceHours = actual - target
            });
        }

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

        HasRunningEntry = currentEmployeeId.HasValue &&
            db.WorkTimeEntries.AsNoTracking().Any(x => x.EmployeeId == currentEmployeeId.Value && x.IsRunning);
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
    public string DurationText { get; set; } = string.Empty;
    public string ArticleNumber { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
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

public sealed class WorkTimeArticleOption
{
    public int Id { get; set; }
    public string ArticleNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName => $"{ArticleNumber} · {Name}";
}

public sealed class WorkTimeOrderOption
{
    public int Id { get; set; }
    public string ArticleNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(BatchNumber)
        ? $"{OrderNumber} · {Product}"
        : $"{BatchNumber} · {OrderNumber} · {Product}";
}
