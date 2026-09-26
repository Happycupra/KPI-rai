using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class ProductionOrderManagementViewModel : ObservableObject
{
    public ObservableCollection<ProductionOrderRow> Orders { get; } = new();
    public ObservableCollection<WorkstationOption> Workstations { get; } = new();
    public ObservableCollection<ShiftOption> Shifts { get; } = new();
    public ObservableCollection<ProductionSchedulePreviewRow> RunSchedulePreview { get; } = new();
    public ICollectionView OrdersView { get; }
    public IReadOnlyList<string> StatusFilters { get; } = new[] { "Alle", "Offen", "Geplant", "Bereit", "Läuft", "Pausiert", "Problem", "Abgeschlossen" };
    public IReadOnlyList<string> Priorities { get; } = new[] { "Niedrig", "Normal", "Hoch", "Dringend" };
    public IReadOnlyList<string> Statuses { get; } = new[] { "Geplant", "Bereit", "Läuft", "Pausiert", "Abgeschlossen", "Problem" };
    public IReadOnlyList<string> Units { get; } = new[] { "Stück", "kg", "g", "l", "ml", "Charge" };

    [ObservableProperty] private ProductionOrderRow? selectedOrder;
    [ObservableProperty] private string orderNumber = string.Empty;
    [ObservableProperty] private string product = string.Empty;
    [ObservableProperty] private string articleNumber = string.Empty;
    [ObservableProperty] private string batchNumber = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private double quantity = 1;
    [ObservableProperty] private string unit = "Stück";
    [ObservableProperty] private string priority = "Normal";
    [ObservableProperty] private DateTime plannedDate = DateTime.Today;
    [ObservableProperty] private WorkstationOption? selectedWorkstation;
    [ObservableProperty] private ShiftOption? selectedShift;
    [ObservableProperty] private int plannedShiftCount = 1;
    [ObservableProperty] private int requiredStaff = 1;
    [ObservableProperty] private string status = "Geplant";
    [ObservableProperty] private string comment = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedStatusFilter = "Alle";

    public string RunScheduleSummary => PlannedShiftCount <= 1
        ? "1 Schicht"
        : $"{PlannedShiftCount} aufeinanderfolgende Schichten";

    public bool IsArticleIdentityLocked => SelectedOrder?.ArticleIdentityLocked == true;
    public bool CanEditArticleIdentity => SelectedOrder is not null && !IsArticleIdentityLocked;
    public bool IsBatchNumberLocked => SelectedOrder?.HasProductionHistory == true;
    public bool CanEditScheduling => SelectedOrder is not null && SelectedOrder.HasActuals == false;
    public string LockedFieldsHint => SelectedOrder is null
        ? "Bitte einen bestehenden Auftrag auswählen oder „Neue Charge“ verwenden."
        : SelectedOrder.HasActuals
            ? "Historische Ist-Daten vorhanden: Artikel, Produkt, Einheit sowie Terminierung sind fix. Zulässige Felder bleiben bearbeitbar."
            : SelectedOrder.HasProductionHistory
                ? "Produktion wurde bereits begonnen: Artikel, Produkt, Einheit und Chargennummer sind fix. Zulässige Felder bleiben bearbeitbar."
                : SelectedOrder.ArticleMasterId.HasValue
                    ? "Artikelstammdaten und Einheit stammen aus dem Artikelverzeichnis und bleiben für diese Charge fix."
                    : string.Empty;

    public ProductionOrderManagementViewModel(bool loadExistingOrders = true)
    {
        OrdersView = CollectionViewSource.GetDefaultView(Orders);
        OrdersView.Filter = MatchesOrderFilter;
        LoadReferenceData();
        if (loadExistingOrders)
            LoadOrders();
        NewOrder();
    }

    public string OrderFilterSummary => $"{OrdersView.Cast<object>().Count()} von {Orders.Count} Aufträgen";

    partial void OnSearchTextChanged(string value)
    {
        OrdersView.Refresh();
        OnPropertyChanged(nameof(OrderFilterSummary));
    }

    partial void OnSelectedStatusFilterChanged(string value)
    {
        OrdersView.Refresh();
        OnPropertyChanged(nameof(OrderFilterSummary));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatusFilter = "Alle";
        OrdersView.Refresh();
        OnPropertyChanged(nameof(OrderFilterSummary));
    }

    private bool MatchesOrderFilter(object item)
    {
        if (item is not ProductionOrderRow row)
            return false;

        var statusMatches = SelectedStatusFilter switch
        {
            "Alle" => true,
            "Offen" => !string.Equals(row.Status, "Abgeschlossen", StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(row.Status, SelectedStatusFilter, StringComparison.OrdinalIgnoreCase)
        };
        if (!statusMatches)
            return false;

        var term = SearchText?.Trim();
        if (string.IsNullOrWhiteSpace(term))
            return true;

        return row.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.ArticleNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.BatchNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.Product.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.WorkstationName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.ShiftName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.Status.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.Priority.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSelectedOrderChanged(ProductionOrderRow? value)
    {
        if (value is null)
            return;

        OrderNumber = value.OrderNumber;
        Product = value.Product;
        ArticleNumber = value.ArticleNumber;
        BatchNumber = value.BatchNumber;
        Description = value.Description ?? string.Empty;
        Quantity = value.Quantity;
        Unit = value.Unit;
        Priority = value.Priority;
        PlannedDate = value.PlannedDate;
        SelectedWorkstation = Workstations.FirstOrDefault(x => x.Id == value.WorkstationId);
        RefreshAllowedShifts(value.ShiftId, includePreferredEvenIfNotAllowed: true);
        SelectedShift = Shifts.FirstOrDefault(x => x.Id == value.ShiftId);
        PlannedShiftCount = Math.Max(1, value.PlannedShiftCount);
        RequiredStaff = value.RequiredStaff;
        Status = value.Status;
        Comment = value.Comment ?? string.Empty;
        StatusMessage = value.HasActuals
            ? "Historie vorhanden: historisch kritische Felder sind gesperrt; zulässige Korrekturen bleiben möglich."
            : value.HasProductionHistory
                ? "Produktion begonnen: Artikelidentität und Chargennummer sind gesperrt; zulässige Korrekturen bleiben möglich."
                : string.Empty;
        OnPropertyChanged(nameof(IsArticleIdentityLocked));
        OnPropertyChanged(nameof(CanEditArticleIdentity));
        OnPropertyChanged(nameof(IsBatchNumberLocked));
        OnPropertyChanged(nameof(CanEditScheduling));
        OnPropertyChanged(nameof(LockedFieldsHint));
        RefreshRunSchedulePreview();
    }

    partial void OnPlannedDateChanged(DateTime value)
    {
        RefreshAllowedShifts(SelectedShift?.Id, includePreferredEvenIfNotAllowed: SelectedOrder is not null);
        RefreshRunSchedulePreview();
    }

    partial void OnSelectedShiftChanged(ShiftOption? value) => RefreshRunSchedulePreview();

    partial void OnPlannedShiftCountChanged(int value)
    {
        OnPropertyChanged(nameof(RunScheduleSummary));
        RefreshRunSchedulePreview();
    }

    partial void OnSelectedWorkstationChanged(WorkstationOption? value)
    {
        if (value is not null && SelectedOrder is null)
            RequiredStaff = Math.Max(1, GetDefaultRequiredStaff(value.Id));

        RefreshAllowedShifts(SelectedShift?.Id, includePreferredEvenIfNotAllowed: SelectedOrder is not null);
        RefreshRunSchedulePreview();
    }

    [RelayCommand]
    private void NewOrder()
    {
        SelectedOrder = null;
        OrderNumber = Product = ArticleNumber = BatchNumber = Description = string.Empty;
        Quantity = 1;
        Unit = "Stück";
        Priority = "Normal";
        PlannedDate = DateTime.Today;
        SelectedWorkstation = Workstations.FirstOrDefault();
        RefreshAllowedShifts();
        PlannedShiftCount = 1;
        RequiredStaff = SelectedWorkstation is null ? 1 : Math.Max(1, GetDefaultRequiredStaff(SelectedWorkstation.Id));
        Status = "Geplant";
        Comment = StatusMessage = string.Empty;
        RefreshRunSchedulePreview();
    }

    [RelayCommand]
    private void Save()
    {
        if (!SessionService.IsPlannerOrAdmin) { StatusMessage = "Nur Planer oder Administratoren dürfen Produktionsdaten ändern."; return; }
        if (SelectedOrder is null) { StatusMessage = "Bitte „Neue Charge“ verwenden und einen Artikel auswählen."; return; }
        var orderNumber = OrderNumber.Trim();
        var product = Product.Trim();
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            StatusMessage = "Bitte eine Auftragsnummer eingeben.";
            return;
        }
        if (string.IsNullOrWhiteSpace(product))
        {
            StatusMessage = "Bitte ein Produkt eingeben.";
            return;
        }
        if (!double.IsFinite(Quantity) || Quantity <= 0)
        {
            StatusMessage = "Die Menge muss eine gültige Zahl grösser als 0 sein.";
            return;
        }
        if (RequiredStaff <= 0)
        {
            StatusMessage = "Der Personalbedarf muss mindestens 1 betragen.";
            return;
        }
        if (PlannedShiftCount is < 1 or > ProductionScheduleService.MaxPlannedShiftCount)
        {
            StatusMessage = "Ungültige Laufdauer.";
            return;
        }
        if (SelectedWorkstation is null || SelectedShift is null)
        {
            StatusMessage = "Bitte Arbeitsplatz und eine freigegebene Startschicht auswählen.";
            return;
        }

        using var db = new AppDbContext();
        var editingId = SelectedOrder?.Id;
        if (db.ProductionOrders.AsNoTracking().Any(x =>
                x.OrderNumber == orderNumber &&
                (!editingId.HasValue || x.Id != editingId.Value)))
        {
            StatusMessage = "Diese Auftragsnummer existiert bereits.";
            return;
        }

        var existing = editingId.HasValue
            ? db.ProductionOrders.AsNoTracking().FirstOrDefault(x => x.Id == editingId.Value)
            : null;
        if (!BatchService.CanEdit(db, editingId!.Value, out var editError)) { StatusMessage = editError; return; }
        if (!Statuses.Contains(Status) || !Priorities.Contains(Priority)) { StatusMessage = "Ungültiger Status oder Priorität."; return; }
        if (Status == "Abgeschlossen" && db.JobCards.Any(x => x.ProductionOrderId == editingId && x.Status != "Fertig"))
        { StatusMessage = "Zuerst alle Arbeitsgänge abschliessen."; return; }
        if (existing is null) { StatusMessage = "Der Auftrag existiert nicht mehr."; return; }
        var hasProduction = existing.StartedAtUtc.HasValue || db.JobCards.Any(x => x.ProductionOrderId == editingId && (x.Status != "Bereit" || x.RunMinutes > 0)) || db.ProductionActuals.Any(x => x.ProductionOrderId == editingId);
        if ((hasProduction || existing.ArticleMasterId.HasValue) && (existing.ArticleNumber != ArticleNumber.Trim() || existing.Product != product || existing.Unit != Unit) ||
            hasProduction && existing.BatchNumber != BatchNumber.Trim())
        { StatusMessage = "Artikelidentität und historische Chargennummer sind gesperrt."; return; }
        if (existing.ArticleMasterId.HasValue && (string.IsNullOrWhiteSpace(BatchNumber) || db.ProductionOrders.Any(x => x.Id != editingId && (x.ArticleMasterId == existing.ArticleMasterId || x.ArticleNumber == existing.ArticleNumber) && x.BatchNumber == BatchNumber.Trim())))
        { StatusMessage = "Chargennummer fehlt oder ist für diesen Artikel bereits vergeben."; return; }
        var hasActuals = editingId.HasValue &&
                         db.ProductionActuals.AsNoTracking().Any(x => x.ProductionOrderId == editingId.Value);
        var schedulingChanged = existing is null ||
                                existing.WorkstationId != SelectedWorkstation.Id ||
                                existing.ShiftId != SelectedShift.Id ||
                                existing.PlannedDate.Date != PlannedDate.Date ||
                                existing.PlannedShiftCount != PlannedShiftCount;

        if (hasActuals && existing is not null &&
            (existing.Product != product ||
             existing.Unit != Unit ||
             schedulingChanged))
        {
            StatusMessage = "Historische Ist-Daten vorhanden: Produkt, Einheit, Arbeitsplatz, Startschicht, Datum und Schichtanzahl dürfen nicht mehr geändert werden. Für eine Korrektur bitte einen neuen Auftrag anlegen.";
            return;
        }

        if (schedulingChanged)
        {
            var schedule = ProductionScheduleService.BuildPreview(
                PlannedDate,
                SelectedWorkstation.Id,
                SelectedShift.Id,
                PlannedShiftCount);
            if (schedule.Count != PlannedShiftCount)
            {
                StatusMessage = "Der Auftrag kann nicht gespeichert werden: Terminierung ist für den Arbeitsplatz nicht vollständig freigegeben.";
                return;
            }
        }

        using var tx = db.Database.BeginTransaction();
        try
        {
            ProductionOrder entity;
            if (editingId.HasValue)
                entity = db.ProductionOrders.First(x => x.Id == editingId.Value);
            else
            {
                entity = new ProductionOrder();
                db.ProductionOrders.Add(entity);
            }

            entity.OrderNumber = orderNumber;
            entity.Product = product;
            entity.ArticleNumber = ArticleNumber.Trim();
            entity.BatchNumber = BatchNumber.Trim();
            entity.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            entity.Quantity = Quantity;
            entity.Unit = Unit;
            entity.Priority = Priority;
            entity.PlannedDate = PlannedDate.Date;
            entity.WorkstationId = SelectedWorkstation.Id;
            entity.ShiftId = SelectedShift.Id;
            entity.PlannedStart = SelectedShift.StartTime;
            entity.PlannedEnd = SelectedShift.EndTime;
            entity.PlannedShiftCount = PlannedShiftCount;
            entity.RequiredStaff = RequiredStaff;
            entity.Status = Status;
            if (Status == "Läuft") entity.StartedAtUtc ??= DateTime.UtcNow;
            if (Status == "Abgeschlossen") entity.CompletedAtUtc = DateTime.UtcNow;
            entity.Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();
            db.SaveChanges();

            // Vorhandene Läufe bleiben bei reinen Status-/Prioritäts-/Kommentaränderungen
            // unverändert. Neu geplant wird nur bei einer tatsächlichen Terminänderung.
            if (!hasActuals && schedulingChanged)
            {
                ProductionScheduleService.SyncRunSlots(db, entity);
                db.SaveChanges();
                var count = db.ProductionRunSlots.Count(x => x.ProductionOrderId == entity.Id);
                if (count != PlannedShiftCount)
                    throw new InvalidOperationException($"Terminplanung unvollständig ({count}/{PlannedShiftCount}).");
            }

            tx.Commit();
            LoadOrders(entity.Id);
            StatusMessage = hasActuals
                ? "Auftrag aktualisiert; historische Stammdaten und Produktionsschichten blieben unverändert."
                : schedulingChanged
                    ? "Produktionsauftrag und Produktionsschichten gespeichert."
                    : "Produktionsauftrag aktualisiert; bestehende Produktionsschichten blieben unverändert.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            StatusMessage = $"Produktionsauftrag wurde nicht gespeichert: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedOrder is null)
        {
            StatusMessage = "Bitte zuerst einen Produktionsauftrag auswählen.";
            return;
        }
        if (!SessionService.IsPlannerOrAdmin)
        {
            StatusMessage = "Nur Planer oder Administratoren dürfen Produktionsdaten ändern.";
            return;
        }

        var batchText = string.IsNullOrWhiteSpace(SelectedOrder.BatchNumber)
            ? "ohne Chargennummer"
            : $"Charge {SelectedOrder.BatchNumber}";
        if (MessageBox.Show(
                $"Auftrag {SelectedOrder.OrderNumber} / {batchText} wirklich in den Papierkorb verschieben?\n\n" +
                "Produktionsdaten, Arbeitskarten und Historie bleiben vollständig erhalten und der Datensatz kann durch einen Administrator wiederhergestellt werden.",
                "Charge / Auftrag in Papierkorb",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            RecycleBinService.MoveProductionOrderToTrash(SelectedOrder.Id);
            LoadOrders();
            NewOrder();
            StatusMessage = "Produktionsauftrag in den Papierkorb verschoben.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Produktionsauftrag konnte nicht in den Papierkorb verschoben werden: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadReferenceData();
        LoadOrders(SelectedOrder?.Id);
        StatusMessage = "Produktionsaufträge aktualisiert.";
    }

    private void RefreshRunSchedulePreview()
    {
        RunSchedulePreview.Clear();
        if (SelectedWorkstation is null || SelectedShift is null || PlannedShiftCount <= 0)
            return;

        foreach (var slot in ProductionScheduleService.BuildPreview(
                     PlannedDate,
                     SelectedWorkstation.Id,
                     SelectedShift.Id,
                     PlannedShiftCount))
        {
            RunSchedulePreview.Add(new ProductionSchedulePreviewRow
            {
                SequenceNumber = slot.SequenceNumber,
                Date = slot.Date,
                ShiftName = slot.ShiftName,
                TimeText = slot.TimeText
            });
        }
    }

    private void RefreshAllowedShifts(int? preferredShiftId = null, bool includePreferredEvenIfNotAllowed = false)
    {
        var desiredShiftId = preferredShiftId ?? SelectedShift?.Id;
        Shifts.Clear();
        if (SelectedWorkstation is null)
        {
            SelectedShift = null;
            return;
        }

        using var db = new AppDbContext();
        var allowed = ProductionScheduleService.LoadAllowedShiftsForDate(db, SelectedWorkstation.Id, PlannedDate);
        foreach (var shift in allowed)
            AddShiftOption(shift);

        if (includePreferredEvenIfNotAllowed &&
            desiredShiftId.HasValue &&
            Shifts.All(x => x.Id != desiredShiftId.Value))
        {
            var existingShift = db.Shifts.AsNoTracking().FirstOrDefault(x => x.Id == desiredShiftId.Value);
            if (existingShift is not null)
                AddShiftOption(existingShift);
        }

        SelectedShift = Shifts.FirstOrDefault(x => x.Id == desiredShiftId) ?? Shifts.FirstOrDefault();
    }

    private void AddShiftOption(Shift shift)
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

    private void LoadReferenceData()
    {
        using var db = new AppDbContext();
        var workstationId = SelectedWorkstation?.Id;
        var shiftId = SelectedShift?.Id;

        Workstations.Clear();
        foreach (var workstation in db.Workstations.AsNoTracking()
                     .Where(x => x.IsActive)
                     .OrderBy(x => x.Name))
            Workstations.Add(new WorkstationOption { Id = workstation.Id, Name = workstation.Name });

        SelectedWorkstation = Workstations.FirstOrDefault(x => x.Id == workstationId) ?? Workstations.FirstOrDefault();
        RefreshAllowedShifts(shiftId, includePreferredEvenIfNotAllowed: SelectedOrder is not null);
        RefreshRunSchedulePreview();
    }

    private int GetDefaultRequiredStaff(int id)
    {
        using var db = new AppDbContext();
        return db.Workstations.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.OptimalStaff)
            .FirstOrDefault();
    }

    private void LoadOrders(int? selectId = null)
    {
        using var db = new AppDbContext();
        var actualOrderIds = db.ProductionActuals.AsNoTracking()
            .Select(x => x.ProductionOrderId)
            .Distinct()
            .ToHashSet();
        var productionHistoryIds = db.JobCards.AsNoTracking()
            .Where(x => x.Status != "Bereit" || x.RunMinutes > 0 || x.StartedAtUtc.HasValue)
            .Select(x => x.ProductionOrderId)
            .Distinct()
            .ToHashSet();
        var items = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .AsEnumerable()
            .OrderBy(x => x.PlannedDate)
            .ToList();

        Orders.Clear();
        foreach (var item in items)
        {
            Orders.Add(new ProductionOrderRow
            {
                Id = item.Id,
                ArticleMasterId = item.ArticleMasterId,
                OrderNumber = item.OrderNumber,
                Product = item.Product,
                ArticleNumber = item.ArticleNumber,
                BatchNumber = item.BatchNumber,
                Description = item.Description,
                Quantity = item.Quantity,
                Unit = item.Unit,
                Priority = item.Priority,
                PlannedDate = item.PlannedDate,
                WorkstationId = item.WorkstationId,
                WorkstationName = item.Workstation?.Name ?? "Arbeitsplatz nicht mehr vorhanden",
                ShiftId = item.ShiftId,
                ShiftName = item.Shift?.Name ?? "Individuell",
                PlannedShiftCount = Math.Max(1, item.PlannedShiftCount),
                RequiredStaff = item.RequiredStaff,
                Status = item.Status,
                Comment = item.Comment,
                HasActuals = actualOrderIds.Contains(item.Id),
                HasProductionHistory = item.StartedAtUtc.HasValue || actualOrderIds.Contains(item.Id) || productionHistoryIds.Contains(item.Id)
            });
        }

        OrdersView.Refresh();
        OnPropertyChanged(nameof(OrderFilterSummary));
        SelectedOrder = selectId.HasValue ? Orders.FirstOrDefault(x => x.Id == selectId) : null;
    }
}

public class ProductionOrderRow
{
    public int Id { get; set; }
    public int? ArticleMasterId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string ArticleNumber { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime PlannedDate { get; set; }
    public int WorkstationId { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public int? ShiftId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public int PlannedShiftCount { get; set; } = 1;
    public int RequiredStaff { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public bool HasActuals { get; set; }
    public bool HasProductionHistory { get; set; }
    public bool ArticleIdentityLocked => ArticleMasterId.HasValue || HasProductionHistory;
    public string QuantityText => $"{Quantity:N0} {Unit}";
    public string RunText => PlannedShiftCount == 1 ? "1 Schicht" : $"{PlannedShiftCount} Schichten";
}

public sealed class ProductionSchedulePreviewRow
{
    public int SequenceNumber { get; set; }
    public DateTime Date { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public string DateText => Date.ToString("ddd dd.MM.");
}
