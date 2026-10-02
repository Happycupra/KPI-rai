using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

internal static partial class Program
{
    private static void WhatIfSimulationApply()
    {
        Planner();
        var date = new DateTime(2032, 4, 5); // Monday
        int employeeId;
        int workstationId;
        int shiftId;

        using (var db = new AppDbContext())
        {
            var shift = db.Shifts.OrderBy(x => x.StartTime).First();
            var employee = new Employee
            {
                PersonnelNumber = "WHATIF-001",
                FirstName = "Simone",
                LastName = "Simulation",
                Role = "Produktion",
                WeeklyTargetHours = 40,
                IsActive = true
            };
            var workstation = new Workstation
            {
                Name = "What-if-Prüfplatz",
                Area = "Regressionstest",
                MinimumStaff = 1,
                OptimalStaff = 1,
                MaximumStaff = 2,
                IsActive = true
            };
            db.Employees.Add(employee);
            db.Workstations.Add(workstation);
            db.SaveChanges();
            db.WorkstationShiftRules.Add(new WorkstationShiftRule
            {
                WorkstationId = workstation.Id,
                ShiftId = shift.Id,
                Monday = true,
                Tuesday = true,
                Wednesday = true,
                Thursday = true,
                Friday = true,
                Saturday = true,
                Sunday = true
            });
            db.SaveChanges();
            employeeId = employee.Id;
            workstationId = workstation.Id;
            shiftId = shift.Id;
        }

        var before = WhatIfPlanningService.SimulateStaffingGap(date, workstationId, shiftId);
        Check(before.IsValid && before.PlannedStaff == 0 && before.TargetStaff == 1 && before.MissingStaff == 1,
            "What-if result did not expose the controlled staffing gap");
        using (var db = new AppDbContext())
            Check(!db.PlanningAssignments.Any(x => x.Date == date && x.WorkstationId == workstationId),
                "Read-only simulation wrote a planning assignment");

        using (var db = new AppDbContext())
        {
            db.Absences.Add(new Absence
            {
                EmployeeId = employeeId,
                Type = "Regressionstest",
                StartDate = date,
                EndDate = date
            });
            db.SaveChanges();
        }
        var rejected = WhatIfPlanningService.ApplyAlternative(date, workstationId, shiftId, employeeId);
        Check(!rejected.Success && rejected.Message.Contains("abwesend", StringComparison.OrdinalIgnoreCase),
            "What-if apply did not revalidate a newly added absence");

        using (var db = new AppDbContext())
        {
            db.Absences.RemoveRange(db.Absences.Where(x => x.EmployeeId == employeeId && x.StartDate == date));
            db.SaveChanges();
        }
        var applied = WhatIfPlanningService.ApplyAlternative(date, workstationId, shiftId, employeeId);
        Check(applied.Success && applied.AssignmentId.HasValue, "Valid What-if alternative was not applied");
        var assignmentId = applied.AssignmentId.GetValueOrDefault();
        var duplicate = WhatIfPlanningService.ApplyAlternative(date, workstationId, shiftId, employeeId);
        Check(!duplicate.Success, "What-if apply accepted the same employee twice");

        using var check = new AppDbContext();
        Check(check.PlanningAssignments.Count(x => x.Date == date && x.WorkstationId == workstationId) == 1,
            "What-if apply did not create exactly one assignment");
        Check(check.AuditLogs.Any(x => x.Action == "What-if-Vorschlag übernommen" && x.EntityId == assignmentId.ToString()),
            "What-if apply did not create its semantic audit entry");
    }

    private static void ShiftHandoverWorkflow()
    {
        Planner();
        int fromShiftId;
        int toShiftId;
        int workstationId;
        using (var db = new AppDbContext())
        {
            var shifts = db.Shifts.OrderBy(x => x.StartTime).Take(2).ToList();
            Check(shifts.Count == 2, "Shift handover test requires two shifts");
            fromShiftId = shifts[0].Id;
            toShiftId = shifts[1].Id;
            workstationId = db.Workstations.Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).First();
        }

        var created = ShiftHandoverService.Create(
            DateTime.Today,
            fromShiftId,
            toShiftId,
            workstationId,
            null,
            "Kritisch",
            "Temperatur prüfen",
            "Maschine vor dem Produktionsstart kontrollieren.");
        Check(created.Id > 0, "Shift handover was not persisted");
        Check(ShiftHandoverService.Get(DateTime.Today, includeResolved: false, criticalOnly: true).Any(x => x.Id == created.Id),
            "Critical open handover was not returned by the filter");

        ShiftHandoverService.Acknowledge(created.Id);
        using (var db = new AppDbContext())
        {
            var acknowledged = db.ShiftHandovers.AsNoTracking().Single(x => x.Id == created.Id);
            Check(acknowledged.Status == "Bestätigt" && acknowledged.AcknowledgedAtUtc.HasValue,
                "Shift handover acknowledgement was not persisted");
        }

        ShiftHandoverService.Resolve(created.Id, "Temperatur stabil; Freigabe erteilt.");
        using var check = new AppDbContext();
        var resolved = check.ShiftHandovers.AsNoTracking().Single(x => x.Id == created.Id);
        Check(resolved.Status == "Erledigt" && resolved.ResolvedAtUtc.HasValue && !string.IsNullOrWhiteSpace(resolved.Resolution),
            "Shift handover resolution was not persisted");
        Check(!ShiftHandoverService.GetOpen().Any(x => x.Id == created.Id),
            "Resolved handover remains in the open list");
        var actions = check.AuditLogs.Where(x => x.EntityType == nameof(ShiftHandover) && x.EntityId == created.Id.ToString())
            .Select(x => x.Action).ToList();
        Check(actions.Contains("Schichtübergabe erstellt") && actions.Contains("Schichtübergabe bestätigt") && actions.Contains("Schichtübergabe abgeschlossen"),
            "Shift handover workflow is missing semantic audit entries");
    }
}
