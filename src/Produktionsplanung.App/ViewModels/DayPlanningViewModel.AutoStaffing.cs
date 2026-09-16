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
            StatusMessage = $"{coverage.OrderNumber} ist bereits personell gedeckt.";
            return;
        }

        using var db = new AppDbContext();
        var order = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Shift)
            .FirstOrDefault(x => x.Id == coverage.OrderId);
        if (order?.Shift is null)
        {
            StatusMessage = "Der Auftrag oder seine Schicht wurde nicht mehr gefunden.";
            return;
        }

        var suggestions = QualificationPlanningService.Suggest(
            order.PlannedDate,
            order.WorkstationId,
            order.ShiftId!.Value)
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
                ShiftId = order.ShiftId,
                Date = order.PlannedDate.Date,
                StartTime = order.Shift.StartTime,
                EndTime = order.Shift.EndTime,
                BreakMinutes = order.Shift.BreakMinutes,
                Comment = $"Auto-Besetzung · {order.OrderNumber}"
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
            ? $"{coverage.OrderNumber}: {suggestions.Count} Mitarbeiter automatisch passend eingeplant."
            : $"{coverage.OrderNumber}: {suggestions.Count} passend eingeplant; für {remaining} Position(en) wurde kein geeigneter Mitarbeiter gefunden.";
    }
}
