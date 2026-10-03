using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

internal static partial class Program
{
    private static void LargeSyntheticPlanningHistory()
    {
        // This fixture is created from constants, never from a customer database.
        SessionService.SignOut();
        var start = new DateTime(2028, 1, 1);
        int workstationId, shiftId;
        int[] employeeIds;
        TimeSpan shiftStart, shiftEnd;
        int shiftBreak;
        using (var db = new AppDbContext())
        {
            var workstation = new Workstation { Name = "SYNTHETIC-LINE", Area = "Test", MinimumStaff = 50, OptimalStaff = 52, MaximumStaff = 100 };
            db.Workstations.Add(workstation);
            var employees = Enumerable.Range(1, 500).Select(i => new Employee
            {
                PersonnelNumber = $"SYNTHETIC-{i:D5}", FirstName = "Testperson", LastName = $"Nummer {i:D5}", Role = "Test", WeeklyTargetHours = 40
            }).ToArray();
            db.Employees.AddRange(employees);
            db.SaveChanges();
            workstationId = workstation.Id;
            employeeIds = employees.Select(x => x.Id).ToArray();
            var shift = db.Shifts.AsNoTracking().OrderBy(x => x.Id).First();
            shiftId = shift.Id;
            shiftStart = shift.StartTime;
            shiftEnd = shift.EndTime;
            shiftBreak = shift.BreakMinutes;
            db.WorkstationShiftRules.Add(new WorkstationShiftRule { WorkstationId = workstationId, ShiftId = shiftId, Saturday = true, Sunday = true });
            db.ProductionOrders.AddRange(Enumerable.Range(1, 3000).Select(i => new ProductionOrder
            {
                OrderNumber = $"SYNTHETIC-PO-{i:D5}", ArticleNumber = $"TEST-ARTICLE-{i % 20:D2}",
                BatchNumber = $"SYNTHETIC-BATCH-{i:D5}", Product = "Synthetic product", Quantity = 100,
                WorkstationId = workstationId, ShiftId = shiftId, PlannedDate = start.AddDays(i % 1826)
            }));
            db.SaveChanges();
        }
        for (var offset = 0; offset < 1826; offset += 40)
        {
            using var db = new AppDbContext();
            var rows = new List<PlanningAssignment>();
            for (var day = offset; day < Math.Min(offset + 40, 1826); day++)
                for (var person = 0; person < 50; person++)
                    rows.Add(new PlanningAssignment
                    {
                        EmployeeId = employeeIds[person], WorkstationId = workstationId, ShiftId = shiftId,
                        Date = start.AddDays(day), StartTime = shiftStart, EndTime = shiftEnd, BreakMinutes = shiftBreak
                    });
            db.PlanningAssignments.AddRange(rows);
            db.SaveChanges();
        }
        var date = start.AddDays(1825);
        using (var db = new AppDbContext())
        {
            Check(db.PlanningAssignments.Count(x => x.WorkstationId == workstationId) == 91300, "Five-year synthetic planning history was incomplete");
            Check(db.ProductionOrders.Count(x => x.WorkstationId == workstationId) == 3000, "Synthetic orders or batches were lost");
            var week = db.PlanningAssignments.AsNoTracking().Where(x => x.WorkstationId == workstationId && x.Date >= date.AddDays(-6) && x.Date <= date).ToList();
            Check(week.Count == 350, "Week query mixed historical rows or omitted dates");
        }
        var result = WhatIfPlanningService.SimulateStaffingGap(date, workstationId, shiftId);
        Check(result.IsValid && result.PlannedStaff == 50 && result.MissingStaff == 2, "What-if miscounted staffing with a large history");
        Check(result.Alternatives.Count == 2 && result.Alternatives.All(x => !employeeIds.Take(50).Contains(x.EmployeeId)), "What-if proposed overlapping staff");
        using var verify = new AppDbContext();
        Check(verify.PlanningAssignments.Count(x => x.WorkstationId == workstationId) == 91300, "Large-history simulation wrote assignments");
    }
}
