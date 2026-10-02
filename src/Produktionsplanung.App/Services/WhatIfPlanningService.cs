using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

/// <summary>
/// Read-only scenario engine. It never writes planning data.
/// Every alternative is explainable and must be explicitly accepted by the user elsewhere.
/// </summary>
public static class WhatIfPlanningService
{
    public static WhatIfResult SimulateStaffingGap(DateTime date, int workstationId, int shiftId)
    {
        using var db = new AppDbContext();
        var workstation = db.Workstations.AsNoTracking().FirstOrDefault(x => x.Id == workstationId);
        var shift = db.Shifts.AsNoTracking().FirstOrDefault(x => x.Id == shiftId);
        if (workstation is null || shift is null)
            return WhatIfResult.Invalid("Arbeitsplatz oder Schicht existiert nicht mehr.");

        if (!ProductionScheduleService.IsShiftAllowedOnDate(db, workstationId, shiftId, date.Date))
            return WhatIfResult.Invalid("Die Schicht ist für diesen Arbeitsplatz an diesem Datum nicht freigegeben.");

        var planned = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date == date.Date && x.WorkstationId == workstationId && x.ShiftId == shiftId)
            .Select(x => x.EmployeeId).Distinct().Count();

        var target = Math.Max(workstation.MinimumStaff, workstation.OptimalStaff);
        var missing = Math.Max(0, target - planned);
        if (missing == 0)
            return new WhatIfResult(true, planned, target, 0, "Besetzung erfüllt das Ziel.", Array.Empty<WhatIfAlternative>());

        var suggestions = QualificationPlanningService.Suggest(date.Date, workstationId, shiftId)
            .Take(missing)
            .Select((x, index) => new WhatIfAlternative(
                index + 1,
                x.EmployeeId,
                x.EmployeeName,
                $"Qualifikation: {x.SkillText}; Auslastung: {x.LoadText}",
                $"Besetzung {planned + index + 1}/{target} nach Übernahme dieses Vorschlags."))
            .ToList();

        var message = suggestions.Count >= missing
            ? $"{missing} Besetzung(en) fehlen. Es stehen genügend konfliktfreie, qualifizierte Vorschläge bereit."
            : $"{missing} Besetzung(en) fehlen. Nur {suggestions.Count} konfliktfreie, qualifizierte Vorschläge gefunden.";

        return new WhatIfResult(true, planned, target, missing, message, suggestions);
    }

    /// <summary>
    /// Applies one suggestion after repeating every volatile validation against the current database state.
    /// A simulation is therefore never treated as an authorization to write stale planning data.
    /// </summary>
    public static WhatIfApplyResult ApplyAlternative(DateTime date, int workstationId, int shiftId, int employeeId)
    {
        if (!SessionService.IsPlannerOrAdmin)
            return WhatIfApplyResult.Rejected("Für die Übernahme ist die Rolle Planer oder Administrator erforderlich.");

        using var db = new AppDbContext();
        var employee = db.Employees.AsNoTracking().FirstOrDefault(x => x.Id == employeeId && x.IsActive);
        var workstation = db.Workstations.AsNoTracking().FirstOrDefault(x => x.Id == workstationId && x.IsActive);
        var shift = db.Shifts.AsNoTracking().FirstOrDefault(x => x.Id == shiftId);
        if (employee is null || workstation is null || shift is null)
            return WhatIfApplyResult.Rejected("Mitarbeiter, Arbeitsplatz oder Schicht existiert nicht mehr bzw. ist nicht aktiv.");

        var normalizedDate = date.Date;
        var start = normalizedDate + shift.StartTime;
        var end = normalizedDate + shift.EndTime;
        if (end <= start) end = end.AddDays(1);

        // Keep this order aligned with the confirmation workflow shown to the user.
        if (db.Absences.AsNoTracking().Any(x =>
                x.EmployeeId == employeeId &&
                x.StartDate.Date <= end.Date &&
                x.EndDate.Date >= start.Date))
        {
            return WhatIfApplyResult.Rejected($"{employee.FirstName} {employee.LastName} ist im gewählten Zeitraum abwesend.");
        }

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.Date.Date >= normalizedDate.AddDays(-1) &&
                x.Date.Date <= normalizedDate.AddDays(1))
            .ToList();
        var sameAssignment = assignments.Any(x =>
            x.Date.Date == normalizedDate &&
            x.WorkstationId == workstationId &&
            x.ShiftId == shiftId);
        var overlapsAnotherAssignment = assignments
            .Where(x => !(x.Date.Date == normalizedDate && x.WorkstationId == workstationId && x.ShiftId == shiftId))
            .Any(x =>
            {
                var existingStart = x.Date.Date + x.StartTime;
                var existingEnd = x.Date.Date + x.EndTime;
                if (existingEnd <= existingStart) existingEnd = existingEnd.AddDays(1);
                return start < existingEnd && existingStart < end;
            });
        if (overlapsAnotherAssignment)
            return WhatIfApplyResult.Rejected($"Doppelbelegung: {employee.FirstName} {employee.LastName} ist in dieser Zeit bereits anders eingeplant.");

        var skill = QualificationPlanningService.CheckEmployee(db, employeeId, workstationId);
        if (!skill.IsQualified)
            return WhatIfApplyResult.Rejected($"Zuweisung blockiert: {skill.Message}");

        if (!ProductionScheduleService.IsShiftAllowedOnDate(db, workstationId, shiftId, normalizedDate))
            return WhatIfApplyResult.Rejected($"{workstation.Name} / {shift.Name} ist am {normalizedDate:dd.MM.yyyy} nicht freigegeben.");

        if (sameAssignment)
            return WhatIfApplyResult.Rejected($"{employee.FirstName} {employee.LastName} ist für diese Schicht bereits eingeplant.");

        var target = Math.Max(workstation.MinimumStaff, workstation.OptimalStaff);
        var planned = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date == normalizedDate && x.WorkstationId == workstationId && x.ShiftId == shiftId)
            .Select(x => x.EmployeeId)
            .Distinct()
            .Count();
        if (planned >= target)
            return WhatIfApplyResult.Rejected("Das Besetzungsziel ist inzwischen bereits erfüllt. Bitte die Simulation aktualisieren.");

        var assignment = new PlanningAssignment
        {
            EmployeeId = employeeId,
            WorkstationId = workstationId,
            ShiftId = shiftId,
            Date = normalizedDate,
            StartTime = shift.StartTime,
            EndTime = shift.EndTime,
            BreakMinutes = shift.BreakMinutes,
            Comment = "Übernahme aus What-if-Simulation"
        };
        db.PlanningAssignments.Add(assignment);
        db.SaveChanges();

        AuditService.Log(
            "What-if-Vorschlag übernommen",
            nameof(PlanningAssignment),
            assignment.Id.ToString(),
            $"{employee.FirstName} {employee.LastName} · {workstation.Name} · {shift.Name} · {normalizedDate:dd.MM.yyyy}");

        return new WhatIfApplyResult(
            true,
            $"{employee.FirstName} {employee.LastName} wurde für {workstation.Name} / {shift.Name} eingeplant.",
            assignment.Id);
    }
}

public sealed record WhatIfResult(
    bool IsValid,
    int PlannedStaff,
    int TargetStaff,
    int MissingStaff,
    string Summary,
    IReadOnlyList<WhatIfAlternative> Alternatives)
{
    public static WhatIfResult Invalid(string message) => new(false, 0, 0, 0, message, Array.Empty<WhatIfAlternative>());
}

public sealed record WhatIfAlternative(
    int Rank,
    int EmployeeId,
    string EmployeeName,
    string Reason,
    string Impact);

public sealed record WhatIfApplyResult(bool Success, string Message, int? AssignmentId)
{
    public static WhatIfApplyResult Rejected(string message) => new(false, message, null);
}
