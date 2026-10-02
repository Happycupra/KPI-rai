using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class WhatIfPlanningViewModel : ObservableObject
{
    public ObservableCollection<Workstation> Workstations { get; } = new();
    public ObservableCollection<Shift> Shifts { get; } = new();
    public ObservableCollection<WhatIfAlternative> Alternatives { get; } = new();

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;
    [ObservableProperty] private Workstation? selectedWorkstation;
    [ObservableProperty] private Shift? selectedShift;
    [ObservableProperty] private int plannedStaff;
    [ObservableProperty] private int targetStaff;
    [ObservableProperty] private int missingStaff;
    [ObservableProperty] private string summary = "Datum, Arbeitsplatz und Schicht auswählen und anschließend simulieren.";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool hasSimulation;
    [ObservableProperty] private bool isSimulationValid;

    public WhatIfPlanningViewModel()
    {
        using var db = new AppDbContext();
        foreach (var workstation in db.Workstations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name))
            Workstations.Add(workstation);
        SelectedWorkstation = Workstations.FirstOrDefault();
    }

    partial void OnSelectedDateChanged(DateTime value) => LoadAllowedShifts();
    partial void OnSelectedWorkstationChanged(Workstation? value) => LoadAllowedShifts();
    partial void OnSelectedShiftChanged(Shift? value) => ClearSimulation();

    [RelayCommand]
    private void Simulate()
    {
        StatusMessage = string.Empty;
        Alternatives.Clear();
        if (SelectedWorkstation is null || SelectedShift is null)
        {
            HasSimulation = false;
            IsSimulationValid = false;
            Summary = "Bitte einen Arbeitsplatz und eine freigegebene Schicht auswählen.";
            return;
        }

        var result = WhatIfPlanningService.SimulateStaffingGap(
            SelectedDate.Date,
            SelectedWorkstation.Id,
            SelectedShift.Id);
        PlannedStaff = result.PlannedStaff;
        TargetStaff = result.TargetStaff;
        MissingStaff = result.MissingStaff;
        Summary = result.Summary;
        IsSimulationValid = result.IsValid;
        HasSimulation = true;
        foreach (var alternative in result.Alternatives)
            Alternatives.Add(alternative);
    }

    [RelayCommand]
    private void ApplyAlternative(WhatIfAlternative? alternative)
    {
        if (alternative is null || SelectedWorkstation is null || SelectedShift is null)
            return;

        var result = WhatIfPlanningService.ApplyAlternative(
            SelectedDate.Date,
            SelectedWorkstation.Id,
            SelectedShift.Id,
            alternative.EmployeeId);
        StatusMessage = result.Message;
        Simulate();
        StatusMessage = result.Message;
    }

    private void LoadAllowedShifts()
    {
        var previousId = SelectedShift?.Id;
        Shifts.Clear();
        if (SelectedWorkstation is not null)
        {
            using var db = new AppDbContext();
            foreach (var shift in ProductionScheduleService.LoadAllowedShiftsForDate(db, SelectedWorkstation.Id, SelectedDate.Date))
                Shifts.Add(shift);
        }

        SelectedShift = Shifts.FirstOrDefault(x => x.Id == previousId) ?? Shifts.FirstOrDefault();
        ClearSimulation();
    }

    private void ClearSimulation()
    {
        Alternatives.Clear();
        HasSimulation = false;
        IsSimulationValid = false;
        PlannedStaff = 0;
        TargetStaff = 0;
        MissingStaff = 0;
        Summary = Shifts.Count == 0 && SelectedWorkstation is not null
            ? "Für den gewählten Arbeitsplatz ist an diesem Datum keine Schicht freigegeben."
            : "Auswahl bereit. Simulation starten, um die aktuelle Besetzung und sichere Alternativen zu prüfen.";
        StatusMessage = string.Empty;
    }
}
