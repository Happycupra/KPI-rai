using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class ProductionActualViewModel : ObservableObject
{
    public ObservableCollection<ProductionActualRow> Actuals { get; } = new();
    public ObservableCollection<ProductionOrderActualOption> Orders { get; } = new();
    public ObservableCollection<DowntimeEntryRow> Downtimes { get; } = new();
    public IReadOnlyList<string> DowntimeReasons { get; } = new[]
    {
        "Störung", "Materialmangel", "Umrüstung", "Reinigung", "Qualität", "Personal", "Wartung", "Sonstiges"
    };

    [ObservableProperty] private ProductionActualRow? selectedActual;
    [ObservableProperty] private ProductionOrderActualOption? selectedOrder;
    [ObservableProperty] private DowntimeEntryRow? selectedDowntime;
    [ObservableProperty] private DateTime actualDate = DateTime.Today;
    [ObservableProperty] private double totalQuantity;
    [ObservableProperty] private double goodQuantity;
    [ObservableProperty] private double scrapQuantity;
    [ObservableProperty] private double plannedProductionMinutes = 450;
    [ObservableProperty] private double runMinutes;
    [ObservableProperty] private double idealRatePerHour = 1;
    [ObservableProperty] private string comment = string.Empty;
    [ObservableProperty] private string downtimeReason = "Störung";
    [ObservableProperty] private double downtimeMinutes;
    [ObservableProperty] private string downtimeComment = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string availabilityText = "0.0 %";
    [ObservableProperty] private string performanceText = "0.0 %";
    [ObservableProperty] private string qualityText = "0.0 %";
    [ObservableProperty] private string oeeText = "0.0 %";
    [ObservableProperty] private string theoreticalQuantityText = "0";

    public ProductionActualViewModel()
    {
        LoadOrders();
        LoadActuals();
        NewActual();
    }

    partial void OnSelectedActualChanged(ProductionActualRow? value)
    {
        if (value is null) return;

        SelectedOrder = Orders.FirstOrDefault(x => x.Id == value.ProductionOrderId);
        ActualDate = value.Date;
        TotalQuantity = value.TotalQuantity;
        GoodQuantity = value.GoodQuantity;
        ScrapQuantity = value.ScrapQuantity;
        PlannedProductionMinutes = value.PlannedProductionMinutes;
        RunMinutes = value.RunMinutes;
        IdealRatePerHour = value.IdealRatePerHour;
        Comment = value.Comment ?? string.Empty;
        StatusMessage = string.Empty;
        LoadDowntimes(value.Id);
        RefreshPreview();
    }

    partial void OnSelectedOrderChanged(ProductionOrderActualOption? value)
    {
        if (value is null || SelectedActual is not null) return;
        ApplyOrderDefaults(value);
    }

    partial void OnPlannedProductionMinutesChanged(double value) => RefreshPreview();
    partial void OnRunMinutesChanged(double value) => RefreshPreview();
    partial void OnTotalQuantityChanged(double value) => RefreshPreview();
    partial void OnGoodQuantityChanged(double value) => RefreshPreview();
    partial void OnIdealRatePerHourChanged(double value) => RefreshPreview();

    [RelayCommand]
    private void NewActual()
    {
        SelectedActual = null;
        SelectedDowntime = null;
        Downtimes.Clear();
        SelectedOrder = Orders.FirstOrDefault();
        ActualDate = SelectedOrder?.PlannedDate ?? DateTime.Today;
        TotalQuantity = 0;
        GoodQuantity = 0;
        ScrapQuantity = 0;
        Comment = string.Empty;
        DowntimeReason = "Störung";
        DowntimeMinutes = 0;
        DowntimeComment = string.Empty;
        StatusMessage = string.Empty;

        if (SelectedOrder is not null)
            ApplyOrderDefaults(SelectedOrder);
        else
        {
            PlannedProductionMinutes = 450;
            RunMinutes = 0;
            IdealRatePerHour = 1;
        }

        RefreshPreview();
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedOrder is null)
        {
            StatusMessage = "Bitte einen Produktionsauftrag auswählen.";
            return;
        }

        if (PlannedProductionMinutes <= 0)
        {
            StatusMessage = "Die geplante Produktionszeit muss grösser als 0 sein.";
            return;
        }

        if (RunMinutes < 0 || RunMinutes > PlannedProductionMinutes)
        {
            StatusMessage = "Die Laufzeit muss zwischen 0 und der geplanten Produktionszeit liegen.";
            return;
        }

        if (TotalQuantity < 0 || GoodQuantity < 0 || ScrapQuantity < 0)
        {
            StatusMessage = "Mengen dürfen nicht negativ sein.";
            return;
        }

        if (GoodQuantity > TotalQuantity || ScrapQuantity > TotalQuantity)
        {
            StatusMessage = "Gutmenge und Ausschuss dürfen die Gesamtmenge nicht überschreiten.";
            return;
        }

        if (Math.Abs(TotalQuantity - (GoodQuantity + ScrapQuantity)) > 0.01)
        {
            StatusMessage = "Gesamtmenge muss Gutmenge + Ausschuss entsprechen.";
            return;
        }

        if (IdealRatePerHour <= 0)
        {
            StatusMessage = "Die Sollrate pro Stunde muss grösser als 0 sein.";
            return;
        }

        using var db = new AppDbContext();
        ProductionActual entity;
        if (SelectedActual is null)
        {
            entity = new ProductionActual();
            db.ProductionActuals.Add(entity);
        }
        else
        {
            entity = db.ProductionActuals.First(x => x.Id == SelectedActual.Id);
        }

        var existingDowntime = db.DowntimeEntries
            .Where(x => x.ProductionActualId == entity.Id).Sum(x => x.Minutes);
        if (existingDowntime > PlannedProductionMinutes - RunMinutes + 0.01)
        {
            StatusMessage = "Die gespeicherten Stillstände überschreiten die neue Verlustzeit. Bitte zuerst die Stillstände korrigieren.";
            return;
        }

        entity.ProductionOrderId = SelectedOrder.Id;
        entity.Date = ActualDate.Date;
        entity.TotalQuantity = TotalQuantity;
        entity.GoodQuantity = GoodQuantity;
        entity.ScrapQuantity = ScrapQuantity;
        entity.PlannedProductionMinutes = PlannedProductionMinutes;
        entity.RunMinutes = RunMinutes;
        entity.IdealRatePerHour = IdealRatePerHour;
        entity.Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();

        db.SaveChanges();
        LoadActuals(entity.Id);
        StatusMessage = "Ist-Produktion gespeichert.";
    }

    [RelayCommand]
    private void DeleteActual()
    {
        if (SelectedActual is null)
        {
            StatusMessage = "Bitte zuerst eine Ist-Erfassung auswählen.";
            return;
        }

        using var db = new AppDbContext();
        var entity = db.ProductionActuals.FirstOrDefault(x => x.Id == SelectedActual.Id);
        if (entity is null)
        {
            StatusMessage = "Die Ist-Erfassung wurde nicht mehr gefunden.";
            LoadActuals();
            return;
        }

        db.ProductionActuals.Remove(entity);
        db.SaveChanges();
        LoadActuals();
        NewActual();
        StatusMessage = "Ist-Erfassung inklusive Stillstandsdetails gelöscht.";
    }

    [RelayCommand]
    private void AddDowntime()
    {
        if (SelectedActual is null)
        {
            StatusMessage = "Bitte die Ist-Erfassung zuerst speichern, bevor Stillstände erfasst werden.";
            return;
        }

        if (DowntimeMinutes <= 0)
        {
            StatusMessage = "Die Stillstandsdauer muss grösser als 0 sein.";
            return;
        }

        using var db = new AppDbContext();
        var actual = db.ProductionActuals.FirstOrDefault(x => x.Id == SelectedActual.Id);
        if (actual is null)
        {
            StatusMessage = "Die Ist-Erfassung wurde nicht mehr gefunden. Bitte aktualisieren.";
            return;
        }
        var availableLossMinutes = Math.Max(0, actual.PlannedProductionMinutes - actual.RunMinutes);
        var existingDowntime = db.DowntimeEntries
            .Where(x => x.ProductionActualId == actual.Id).Sum(x => x.Minutes);
        if (existingDowntime + DowntimeMinutes > availableLossMinutes + 0.01)
        {
            StatusMessage = $"Stillstandszeiten überschreiten die verfügbare Verlustzeit von {availableLossMinutes:N0} Minuten.";
            return;
        }

        db.DowntimeEntries.Add(new DowntimeEntry
        {
            ProductionActualId = SelectedActual.Id,
            Reason = DowntimeReason,
            Minutes = DowntimeMinutes,
            Comment = string.IsNullOrWhiteSpace(DowntimeComment) ? null : DowntimeComment.Trim()
        });
        db.SaveChanges();

        DowntimeMinutes = 0;
        DowntimeComment = string.Empty;
        LoadDowntimes(SelectedActual.Id);
        LoadActuals(SelectedActual.Id);
        StatusMessage = "Stillstand erfasst.";
    }

    [RelayCommand]
    private void DeleteDowntime()
    {
        if (SelectedDowntime is null)
        {
            StatusMessage = "Bitte zuerst einen Stillstand auswählen.";
            return;
        }

        using var db = new AppDbContext();
        var entity = db.DowntimeEntries.FirstOrDefault(x => x.Id == SelectedDowntime.Id);
        if (entity is null)
        {
            StatusMessage = "Der Stillstand wurde nicht mehr gefunden.";
            return;
        }

        var actualId = entity.ProductionActualId;
        db.DowntimeEntries.Remove(entity);
        db.SaveChanges();
        LoadDowntimes(actualId);
        LoadActuals(actualId);
        StatusMessage = "Stillstand gelöscht.";
    }

    [RelayCommand]
    private void Refresh()
    {
        var selectedId = SelectedActual?.Id;
        LoadOrders();
        LoadActuals(selectedId);
        StatusMessage = "Ist-Produktion aktualisiert.";
    }

    private void ApplyOrderDefaults(ProductionOrderActualOption order)
    {
        ActualDate = order.PlannedDate;
        PlannedProductionMinutes = order.PlannedProductionMinutes > 0 ? order.PlannedProductionMinutes : 450;
        RunMinutes = PlannedProductionMinutes;
        IdealRatePerHour = order.DefaultIdealRatePerHour > 0 ? order.DefaultIdealRatePerHour : 1;
    }

    private void LoadOrders()
    {
        using var db = new AppDbContext();
        var selectedId = SelectedOrder?.Id;
        var orders = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .OrderByDescending(x => x.PlannedDate)
            .ThenBy(x => x.OrderNumber)
            .ToList();

        Orders.Clear();
        foreach (var order in orders)
        {
            var plannedMinutes = CalculatePlannedMinutes(order.Shift, order.PlannedStart, order.PlannedEnd);
            var defaultRate = plannedMinutes > 0 && order.Quantity > 0
                ? order.Quantity / (plannedMinutes / 60.0)
                : 1;

            Orders.Add(new ProductionOrderActualOption
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                Product = order.Product,
                Unit = order.Unit,
                PlannedDate = order.PlannedDate,
                WorkstationName = order.Workstation.Name,
                ShiftName = order.Shift?.Name ?? "Individuell",
                PlannedProductionMinutes = plannedMinutes,
                DefaultIdealRatePerHour = defaultRate
            });
        }

        SelectedOrder = Orders.FirstOrDefault(x => x.Id == selectedId) ?? Orders.FirstOrDefault();
    }

    private void LoadActuals(int? selectId = null)
    {
        using var db = new AppDbContext();
        var items = db.ProductionActuals.AsNoTracking()
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Workstation)
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Shift)
            .Include(x => x.Downtimes)
            .OrderByDescending(x => x.Date)
            .ThenBy(x => x.ProductionOrder.OrderNumber)
            .ToList();

        Actuals.Clear();
        foreach (var item in items)
        {
            var metrics = OeeAnalyticsService.Calculate(
                item.PlannedProductionMinutes,
                item.RunMinutes,
                item.TotalQuantity,
                item.GoodQuantity,
                item.IdealRatePerHour);

            Actuals.Add(new ProductionActualRow
            {
                Id = item.Id,
                ProductionOrderId = item.ProductionOrderId,
                Date = item.Date,
                OrderNumber = item.ProductionOrder.OrderNumber,
                Product = item.ProductionOrder.Product,
                Unit = item.ProductionOrder.Unit,
                WorkstationName = item.ProductionOrder.Workstation.Name,
                ShiftName = item.ProductionOrder.Shift?.Name ?? "Individuell",
                TotalQuantity = item.TotalQuantity,
                GoodQuantity = item.GoodQuantity,
                ScrapQuantity = item.ScrapQuantity,
                PlannedProductionMinutes = item.PlannedProductionMinutes,
                RunMinutes = item.RunMinutes,
                DowntimeMinutes = item.Downtimes.Sum(x => x.Minutes),
                IdealRatePerHour = item.IdealRatePerHour,
                AvailabilityPercent = metrics.AvailabilityPercent,
                PerformancePercent = metrics.PerformancePercent,
                QualityPercent = metrics.QualityPercent,
                OeePercent = metrics.OeePercent,
                Comment = item.Comment
            });
        }

        SelectedActual = selectId.HasValue ? Actuals.FirstOrDefault(x => x.Id == selectId.Value) : null;
    }

    private void LoadDowntimes(int productionActualId)
    {
        using var db = new AppDbContext();
        var items = db.DowntimeEntries.AsNoTracking()
            .Where(x => x.ProductionActualId == productionActualId)
            .OrderByDescending(x => x.Minutes)
            .ThenBy(x => x.Reason)
            .ToList();

        Downtimes.Clear();
        foreach (var item in items)
        {
            Downtimes.Add(new DowntimeEntryRow
            {
                Id = item.Id,
                ProductionActualId = item.ProductionActualId,
                Reason = item.Reason,
                Minutes = item.Minutes,
                Comment = item.Comment
            });
        }
    }

    private void RefreshPreview()
    {
        var metrics = OeeAnalyticsService.Calculate(
            PlannedProductionMinutes,
            RunMinutes,
            TotalQuantity,
            GoodQuantity,
            IdealRatePerHour);

        AvailabilityText = $"{metrics.AvailabilityPercent:N1} %";
        PerformanceText = $"{metrics.PerformancePercent:N1} %";
        QualityText = $"{metrics.QualityPercent:N1} %";
        OeeText = $"{metrics.OeePercent:N1} %";
        TheoreticalQuantityText = $"{metrics.TheoreticalQuantity:N0}";
    }

    private static double CalculatePlannedMinutes(Shift? shift, TimeSpan? plannedStart, TimeSpan? plannedEnd)
    {
        if (shift is not null)
        {
            var start = DateTime.Today + shift.StartTime;
            var end = DateTime.Today + shift.EndTime;
            if (end <= start) end = end.AddDays(1);
            return Math.Max(0, (end - start).TotalMinutes - shift.BreakMinutes);
        }

        if (plannedStart.HasValue && plannedEnd.HasValue)
        {
            var start = DateTime.Today + plannedStart.Value;
            var end = DateTime.Today + plannedEnd.Value;
            if (end <= start) end = end.AddDays(1);
            return Math.Max(0, (end - start).TotalMinutes);
        }

        return 0;
    }
}

public class ProductionOrderActualOption
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public DateTime PlannedDate { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public double PlannedProductionMinutes { get; set; }
    public double DefaultIdealRatePerHour { get; set; }
    public string DisplayName => $"{OrderNumber} · {Product} · {WorkstationName} / {ShiftName}";
}

public class ProductionActualRow
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public DateTime Date { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public double TotalQuantity { get; set; }
    public double GoodQuantity { get; set; }
    public double ScrapQuantity { get; set; }
    public double PlannedProductionMinutes { get; set; }
    public double RunMinutes { get; set; }
    public double DowntimeMinutes { get; set; }
    public double IdealRatePerHour { get; set; }
    public double AvailabilityPercent { get; set; }
    public double PerformancePercent { get; set; }
    public double QualityPercent { get; set; }
    public double OeePercent { get; set; }
    public string? Comment { get; set; }
    public string QuantityText => $"{TotalQuantity:N0} {Unit}";
    public string ScrapText => $"{ScrapQuantity:N0} {Unit}";
    public string DowntimeText => $"{DowntimeMinutes:N0} min";
    public string AvailabilityText => $"{AvailabilityPercent:N1} %";
    public string PerformanceText => $"{PerformancePercent:N1} %";
    public string QualityText => $"{QualityPercent:N1} %";
    public string OeeText => $"{OeePercent:N1} %";
}

public class DowntimeEntryRow
{
    public int Id { get; set; }
    public int ProductionActualId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double Minutes { get; set; }
    public string? Comment { get; set; }
    public string MinutesText => $"{Minutes:N0} min";
}
