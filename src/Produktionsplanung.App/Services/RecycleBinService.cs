using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed class RecycleBinRow
{
    public long Id { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public DateTime DeletedAtUtc { get; init; }
    public string DeletedBy { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateTime? RestoredAtUtc { get; init; }
    public string? RestoredBy { get; init; }
    public bool CanRestore => !RestoredAtUtc.HasValue && EntityType is nameof(ProductionOrder) or nameof(Employee) or nameof(ProductionActual) or nameof(DowntimeEntry);
    public string TypeText => EntityType switch
    {
        nameof(ProductionOrder) => "Charge / Auftrag",
        nameof(Employee) => "Mitarbeiter",
        nameof(ProductionActual) => "Ist-Produktion",
        nameof(DowntimeEntry) => "Stillstand",
        _ => EntityType
    };
    public string DeletedAtText => DeletedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string StatusText => RestoredAtUtc.HasValue
        ? $"Wiederhergestellt {RestoredAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm} · {RestoredBy}"
        : CanRestore ? "Im Papierkorb · wiederherstellbar" : "Archiviert · Audit-Historie erhalten";
}

public static class RecycleBinService
{
    public static IReadOnlyList<RecycleBinRow> Load()
    {
        using var db = new AppDbContext();
        return db.RecycleBinItems.AsNoTracking()
            .OrderByDescending(x => x.DeletedAtUtc)
            .Take(1000)
            .Select(x => new RecycleBinRow
            {
                Id = x.Id,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                DisplayName = x.DisplayName,
                DeletedAtUtc = x.DeletedAtUtc,
                DeletedBy = x.DeletedBy,
                Reason = x.Reason,
                RestoredAtUtc = x.RestoredAtUtc,
                RestoredBy = x.RestoredBy
            })
            .ToList();
    }

    public static void MoveProductionOrderToTrash(int id, string? reason = null)
    {
        RequirePlanner();
        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();
        var order = db.ProductionOrders.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Die Charge bzw. der Auftrag ist nicht mehr aktiv vorhanden.");

        var username = CurrentUsername();
        var now = DateTime.UtcNow;
        order.IsDeleted = true;
        order.DeletedAtUtc = now;
        order.DeletedBy = username;

        db.RecycleBinItems.Add(new RecycleBinItem
        {
            EntityType = nameof(ProductionOrder),
            EntityId = order.Id.ToString(),
            DisplayName = BuildOrderDisplayName(order),
            DeletedAtUtc = now,
            DeletedBy = username,
            Reason = CleanReason(reason),
            SnapshotJson = JsonSerializer.Serialize(new
            {
                order.Id,
                order.OrderNumber,
                order.ArticleMasterId,
                order.ArticleNumber,
                order.Product,
                order.BatchNumber,
                order.Quantity,
                order.Unit,
                order.Priority,
                order.PlannedDate,
                order.WorkstationId,
                order.ShiftId,
                order.PlannedShiftCount,
                order.RequiredStaff,
                order.Status,
                order.StartedAtUtc,
                order.CompletedAtUtc,
                order.Comment
            })
        });
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = now,
            Username = username,
            Action = "In Papierkorb verschoben",
            EntityType = nameof(ProductionOrder),
            EntityId = order.Id.ToString(),
            Details = $"{BuildOrderDisplayName(order)} · Produktions- und Historiendaten bleiben erhalten."
        });
        db.SaveChanges();
        tx.Commit();
    }

    public static void MoveEmployeeToTrash(int id, string? reason = null)
    {
        RequirePlanner();
        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();
        var employee = db.Employees.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Der Mitarbeiter ist nicht mehr aktiv vorhanden.");

        var username = CurrentUsername();
        var now = DateTime.UtcNow;
        employee.IsDeleted = true;
        employee.IsActive = false;
        employee.DeletedAtUtc = now;
        employee.DeletedBy = username;

        db.RecycleBinItems.Add(new RecycleBinItem
        {
            EntityType = nameof(Employee),
            EntityId = employee.Id.ToString(),
            DisplayName = $"{employee.LastName}, {employee.FirstName} · {employee.PersonnelNumber}",
            DeletedAtUtc = now,
            DeletedBy = username,
            Reason = CleanReason(reason),
            SnapshotJson = JsonSerializer.Serialize(new
            {
                employee.Id,
                employee.PersonnelNumber,
                employee.FirstName,
                employee.LastName,
                employee.Role,
                employee.Department,
                employee.WorkloadPercent,
                employee.WeeklyTargetHours,
                employee.IsActive
            })
        });
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = now,
            Username = username,
            Action = "In Papierkorb verschoben",
            EntityType = nameof(Employee),
            EntityId = employee.Id.ToString(),
            Details = $"{employee.LastName}, {employee.FirstName} · {employee.PersonnelNumber}"
        });
        db.SaveChanges();
        tx.Commit();
    }

    public static void MoveProductionActualToTrash(int id, string? reason = null)
    {
        RequirePlanner();
        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();
        var actual = db.ProductionActuals.Include(x => x.ProductionOrder).FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Die Ist-Erfassung ist nicht mehr aktiv vorhanden.");
        var username = CurrentUsername();
        var now = DateTime.UtcNow;
        actual.IsDeleted = true;
        actual.DeletedAtUtc = now;
        actual.DeletedBy = username;
        var label = $"Ist-Produktion · Auftrag {actual.ProductionOrder.OrderNumber} · {actual.Date:dd.MM.yyyy}";
        db.RecycleBinItems.Add(new RecycleBinItem
        {
            EntityType = nameof(ProductionActual),
            EntityId = actual.Id.ToString(),
            DisplayName = label,
            DeletedAtUtc = now,
            DeletedBy = username,
            Reason = CleanReason(reason),
            SnapshotJson = SerializeScalarSnapshot(actual)
        });
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = now, Username = username, Action = "In Papierkorb verschoben",
            EntityType = nameof(ProductionActual), EntityId = actual.Id.ToString(), Details = label
        });
        db.SaveChanges();
        tx.Commit();
    }

    public static void MoveDowntimeToTrash(int id, string? reason = null)
    {
        RequirePlanner();
        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();
        var downtime = db.DowntimeEntries.Include(x => x.ProductionActual).FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Der Stillstand ist nicht mehr aktiv vorhanden.");
        var username = CurrentUsername();
        var now = DateTime.UtcNow;
        downtime.IsDeleted = true;
        downtime.DeletedAtUtc = now;
        downtime.DeletedBy = username;
        var label = $"Stillstand · {downtime.Reason} · {downtime.Minutes:0.#} min · Ist-ID {downtime.ProductionActualId}";
        db.RecycleBinItems.Add(new RecycleBinItem
        {
            EntityType = nameof(DowntimeEntry),
            EntityId = downtime.Id.ToString(),
            DisplayName = label,
            DeletedAtUtc = now,
            DeletedBy = username,
            Reason = CleanReason(reason),
            SnapshotJson = SerializeScalarSnapshot(downtime)
        });
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = now, Username = username, Action = "In Papierkorb verschoben",
            EntityType = nameof(DowntimeEntry), EntityId = downtime.Id.ToString(), Details = label
        });
        db.SaveChanges();
        tx.Commit();
    }

    public static void ArchiveDeletion(AppDbContext db, object entity, string entityId, string displayName, string? reason = null)
    {
        var username = CurrentUsername();
        var now = DateTime.UtcNow;
        var entityType = entity.GetType().Name;
        db.RecycleBinItems.Add(new RecycleBinItem
        {
            EntityType = entityType,
            EntityId = entityId,
            DisplayName = displayName,
            DeletedAtUtc = now,
            DeletedBy = username,
            Reason = CleanReason(reason),
            SnapshotJson = SerializeScalarSnapshot(entity)
        });
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = now,
            Username = username,
            Action = "In Papierkorb archiviert",
            EntityType = entityType,
            EntityId = entityId,
            Details = displayName
        });
    }

    public static void Restore(long recycleBinId)
    {
        if (!SessionService.IsAdministrator)
            throw new InvalidOperationException("Nur Administratoren dürfen Datensätze aus dem Papierkorb wiederherstellen.");

        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();
        var item = db.RecycleBinItems.FirstOrDefault(x => x.Id == recycleBinId)
            ?? throw new InvalidOperationException("Der Papierkorb-Eintrag ist nicht mehr vorhanden.");
        if (item.RestoredAtUtc.HasValue)
            throw new InvalidOperationException("Dieser Papierkorb-Eintrag wurde bereits wiederhergestellt.");

        switch (item.EntityType)
        {
            case nameof(ProductionOrder):
                RestoreProductionOrder(db, item);
                break;
            case nameof(Employee):
                RestoreEmployee(db, item);
                break;
            case nameof(ProductionActual):
                RestoreProductionActual(db, item);
                break;
            case nameof(DowntimeEntry):
                RestoreDowntime(db, item);
                break;
            default:
                throw new InvalidOperationException($"Für „{item.EntityType}“ ist keine Wiederherstellung eingerichtet.");
        }

        var username = CurrentUsername();
        item.RestoredAtUtc = DateTime.UtcNow;
        item.RestoredBy = username;
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            Username = username,
            Action = "Aus Papierkorb wiederhergestellt",
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            Details = item.DisplayName
        });
        db.SaveChanges();
        tx.Commit();
    }

    private static void RestoreProductionOrder(AppDbContext db, RecycleBinItem item)
    {
        if (!int.TryParse(item.EntityId, out var id))
            throw new InvalidOperationException("Ungültige Auftrags-ID im Papierkorb.");

        var order = db.ProductionOrders.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Der zugrunde liegende Auftrag ist nicht mehr vorhanden.");

        if (!order.IsDeleted)
            throw new InvalidOperationException("Der Auftrag ist bereits aktiv.");

        if (db.ProductionOrders.Any(x => x.OrderNumber == order.OrderNumber && x.Id != order.Id))
            throw new InvalidOperationException($"Die Auftragsnummer „{order.OrderNumber}“ ist inzwischen erneut vergeben.");

        if (order.ArticleMasterId.HasValue &&
            db.ProductionOrders.Any(x => x.Id != order.Id &&
                (x.ArticleMasterId == order.ArticleMasterId || x.ArticleNumber == order.ArticleNumber) &&
                x.BatchNumber == order.BatchNumber))
            throw new InvalidOperationException($"Die Chargennummer „{order.BatchNumber}“ ist für diesen Artikel inzwischen erneut vergeben.");

        order.IsDeleted = false;
        order.DeletedAtUtc = null;
        order.DeletedBy = null;
    }

    private static void RestoreProductionActual(AppDbContext db, RecycleBinItem item)
    {
        if (!int.TryParse(item.EntityId, out var id))
            throw new InvalidOperationException("Ungültige Ist-ID im Papierkorb.");
        var actual = db.ProductionActuals.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Die zugrunde liegende Ist-Erfassung ist nicht mehr vorhanden.");
        if (!actual.IsDeleted)
            throw new InvalidOperationException("Die Ist-Erfassung ist bereits aktiv.");
        actual.IsDeleted = false;
        actual.DeletedAtUtc = null;
        actual.DeletedBy = null;
    }

    private static void RestoreDowntime(AppDbContext db, RecycleBinItem item)
    {
        if (!int.TryParse(item.EntityId, out var id))
            throw new InvalidOperationException("Ungültige Stillstands-ID im Papierkorb.");
        var downtime = db.DowntimeEntries.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Der zugrunde liegende Stillstand ist nicht mehr vorhanden.");
        if (!downtime.IsDeleted)
            throw new InvalidOperationException("Der Stillstand ist bereits aktiv.");
        var parentExists = db.ProductionActuals.IgnoreQueryFilters().Any(x => x.Id == downtime.ProductionActualId && !x.IsDeleted);
        if (!parentExists)
            throw new InvalidOperationException("Zuerst muss die zugehörige Ist-Erfassung wiederhergestellt werden.");
        downtime.IsDeleted = false;
        downtime.DeletedAtUtc = null;
        downtime.DeletedBy = null;
    }

    private static void RestoreEmployee(AppDbContext db, RecycleBinItem item)
    {
        if (!int.TryParse(item.EntityId, out var id))
            throw new InvalidOperationException("Ungültige Mitarbeiter-ID im Papierkorb.");

        var employee = db.Employees.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Der zugrunde liegende Mitarbeiter ist nicht mehr vorhanden.");

        if (!employee.IsDeleted)
            throw new InvalidOperationException("Der Mitarbeiter ist bereits aktiv.");

        employee.IsDeleted = false;
        employee.IsActive = true;
        employee.DeletedAtUtc = null;
        employee.DeletedBy = null;
    }

    private static string SerializeScalarSnapshot(object entity)
    {
        var values = entity.GetType().GetProperties()
            .Where(p => p.CanRead &&
                        (p.PropertyType.IsPrimitive ||
                         p.PropertyType.IsEnum ||
                         p.PropertyType == typeof(string) ||
                         p.PropertyType == typeof(decimal) ||
                         p.PropertyType == typeof(DateTime) ||
                         p.PropertyType == typeof(DateTime?) ||
                         p.PropertyType == typeof(TimeSpan) ||
                         p.PropertyType == typeof(TimeSpan?) ||
                         p.PropertyType == typeof(Guid) ||
                         p.PropertyType == typeof(Guid?)))
            .ToDictionary(p => p.Name, p => p.GetValue(entity));
        return JsonSerializer.Serialize(values);
    }

    private static string BuildOrderDisplayName(ProductionOrder order)
    {
        var batch = string.IsNullOrWhiteSpace(order.BatchNumber) ? "ohne Charge" : $"Charge {order.BatchNumber}";
        return $"Auftrag {order.OrderNumber} · {batch} · {order.ArticleNumber} {order.Product}".Trim();
    }

    private static string? CleanReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static string CurrentUsername() => SessionService.CurrentUser?.Username ?? "SYSTEM";

    private static void RequirePlanner()
    {
        if (!SessionService.IsPlannerOrAdmin)
            throw new InvalidOperationException("Nur Planer oder Administratoren dürfen Datensätze in den Papierkorb verschieben.");
    }
}
