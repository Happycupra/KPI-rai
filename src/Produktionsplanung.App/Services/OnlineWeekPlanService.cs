using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static class OnlineWeekPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static OnlineWeekPlanSnapshot BuildSnapshot(DateTime date)
    {
        var monday = GetMonday(date);
        var sunday = monday.AddDays(6);
        var settings = AppSettingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.CompanyId) || string.IsNullOrWhiteSpace(settings.CompanyCode))
            throw new InvalidOperationException("Diese Installation ist noch keiner Firma zugeordnet.");

        using var db = new AppDbContext();
        ProductionScheduleService.EnsureMissingRunSlots(db);

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date >= monday && x.Date.Date <= sunday)
            .AsEnumerable()
            .OrderBy(x => x.Date)
            .ThenBy(x => x.StartTime)
            .ThenBy(x => x.Employee.LastName)
            .Select(x => new OnlineWeekPlanEntry
            {
                Id = $"assignment-{x.Id}",
                AssignmentId = x.Id,
                Date = x.Date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EmployeeId = x.EmployeeId,
                EmployeeName = $"{x.Employee.LastName}, {x.Employee.FirstName}",
                EmployeeRole = x.Employee.Role,
                WorkstationId = x.WorkstationId,
                WorkstationName = x.Workstation.Name,
                WorkstationArea = x.Workstation.Area,
                ShiftId = x.ShiftId,
                ShiftName = x.Shift?.Name ?? "Individuell",
                Start = x.StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                End = x.EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                BreakMinutes = x.BreakMinutes,
                Note = string.Empty
            })
            .ToList();

        var slots = db.ProductionRunSlots.AsNoTracking()
            .Include(x => x.Shift)
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Workstation)
            .Where(x => x.Date.Date >= monday && x.Date.Date <= sunday)
            .AsEnumerable()
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Shift.StartTime)
            .Select(x => new OnlineWeekPlanProductionSlot
            {
                Id = $"run-{x.Id}",
                RunSlotId = x.Id,
                Date = x.Date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                OrderNumber = x.ProductionOrder.OrderNumber,
                Product = x.ProductionOrder.Product,
                WorkstationId = x.ProductionOrder.WorkstationId,
                WorkstationName = x.ProductionOrder.Workstation.Name,
                ShiftId = x.ShiftId,
                ShiftName = x.Shift.Name,
                Start = x.Shift.StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                End = x.Shift.EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                RequiredStaff = x.ProductionOrder.RequiredStaff,
                SequenceNumber = x.SequenceNumber,
                PlannedShiftCount = Math.Max(1, x.ProductionOrder.PlannedShiftCount)
            })
            .ToList();

        var isoYear = ISOWeek.GetYear(monday);
        var isoWeek = ISOWeek.GetWeekOfYear(monday);
        return new OnlineWeekPlanSnapshot
        {
            SchemaVersion = "1.1",
            WeekId = $"{isoYear}-W{isoWeek:00}",
            IsoYear = isoYear,
            IsoWeek = isoWeek,
            WeekStart = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WeekEnd = sunday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PreparedAtUtc = DateTime.UtcNow,
            PreparedBy = SessionService.CurrentUser?.Username ?? "system",
            CompanyId = settings.CompanyId,
            CompanyCode = settings.CompanyCode,
            CompanyName = settings.CompanyName,
            SiteName = settings.SiteName,
            Entries = assignments,
            ProductionSlots = slots
        };
    }

    public static OnlineWeekPlanPackageResult PreparePackage(DateTime date)
    {
        if (!SessionService.IsAdministrator)
            throw new InvalidOperationException("Nur Administratoren dürfen einen Online-Wochenplan vorbereiten.");

        var snapshot = BuildSnapshot(date);
        var root = Path.Combine(AppPaths.ExportsDirectory, "OnlineWeekPlan");
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, $"weekplan-{snapshot.WeekId}.json");
        File.WriteAllText(filePath, JsonSerializer.Serialize(snapshot, JsonOptions));

        AppSettingsService.Update(settings =>
        {
            settings.LastOnlineWeekPreparedAtUtc = snapshot.PreparedAtUtc;
            settings.LastOnlineWeekPreparedId = snapshot.WeekId;
        });

        return new OnlineWeekPlanPackageResult(
            snapshot.WeekId,
            filePath,
            snapshot.Entries.Count,
            snapshot.ProductionSlots.Count,
            snapshot.PreparedAtUtc);
    }

    public static bool IsFirebaseConfigured(AppSettings settings) =>
        settings.OnlineWeekPlanEnabled &&
        !string.IsNullOrWhiteSpace(settings.FirebaseProjectId) &&
        !string.IsNullOrWhiteSpace(settings.FirebaseWebApiKey) &&
        Uri.TryCreate(settings.FirebaseAuthEndpoint, UriKind.Absolute, out _) &&
        Uri.TryCreate(settings.FirebaseHostingUrl, UriKind.Absolute, out _) &&
        Uri.TryCreate(settings.FirebasePublishEndpoint, UriKind.Absolute, out _);

    public static string FirebaseStatusText(AppSettings settings)
    {
        if (!settings.OnlineWeekPlanEnabled)
            return "Firebase vorbereitet · noch nicht aktiviert";
        return IsFirebaseConfigured(settings)
            ? "Firebase-Konfiguration vollständig · Veröffentlichung kann angebunden werden"
            : "Firebase aktiviert · Konfigurationsdaten noch unvollständig";
    }

    private static DateTime GetMonday(DateTime date)
    {
        var days = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-days);
    }
}

public sealed class OnlineWeekPlanSnapshot
{
    public string SchemaVersion { get; init; } = "1.1";
    public string CompanyId { get; init; } = string.Empty;
    public string CompanyCode { get; init; } = string.Empty;
    public string WeekId { get; init; } = string.Empty;
    public int IsoYear { get; init; }
    public int IsoWeek { get; init; }
    public string WeekStart { get; init; } = string.Empty;
    public string WeekEnd { get; init; } = string.Empty;
    public DateTime PreparedAtUtc { get; init; }
    public string PreparedBy { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string SiteName { get; init; } = string.Empty;
    public IReadOnlyList<OnlineWeekPlanEntry> Entries { get; init; } = Array.Empty<OnlineWeekPlanEntry>();
    public IReadOnlyList<OnlineWeekPlanProductionSlot> ProductionSlots { get; init; } = Array.Empty<OnlineWeekPlanProductionSlot>();
}

public sealed class OnlineWeekPlanEntry
{
    public string Id { get; init; } = string.Empty;
    public int AssignmentId { get; init; }
    public string Date { get; init; } = string.Empty;
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeRole { get; init; } = string.Empty;
    public int WorkstationId { get; init; }
    public string WorkstationName { get; init; } = string.Empty;
    public string WorkstationArea { get; init; } = string.Empty;
    public int? ShiftId { get; init; }
    public string ShiftName { get; init; } = string.Empty;
    public string Start { get; init; } = string.Empty;
    public string End { get; init; } = string.Empty;
    public int BreakMinutes { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed class OnlineWeekPlanProductionSlot
{
    public string Id { get; init; } = string.Empty;
    public int RunSlotId { get; init; }
    public string Date { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string Product { get; init; } = string.Empty;
    public int WorkstationId { get; init; }
    public string WorkstationName { get; init; } = string.Empty;
    public int ShiftId { get; init; }
    public string ShiftName { get; init; } = string.Empty;
    public string Start { get; init; } = string.Empty;
    public string End { get; init; } = string.Empty;
    public int RequiredStaff { get; init; }
    public int SequenceNumber { get; init; }
    public int PlannedShiftCount { get; init; }
}

public sealed record OnlineWeekPlanPackageResult(
    string WeekId,
    string FilePath,
    int AssignmentCount,
    int ProductionSlotCount,
    DateTime PreparedAtUtc);
