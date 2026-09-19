using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class ProductionStaffingService
{
    public static ProductionStaffingResult AssignEmployeeToRunSlot(int employeeId, int runSlotId)
    {
        if (!SessionService.IsPlannerOrAdmin)
            return new(false, "Diese Funktion ist nur für Planer und Administratoren verfügbar.", null);

        using var db = new AppDbContext();
        var slot = db.ProductionRunSlots
            .Include(x => x.Shift)
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Workstation)
            .FirstOrDefault(x => x.Id == runSlotId);

        var employee = db.Employees.AsNoTracking()
            .FirstOrDefault(x => x.Id == employeeId && x.IsActive);

        if (slot is null || employee is null)
            return new(false, "Mitarbeiter oder Produktionsschicht wurde nicht mehr gefunden.", null);

        var order = slot.ProductionOrder;
        var date = slot.Date.Date;

        if (!ProductionScheduleService.IsShiftAllowedOnDate(
                db, order.WorkstationId, slot.ShiftId, date))
        {
            return new(false,
                $"{order.Workstation.Name} / {slot.Shift.Name} ist am {date:dd.MM.yyyy} nicht freigegeben.",
                null);
        }

        var start = date + slot.Shift.StartTime;
        var end = date + slot.Shift.EndTime;
        if (end <= start)
            end = end.AddDays(1);

        if (db.Absences.AsNoTracking().Any(x =>
                x.EmployeeId == employeeId &&
                x.StartDate.Date <= end.Date &&
                x.EndDate.Date >= start.Date))
        {
            return new(false,
                $"{employee.FirstName} {employee.LastName} ist in diesem Zeitraum abwesend.",
                null);
        }

        var existingAssignments = db.PlanningAssignments.AsNoTracking()
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.Date.Date >= date.AddDays(-1) &&
                x.Date.Date <= date.AddDays(1))
            .ToList();

        var alreadyHere = existingAssignments.Any(x =>
            x.Date.Date == date &&
            x.WorkstationId == order.WorkstationId &&
            x.ShiftId == slot.ShiftId);

        if (alreadyHere)
        {
            return new(false,
                $"{employee.FirstName} {employee.LastName} ist bei {order.Workstation.Name} / {slot.Shift.Name} bereits eingeplant.",
                null);
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
            return new(false,
                $"Doppelbelegung: {employee.FirstName} {employee.LastName} ist in dieser Zeit bereits anders eingeplant.",
                null);
        }

        var skill = QualificationPlanningService.CheckEmployee(db, employeeId, order.WorkstationId);
        if (!skill.IsQualified)
        {
            return new(false,
                $"Zuweisung blockiert: {employee.FirstName} {employee.LastName} erfüllt die Pflichtqualifikation für {order.Workstation.Name} nicht. {skill.Message}",
                null);
        }

        db.PlanningAssignments.Add(new PlanningAssignment
        {
            EmployeeId = employeeId,
            WorkstationId = order.WorkstationId,
            ShiftId = slot.ShiftId,
            Date = date,
            StartTime = slot.Shift.StartTime,
            EndTime = slot.Shift.EndTime,
            BreakMinutes = slot.Shift.BreakMinutes,
            Comment = $"Drag & Drop · {order.OrderNumber} · Schicht {slot.SequenceNumber}"
        });
        db.SaveChanges();

        var initials = EmployeeInitialsService.Build3(employee.FirstName, employee.LastName);
        return new(true,
            $"{initials} · {employee.FirstName} {employee.LastName} → {order.OrderNumber} / {slot.Shift.Name} eingeplant.",
            initials);
    }
}

public sealed record ProductionStaffingResult(bool Success, string Message, string? EmployeeInitials);

public static class EmployeeInitialsService
{
    public static string Build3(string? firstName, string? lastName)
    {
        var first = (firstName ?? string.Empty).Trim().ToUpperInvariant();
        var last = (lastName ?? string.Empty).Trim().ToUpperInvariant();

        if (first.Length >= 1 && last.Length >= 2)
            return $"{first[0]}{last[0]}{last[1]}";

        if (first.Length >= 2 && last.Length >= 1)
            return $"{first[0]}{first[1]}{last[0]}";

        var combined = new string((first + last).Where(char.IsLetterOrDigit).ToArray());
        return combined.Length <= 3 ? combined : combined[..3];
    }
}
