using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class ShiftHandoverViewModel : ObservableObject
{
    public ObservableCollection<ShiftHandoverRow> Handovers { get; } = new();
    public ObservableCollection<HandoverLookupOption> ShiftOptions { get; } = new();
    public ObservableCollection<HandoverLookupOption> WorkstationOptions { get; } = new();
    public ObservableCollection<HandoverLookupOption> ProductionOrderOptions { get; } = new();
    public ObservableCollection<HandoverLookupOption> FilterShiftOptions { get; } = new();
    public ObservableCollection<HandoverLookupOption> FilterWorkstationOptions { get; } = new();
    public IReadOnlyList<string> FilterOptions { get; } = new[] { "Offen", "Heute", "Kritisch", "Alle" };
    public IReadOnlyList<string> Priorities { get; } = new[] { "Normal", "Hoch", "Kritisch", "Niedrig" };

    [ObservableProperty] private string selectedFilter = "Offen";
    [ObservableProperty] private HandoverLookupOption? selectedFilterShift;
    [ObservableProperty] private HandoverLookupOption? selectedFilterWorkstation;
    [ObservableProperty] private ShiftHandoverRow? selectedHandover;
    [ObservableProperty] private DateTime handoverDate = DateTime.Today;
    [ObservableProperty] private HandoverLookupOption? selectedFromShift;
    [ObservableProperty] private HandoverLookupOption? selectedToShift;
    [ObservableProperty] private HandoverLookupOption? selectedWorkstation;
    [ObservableProperty] private HandoverLookupOption? selectedProductionOrder;
    [ObservableProperty] private string priority = "Normal";
    [ObservableProperty] private string subject = string.Empty;
    [ObservableProperty] private string details = string.Empty;
    [ObservableProperty] private string resolution = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public bool CanAcknowledge => SelectedHandover is { Status: "Offen" };
    public bool CanResolve => SelectedHandover is not null && SelectedHandover.Status != "Erledigt";

    public ShiftHandoverViewModel(int? selectedHandoverId = null)
    {
        LoadLookups();
        LoadHandovers(selectedHandoverId);
    }

    partial void OnSelectedFilterChanged(string value) => LoadHandovers();
    partial void OnSelectedFilterShiftChanged(HandoverLookupOption? value) => LoadHandovers();
    partial void OnSelectedFilterWorkstationChanged(HandoverLookupOption? value) => LoadHandovers();

    partial void OnSelectedHandoverChanged(ShiftHandoverRow? value)
    {
        Resolution = value?.Resolution ?? string.Empty;
        OnPropertyChanged(nameof(CanAcknowledge));
        OnPropertyChanged(nameof(CanResolve));
    }

    [RelayCommand]
    private void Refresh() => LoadHandovers(SelectedHandover?.Id);

    [RelayCommand]
    private void Create()
    {
        try
        {
            var created = ShiftHandoverService.Create(
                HandoverDate.Date,
                SelectedFromShift?.Id,
                SelectedToShift?.Id,
                SelectedWorkstation?.Id,
                SelectedProductionOrder?.Id,
                Priority,
                Subject,
                Details);
            StatusMessage = "Schichtübergabe wurde erstellt.";
            Subject = string.Empty;
            Details = string.Empty;
            Priority = "Normal";
            SelectedProductionOrder = ProductionOrderOptions.FirstOrDefault();
            LoadHandovers(created.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Acknowledge()
    {
        if (SelectedHandover is null) return;
        try
        {
            var id = SelectedHandover.Id;
            ShiftHandoverService.Acknowledge(id);
            StatusMessage = "Eingang der Übergabe wurde bestätigt.";
            LoadHandovers(id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Resolve()
    {
        if (SelectedHandover is null) return;
        try
        {
            var id = SelectedHandover.Id;
            ShiftHandoverService.Resolve(id, Resolution);
            StatusMessage = "Übergabe wurde mit Abschlussnotiz erledigt.";
            LoadHandovers();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            StatusMessage = ex.Message;
        }
    }

    private void LoadLookups()
    {
        using var db = new AppDbContext();
        var shifts = db.Shifts.AsNoTracking().OrderBy(x => x.StartTime).ThenBy(x => x.Name).ToList();
        var workstations = db.Workstations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToList();
        var orders = db.ProductionOrders.AsNoTracking()
            .OrderByDescending(x => x.PlannedDate)
            .ThenBy(x => x.OrderNumber)
            .Take(250)
            .ToList();

        ShiftOptions.Add(new HandoverLookupOption(null, "Keine Schicht"));
        FilterShiftOptions.Add(new HandoverLookupOption(null, "Alle Schichten"));
        foreach (var shift in shifts)
        {
            var option = new HandoverLookupOption(shift.Id, $"{shift.Name} · {shift.StartTime:hh\\:mm}–{shift.EndTime:hh\\:mm}");
            ShiftOptions.Add(option);
            FilterShiftOptions.Add(option);
        }

        WorkstationOptions.Add(new HandoverLookupOption(null, "Kein Arbeitsplatz"));
        FilterWorkstationOptions.Add(new HandoverLookupOption(null, "Alle Arbeitsplätze"));
        foreach (var workstation in workstations)
        {
            var option = new HandoverLookupOption(workstation.Id, workstation.Name);
            WorkstationOptions.Add(option);
            FilterWorkstationOptions.Add(option);
        }

        ProductionOrderOptions.Add(new HandoverLookupOption(null, "Kein Produktionsauftrag"));
        foreach (var order in orders)
            ProductionOrderOptions.Add(new HandoverLookupOption(order.Id, $"{order.OrderNumber} · {order.Product}"));

        SelectedFromShift = ShiftOptions.FirstOrDefault();
        SelectedToShift = ShiftOptions.FirstOrDefault();
        SelectedWorkstation = WorkstationOptions.FirstOrDefault();
        SelectedProductionOrder = ProductionOrderOptions.FirstOrDefault();
        SelectedFilterShift = FilterShiftOptions.FirstOrDefault();
        SelectedFilterWorkstation = FilterWorkstationOptions.FirstOrDefault();
    }

    private void LoadHandovers(int? selectId = null)
    {
        var includeResolved = SelectedFilter == "Alle";
        var date = SelectedFilter == "Heute" ? DateTime.Today : (DateTime?)null;
        var criticalOnly = SelectedFilter == "Kritisch";
        var items = ShiftHandoverService.Get(date, includeResolved, criticalOnly).AsEnumerable();
        if (SelectedFilterWorkstation?.Id is int workstationId)
            items = items.Where(x => x.WorkstationId == workstationId);
        if (SelectedFilterShift?.Id is int shiftId)
            items = items.Where(x => x.FromShiftId == shiftId || x.ToShiftId == shiftId);

        Handovers.Clear();
        foreach (var item in items)
            Handovers.Add(ShiftHandoverRow.From(item));

        SelectedHandover = selectId.HasValue
            ? Handovers.FirstOrDefault(x => x.Id == selectId.Value)
            : Handovers.FirstOrDefault();
    }
}

public sealed record HandoverLookupOption(int? Id, string DisplayText);

public sealed class ShiftHandoverRow
{
    public int Id { get; init; }
    public DateTime HandoverDate { get; init; }
    public string Priority { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CreatedBy { get; init; } = string.Empty;
    public DateTime CreatedAtLocal { get; init; }
    public string ShiftText { get; init; } = string.Empty;
    public string WorkstationText { get; init; } = string.Empty;
    public string ProductionOrderText { get; init; } = string.Empty;
    public string? AcknowledgementText { get; init; }
    public string? Resolution { get; init; }
    public string? ResolutionText { get; init; }

    public static ShiftHandoverRow From(ShiftHandover item) => new()
    {
        Id = item.Id,
        HandoverDate = item.HandoverDate,
        Priority = item.Priority,
        Subject = item.Subject,
        Details = item.Details,
        Status = item.Status,
        CreatedBy = item.CreatedBy,
        CreatedAtLocal = item.CreatedAtUtc.ToLocalTime(),
        ShiftText = $"{item.FromShift?.Name ?? "Ohne Schicht"} → {item.ToShift?.Name ?? "Ohne Schicht"}",
        WorkstationText = item.Workstation?.Name ?? "Kein Arbeitsplatz",
        ProductionOrderText = item.ProductionOrder?.OrderNumber ?? "Kein Produktionsauftrag",
        AcknowledgementText = item.AcknowledgedAtUtc.HasValue
            ? $"Bestätigt von {item.AcknowledgedBy} · {item.AcknowledgedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
            : null,
        Resolution = item.Resolution,
        ResolutionText = item.ResolvedAtUtc.HasValue
            ? $"Erledigt · {item.ResolvedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
            : null
    };
}
