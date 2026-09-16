using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static class QualificationPlanningService
{
    public static SkillCheckResult CheckEmployee(AppDbContext db, int employeeId, int workstationId)
    {
        var workstation = db.Workstations.AsNoTracking()
            .Include(x => x.RequiredQualification)
            .First(x => x.Id == workstationId);

        if (!workstation.RequiredQualificationId.HasValue || workstation.RequiredQualificationLevel <= 0)
            return new SkillCheckResult(true, false, null, 0, 0, "Keine Pflichtqualifikation hinterlegt.");

        var employeeLevel = db.EmployeeQualifications.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.QualificationId == workstation.RequiredQualificationId.Value)
            .Select(x => (int?)x.Level)
            .FirstOrDefault() ?? 0;

        var requiredLevel = Math.Clamp(workstation.RequiredQualificationLevel, 1, 3);
        var qualified = employeeLevel >= requiredLevel;
        var qualificationName = workstation.RequiredQualification?.Name ?? "Qualifikation";
        var message = qualified
            ? $"{qualificationName}: Level {employeeLevel} erfüllt Mindestlevel {requiredLevel}."
            : $"{qualificationName}: benötigt Level {requiredLevel}, Mitarbeiter hat Level {employeeLevel}.";

        return new SkillCheckResult(qualified, true, qualificationName, requiredLevel, employeeLevel, message);
    }

    public static IReadOnlyList<EmployeeSuggestion> Suggest(
        DateTime date,
        int workstationId,
        int shiftId,
        int? editingAssignmentId = null)
    {
        using var db = new AppDbContext();
        var shift = db.Shifts.AsNoTracking().First(x => x.Id == shiftId);
        var start = date.Date + shift.StartTime;
        var end = date.Date + shift.EndTime;
        if (end <= start) end = end.AddDays(1);

        var monday = GetMonday(date);
        var sunday = monday.AddDays(7);
        var employees = db.Employees.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToList();

        var absences = db.Absences.AsNoTracking()
            .Where(x => x.StartDate.Date <= end.Date && x.EndDate.Date >= start.Date)
            .ToList();

        var overlapCandidates = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date >= date.AddDays(-1).Date && x.Date.Date <= date.AddDays(1).Date)
            .ToList();

        var weekAssignments = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date.Date >= monday && x.Date.Date < sunday)
            .ToList();

        var result = new List<EmployeeSuggestion>();
        foreach (var employee in employees)
        {
            if (absences.Any(x => x.EmployeeId == employee.Id))
                continue;

            var overlapping = overlapCandidates
                .Where(x => x.EmployeeId == employee.Id && (!editingAssignmentId.HasValue || x.Id != editingAssignmentId.Value))
                .Any(x =>
                {
                    var existingStart = x.Date.Date + x.StartTime;
                    var existingEnd = x.Date.Date + x.EndTime;
                    if (existingEnd <= existingStart) existingEnd = existingEnd.AddDays(1);
                    return start < existingEnd && existingStart < end;
                });
            if (overlapping)
                continue;

            var skill = CheckEmployee(db, employee.Id, workstationId);
            if (!skill.IsQualified)
                continue;

            var plannedHours = weekAssignments
                .Where(x => x.EmployeeId == employee.Id)
                .Sum(CalculateNetHours);

            result.Add(new EmployeeSuggestion
            {
                EmployeeId = employee.Id,
                EmployeeName = $"{employee.LastName}, {employee.FirstName}",
                Role = employee.Role,
                SkillLevel = skill.EmployeeLevel,
                SkillText = skill.RequirementApplies
                    ? $"{skill.QualificationName} L{skill.EmployeeLevel}"
                    : "keine Skill-Pflicht",
                PlannedWeekHours = plannedHours,
                TargetWeekHours = employee.WeeklyTargetHours,
                LoadText = $"{plannedHours:0.#}/{employee.WeeklyTargetHours:0.#} h diese Woche"
            });
        }

        return result
            .OrderByDescending(x => x.SkillLevel)
            .ThenBy(x => x.TargetWeekHours <= 0 ? double.MaxValue : x.PlannedWeekHours / x.TargetWeekHours)
            .ThenBy(x => x.EmployeeName)
            .Take(12)
            .ToList();
    }

    private static double CalculateNetHours(Models.PlanningAssignment assignment)
    {
        var duration = assignment.EndTime - assignment.StartTime;
        if (duration <= TimeSpan.Zero) duration += TimeSpan.FromDays(1);
        return Math.Max(0, duration.TotalHours - assignment.BreakMinutes / 60d);
    }

    private static DateTime GetMonday(DateTime date)
    {
        var day = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-day);
    }
}

public sealed record SkillCheckResult(
    bool IsQualified,
    bool RequirementApplies,
    string? QualificationName,
    int RequiredLevel,
    int EmployeeLevel,
    string Message);

public sealed class EmployeeSuggestion
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int SkillLevel { get; set; }
    public string SkillText { get; set; } = string.Empty;
    public double PlannedWeekHours { get; set; }
    public double TargetWeekHours { get; set; }
    public string LoadText { get; set; } = string.Empty;
}
