using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

/// <summary>
/// Small, purpose-built state model for the "Neue Charge" dialog.
/// It intentionally does not load production-order history or the order browser.
/// Initialization errors are surfaced in the dialog instead of escaping into the WPF dispatcher.
/// </summary>
public partial class NewBatchViewModel : ObservableObject
{
    public ObservableCollection<Workstation> Workstations { get; } = new();
    public ObservableCollection<Shift> Shifts { get; } = new();
    public ObservableCollection<ProductionScheduleSlotDefinition> RunSchedulePreview { get; } = new();

    [ObservableProperty] private string orderNumber = string.Empty;
    [ObservableProperty] private string batchNumber = string.Empty;
    [ObservableProperty] private double quantity = 1;
    [ObservableProperty] private string unit = "Stück";
    [ObservableProperty] private DateTime plannedDate = DateTime.Today;
    [ObservableProperty] private Workstation? selectedWorkstation;
    [ObservableProperty] private Shift? selectedShift;
    [ObservableProperty] private int plannedShiftCount = 1;
    [ObservableProperty] private int requiredStaff = 1;
    [ObservableProperty] private string planningError = string.Empty;

    public NewBatchViewModel()
    {
        try
        {
            using var db = new AppDbContext();
            foreach (var workstation in db.Workstations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name))
                Workstations.Add(workstation);

            SelectedWorkstation = Workstations.FirstOrDefault();
            if (SelectedWorkstation is null)
                PlanningError = "Bitte zuerst einen aktiven Arbeitsplatz anlegen.";
        }
        catch (Exception ex)
        {
            SelectedWorkstation = null;
            PlanningError = "Arbeitsplätze konnten nicht geladen werden: " + ex.Message;
        }
    }

    partial void OnPlannedDateChanged(DateTime value) => RefreshAllowedShifts(SelectedShift?.Id);

    partial void OnSelectedWorkstationChanged(Workstation? value)
    {
        if (value is not null)
            RequiredStaff = Math.Max(1, value.OptimalStaff);
        RefreshAllowedShifts(SelectedShift?.Id);
    }

    partial void OnSelectedShiftChanged(Shift? value) => RefreshPreview();
    partial void OnPlannedShiftCountChanged(int value) => RefreshPreview();

    private void RefreshAllowedShifts(int? preferredShiftId)
    {
        Shifts.Clear();
        RunSchedulePreview.Clear();
        PlanningError = string.Empty;
        if (SelectedWorkstation is null)
            return;

        try
        {
            using var db = new AppDbContext();
            var allowed = ProductionScheduleService.LoadAllowedShiftsForDate(db, SelectedWorkstation.Id, PlannedDate);
            foreach (var shift in allowed)
                Shifts.Add(shift);

            SelectedShift = preferredShiftId.HasValue
                ? Shifts.FirstOrDefault(x => x.Id == preferredShiftId.Value) ?? Shifts.FirstOrDefault()
                : Shifts.FirstOrDefault();

            if (SelectedShift is null)
                PlanningError = "Für diesen Arbeitsplatz ist am gewählten Datum keine Schicht freigegeben.";
        }
        catch (Exception ex)
        {
            SelectedShift = null;
            PlanningError = "Schichten konnten nicht geladen werden: " + ex.Message;
        }
    }

    private void RefreshPreview()
    {
        RunSchedulePreview.Clear();
        if (SelectedWorkstation is null || SelectedShift is null || PlannedShiftCount < 1)
            return;

        try
        {
            foreach (var slot in ProductionScheduleService.BuildPreview(
                         PlannedDate,
                         SelectedWorkstation.Id,
                         SelectedShift.Id,
                         Math.Clamp(PlannedShiftCount, 1, ProductionScheduleService.MaxPlannedShiftCount)))
                RunSchedulePreview.Add(slot);

            PlanningError = RunSchedulePreview.Count < Math.Clamp(PlannedShiftCount, 1, ProductionScheduleService.MaxPlannedShiftCount)
                ? "Die gewünschte Anzahl Produktionsschichten ist mit der aktuellen Freigabe nicht vollständig planbar."
                : string.Empty;
        }
        catch (Exception ex)
        {
            PlanningError = "Produktionsschichten konnten nicht berechnet werden: " + ex.Message;
        }
    }
}
