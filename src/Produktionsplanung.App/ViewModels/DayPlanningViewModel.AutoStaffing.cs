using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class DayPlanningViewModel
{
    [ObservableProperty] private ProductionOrderCoverageRow? selectedProductionOrderCoverage;

    [RelayCommand]
    private void AutoStaffSelectedOrder()
    {
        var coverage = SelectedProductionOrderCoverage;
        if (coverage is null)
        {
            StatusMessage = "Bitte zuerst einen Produktionsauftrag in der Auftragsabdeckung auswählen.";
            return;
        }

        if (!coverage.ShiftId.HasValue)
        {
            StatusMessage = $"{coverage.OrderNumber}: Für automatische Besetzung muss eine Schicht hinterlegt sein.";
            return;
        }

        var missing = Math.Max(0, coverage.RequiredStaff - coverage.PlannedStaff);
        if (missing == 0)
        {
            StatusMessage = $"{coverage.OrderNumber} ist in dieser Schicht bereits personell gedeckt.";
            return;
        }

        using var db = new AppDbContext();
        var order = db.ProductionOrders.AsNoTracking().FirstOrDefault(x => x.Id == coverage.OrderId);
        var shift = db.Shifts.AsNoTracking().FirstOrDefault(x => x.Id == coverage.ShiftId.Value);
        if (order is null || shift is null)
        {
            StatusMessage = "Der Auftrag oder die Schicht wurde nicht mehr gefunden.";
            return;
        }

        var suggestions = QualificationPlanningService.Suggest(
            coverage.Date,
            order.WorkstationId,
            coverage.ShiftId.Value)
            .Take(missing)
            .ToList();

        if (suggestions.Count == 0)
        {
            StatusMessage = $"{coverage.OrderNumber}: Keine verfügbaren und ausreichend qualifizierten Mitarbeiter gefunden.";
            return;
        }

        foreach (var suggestion in suggestions)
        {
            db.PlanningAssignments.Add(new PlanningAssignment
            {
                EmployeeId = suggestion.EmployeeId,
                WorkstationId = order.WorkstationId,
                ShiftId = coverage.ShiftId,
                Date = coverage.Date.Date,
                StartTime = shift.StartTime,
                EndTime = shift.EndTime,
                BreakMinutes = shift.BreakMinutes,
                Comment = $"Auto-Besetzung · {order.OrderNumber} · Schicht {coverage.SequenceNumber}"
            });
        }

        db.SaveChanges();
        LoadDay();
        RefreshProductionOrderCoverage();
        RefreshSkillAlerts();
        RefreshOperatingCalendarAlert();
        RefreshEmployeeSuggestions();

        var remaining = Math.Max(0, missing - suggestions.Count);
        StatusMessage = remaining == 0
            ? $"{coverage.OrderNumber} · {coverage.ShiftName}: {suggestions.Count} Mitarbeiter automatisch passend eingeplant."
            : $"{coverage.OrderNumber} · {coverage.ShiftName}: {suggestions.Count} passend eingeplant; für {remaining} Position(en) wurde kein geeigneter Mitarbeiter gefunden.";
    }
}
