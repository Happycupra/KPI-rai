using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

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
