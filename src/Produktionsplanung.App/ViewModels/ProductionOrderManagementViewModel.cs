using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.ViewModels;

public partial class ProductionOrderManagementViewModel : ObservableObject
{
    public ObservableCollection<ProductionOrderRow> Orders { get; } = new();
    public ObservableCollection<WorkstationOption> Workstations { get; } = new();
    public ObservableCollection<ShiftOption> Shifts { get; } = new();

    public IReadOnlyList<string> Priorities { get; } = new[] { "Niedrig", "Normal", "Hoch", "Dringend" };
    public IReadOnlyList<string> Statuses { get; } = new[] { "Geplant", "Bereit", "Läuft", "Pausiert", "Abgeschlossen", "Problem" };
    public IReadOnlyList<string> Units { get; } = new[] { "Stück", "kg", "g", "l", "ml", "Charge" };

    [ObservableProperty] private ProductionOrderRow? selectedOrder;
    [ObservableProperty] private string orderNumber = string.Empty;
    [ObservableProperty] private string product = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private double quantity = 1;
    [ObservableProperty] private string unit = "Stück";
    [ObservableProperty] private string priority = "Normal";
    [ObservableProperty] private DateTime plannedDate = DateTime.Today;
    [ObservableProperty] private WorkstationOption? selectedWorkstation;
    [ObservableProperty] private ShiftOption? selectedShift;
    [ObservableProperty] private int requiredStaff = 1;
    [ObservableProperty] private string status = "Geplant";
    [ObservableProperty] private string comment = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public ProductionOrderManagementViewModel()
    {
        LoadReferenceData();
        LoadOrders();
        NewOrder();
    }

    partial void OnSelectedOrderChanged(ProductionOrderRow? value)
    {
        if (value is null) return;

        OrderNumber = value.OrderNumber;
        Product = value.Product;
        Description = value.Description ?? string.Empty;
        Quantity = value.Quantity;
        Unit = value.Unit;
        Priority = value.Priority;
        PlannedDate = value.PlannedDate;
        SelectedWorkstation = Workstations.FirstOrDefault(x => x.Id == value.WorkstationId);
        SelectedShift = Shifts.FirstOrDefault(x => x.Id == value.ShiftId);
        RequiredStaff = value.RequiredStaff;
        Status = value.Status;
        Comment = value.Comment ?? string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void NewOrder()
    {
        SelectedOrder = null;
        OrderNumber = string.Empty;
        Product = string.Empty;
        Description = string.Empty;
        Quantity = 1;
        Unit = "Stück";
        Priority = "Normal";
        PlannedDate = DateTime.Today;
        SelectedWorkstation = Workstations.FirstOrDefault();
        SelectedShift = Shifts.FirstOrDefault();
        RequiredStaff = SelectedWorkstation is null ? 1 : Math.Max(1, GetDefaultRequiredStaff(SelectedWorkstation.Id));
        Status = "Geplant";
        Comment = string.Empty;
        StatusMessage = string.Empty;
    }

    partial void OnSelectedWorkstationChanged(WorkstationOption? value)
    {
        if (value is not null && SelectedOrder is null)
            RequiredStaff = Math.Max(1, GetDefaultRequiredStaff(value.Id));
    }

    [RelayCommand]
    private void Save()
    {
        var normalizedOrderNumber = OrderNumber.Trim();
        var normalizedProduct = Product.Trim();

        if (string.IsNullOrWhiteSpace(normalizedOrderNumber))
        {
            StatusMessage = "Bitte eine Auftragsnummer eingeben.";
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedProduct))
        {
            StatusMessage = "Bitte ein Produkt eingeben.";
            return;
        }

        if (Quantity <= 0)
        {
            StatusMessage = "Die Menge muss grösser als 0 sein.";
            return;
        }

        if (RequiredStaff <= 0)
        {
            StatusMessage = "Der Personalbedarf muss mindestens 1 betragen.";
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

        using var db = new AppDbContext();
        var editingId = SelectedOrder?.Id;
        var duplicate = db.ProductionOrders.AsNoTracking().Any(x =>
            x.OrderNumber == normalizedOrderNumber && (!editingId.HasValue || x.Id != editingId.Value));

        if (duplicate)
        {
            StatusMessage = "Diese Auftragsnummer existiert bereits.";
            return;
        }

        ProductionOrder entity;
        if (SelectedOrder is null)
        {
            entity = new ProductionOrder();
            db.ProductionOrders.Add(entity);
        }
        else
        {
            entity = db.ProductionOrders.First(x => x.Id == SelectedOrder.Id);
        }

        entity.OrderNumber = normalizedOrderNumber;
        entity.Product = normalizedProduct;
        entity.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        entity.Quantity = Quantity;
        entity.Unit = Unit;
        entity.Priority = Priority;
        entity.PlannedDate = PlannedDate.Date;
        entity.WorkstationId = SelectedWorkstation.Id;
        entity.ShiftId = SelectedShift.Id;
        entity.PlannedStart = SelectedShift.StartTime;
        entity.PlannedEnd = SelectedShift.EndTime;
        entity.RequiredStaff = RequiredStaff;
        entity.Status = Status;
        entity.Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();

        db.SaveChanges();
        LoadOrders(entity.Id);
        StatusMessage = "Produktionsauftrag gespeichert.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedOrder is null)
        {
            StatusMessage = "Bitte zuerst einen Produktionsauftrag auswählen.";
            return;
        }

        using var db = new AppDbContext();
        var entity = db.ProductionOrders.FirstOrDefault(x => x.Id == SelectedOrder.Id);
        if (entity is null)
        {
            StatusMessage = "Der Produktionsauftrag wurde nicht mehr gefunden.";
            LoadOrders();
            return;
        }

        db.ProductionOrders.Remove(entity);
        db.SaveChanges();
        LoadOrders();
        NewOrder();
        StatusMessage = "Produktionsauftrag gelöscht.";
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadReferenceData();
        LoadOrders(SelectedOrder?.Id);
        StatusMessage = "Produktionsaufträge aktualisiert.";
    }

    private void LoadReferenceData()
    {
        using var db = new AppDbContext();
        var workstationId = SelectedWorkstation?.Id;
        var shiftId = SelectedShift?.Id;

        Workstations.Clear();
        foreach (var item in db.Workstations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name))
            Workstations.Add(new WorkstationOption { Id = item.Id, Name = item.Name });

        Shifts.Clear();
        foreach (var item in db.Shifts.AsNoTracking().OrderBy(x => x.StartTime).ThenBy(x => x.Name))
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

        SelectedWorkstation = Workstations.FirstOrDefault(x => x.Id == workstationId) ?? Workstations.FirstOrDefault();
        SelectedShift = Shifts.FirstOrDefault(x => x.Id == shiftId) ?? Shifts.FirstOrDefault();
    }

    private int GetDefaultRequiredStaff(int workstationId)
    {
        using var db = new AppDbContext();
        return db.Workstations.AsNoTracking().Where(x => x.Id == workstationId).Select(x => x.OptimalStaff).FirstOrDefault();
    }

    private void LoadOrders(int? selectId = null)
    {
        using var db = new AppDbContext();
        var items = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.Shift!.StartTime)
            .ThenBy(x => x.OrderNumber)
            .ToList();

        Orders.Clear();
        foreach (var item in items)
        {
            Orders.Add(new ProductionOrderRow
            {
                Id = item.Id,
                OrderNumber = item.OrderNumber,
                Product = item.Product,
                Description = item.Description,
                Quantity = item.Quantity,
                Unit = item.Unit,
                Priority = item.Priority,
                PlannedDate = item.PlannedDate,
                WorkstationId = item.WorkstationId,
                WorkstationName = item.Workstation.Name,
                ShiftId = item.ShiftId,
                ShiftName = item.Shift?.Name ?? "Individuell",
                RequiredStaff = item.RequiredStaff,
                Status = item.Status,
                Comment = item.Comment
            });
        }

        SelectedOrder = selectId.HasValue ? Orders.FirstOrDefault(x => x.Id == selectId.Value) : null;
    }
}

public class ProductionOrderRow
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime PlannedDate { get; set; }
    public int WorkstationId { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public int? ShiftId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public int RequiredStaff { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string QuantityText => $"{Quantity:N0} {Unit}";
}
