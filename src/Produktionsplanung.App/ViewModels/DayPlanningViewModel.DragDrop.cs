using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class DayPlanningViewModel
{
    public bool AssignEmployeeToProduction(int employeeId, ProductionOrderCoverageRow? coverage)
    {
        if (!SessionService.IsPlannerOrAdmin)
        {
            StatusMessage = "Diese Funktion ist nur für Planer und Administratoren verfügbar.";
            return false;
        }

        if (coverage is null)
        {
            StatusMessage = "Bitte den Mitarbeiter direkt auf eine Produktionszeile ziehen.";
            return false;
        }

        if (!coverage.ShiftId.HasValue)
        {
            StatusMessage = $"{coverage.OrderNumber}: Für die direkte Zuweisung muss eine Schicht hinterlegt sein.";
            return false;
        }

        using var db = new AppDbContext();
        var employee = db.Employees.AsNoTracking().FirstOrDefault(x => x.Id == employeeId && x.IsActive);
        var shift = db.Shifts.AsNoTracking().FirstOrDefault(x => x.Id == coverage.ShiftId.Value);
        if (employee is null || shift is null)
        {
            StatusMessage = "Mitarbeiter oder Schicht wurde nicht mehr gefunden.";
            return false;
        }

        if (!ProductionScheduleService.IsShiftAllowedOnDate(
                db,
                coverage.WorkstationId,
                coverage.ShiftId.Value,
                coverage.Date))
        {
            StatusMessage = $"{coverage.WorkstationName} / {coverage.ShiftName} ist am {coverage.Date:dd.MM.yyyy} nicht freigegeben.";
            return false;
        }

        var start = coverage.Date.Date + shift.StartTime;
        var end = coverage.Date.Date + shift.EndTime;
        if (end <= start)
            end = end.AddDays(1);

        if (db.Absences.AsNoTracking().Any(x =>
                x.EmployeeId == employeeId &&
                x.StartDate.Date <= end.Date &&
                x.EndDate.Date >= start.Date))
        {
            StatusMessage = $"{employee.FirstName} {employee.LastName} ist in diesem Zeitraum abwesend.";
            return false;
        }

        var existingAssignments = db.PlanningAssignments.AsNoTracking()
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.Date.Date >= coverage.Date.AddDays(-1).Date &&
                x.Date.Date <= coverage.Date.AddDays(1).Date)
            .ToList();

        var alreadyHere = existingAssignments.Any(x =>
            x.Date.Date == coverage.Date.Date &&
            x.WorkstationId == coverage.WorkstationId &&
            x.ShiftId == coverage.ShiftId);
        if (alreadyHere)
        {
            StatusMessage = $"{employee.FirstName} {employee.LastName} ist bei {coverage.WorkstationName} / {coverage.ShiftName} bereits eingeplant.";
            return false;
        }

        var overlapping = existingAssignments.Any(x =>
        {
            var existingStart = x.Date.Date + x.StartTime;
            var existingEnd = x.Date.Date + x.EndTime;
            if (existingEnd <= existingStart)
                existingEnd = existingEnd.AddDays(1);
            return start < existingEnd && existingStart < end;
        });
        if (overlapping)
        {
            StatusMessage = $"Doppelbelegung: {employee.FirstName} {employee.LastName} ist in dieser Zeit bereits anders eingeplant.";
            return false;
        }

        var skill = QualificationPlanningService.CheckEmployee(db, employeeId, coverage.WorkstationId);
        if (!skill.IsQualified)
        {
            StatusMessage = $"Zuweisung blockiert: {employee.FirstName} {employee.LastName} erfüllt die Pflichtqualifikation für {coverage.WorkstationName} nicht. {skill.Message}";
            return false;
        }

        db.PlanningAssignments.Add(new PlanningAssignment
        {
            EmployeeId = employeeId,
            WorkstationId = coverage.WorkstationId,
            ShiftId = coverage.ShiftId,
            Date = coverage.Date.Date,
            StartTime = shift.StartTime,
            EndTime = shift.EndTime,
            BreakMinutes = shift.BreakMinutes,
            Comment = $"Drag & Drop · {coverage.OrderNumber} · Schicht {coverage.SequenceNumber}"
        });
        db.SaveChanges();

        var runSlotId = coverage.RunSlotId;
        LoadDay();
        RefreshProductionOrderCoverage();
        SelectedProductionOrderCoverage = ProductionOrderCoverage.FirstOrDefault(x => x.RunSlotId == runSlotId);
        RefreshSkillAlerts();
        RefreshOperatingCalendarAlert();
        RefreshEmployeeSuggestions();

        StatusMessage = $"{BuildEmployeeInitials(employee.FirstName, employee.LastName)} · {employee.FirstName} {employee.LastName} → {coverage.OrderNumber} / {coverage.ShiftName} eingeplant.";
        return true;
    }

    private static string BuildEmployeeInitials(string firstName, string lastName)
    {
        var first = string.IsNullOrWhiteSpace(firstName) ? string.Empty : firstName.Trim()[0].ToString();
        var last = string.IsNullOrWhiteSpace(lastName) ? string.Empty : lastName.Trim()[0].ToString();
        return (first + last).ToUpperInvariant();
    }
}
