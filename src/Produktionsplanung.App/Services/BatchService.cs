using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed record CreateBatchRequest(int ArticleId, string OrderNumber, string BatchNumber, double Quantity,
    DateTime PlannedDate, int WorkstationId, int ShiftId, int PlannedShiftCount, int RequiredStaff, int? RoutingId = null);

public sealed record BatchFilter(string Mode = "Alle", string Search = "", int? ArticleId = null,
    DateTime? CompletedFrom = null, DateTime? CompletedTo = null);

public sealed class BatchRow
{
    public int Id { get; init; }
    public int? ArticleId { get; init; }
    public string OrderNumber { get; init; } = "";
    public string BatchNumber { get; init; } = "";
    public string ArticleNumber { get; init; } = "";
    public string Product { get; init; } = "";
    public double Quantity { get; init; }
    public string Unit { get; init; } = "";
    public string Workstation { get; init; } = "";
    public DateTime PlannedDate { get; init; }
    public string Status { get; init; } = "";
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public int CompletedSteps { get; init; }
    public int TotalSteps { get; init; }
    public string QuantityText => $"{Quantity:N2} {Unit}";
    public string ProgressText => TotalSteps == 0 ? "Keine Arbeitskarten" : $"{CompletedSteps} von {TotalSteps} fertig";
    public string BatchLabel => string.IsNullOrWhiteSpace(BatchNumber) ? $"Auftrag {OrderNumber}" : BatchNumber;
    public string ArticleText => $"{ArticleNumber} – {Product}";
    public string AssignmentText => ArticleId.HasValue ? "" : "Artikelzuordnung offen";
    public string StartedText => FormatDate(StartedAtUtc, "Nicht erfasst");
    public string CompletedText => FormatDate(CompletedAtUtc, Status == "Abgeschlossen" ? "Abschlussdatum unbekannt" : "Nicht abgeschlossen");
    public static string FormatDate(DateTime? value, string missing) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm") : missing;
}

public sealed record BatchDetails(BatchRow Batch, IReadOnlyList<JobCard> JobCards, IReadOnlyList<ProductionActual> Actuals);

public static class BatchService
{
    public static void RequirePlanner()
    {
        if (!SessionService.IsPlannerOrAdmin) throw new InvalidOperationException("Nur Planer oder Administratoren dürfen Produktionsdaten ändern.");
    }

    public static List<BatchRow> Search(BatchFilter? filter = null)
    {
        filter ??= new();
        if (filter.CompletedFrom.HasValue && filter.CompletedTo.HasValue && filter.CompletedFrom > filter.CompletedTo)
            throw new InvalidOperationException("Das Enddatum muss nach dem Startdatum liegen.");
        using var db = new AppDbContext();
        var query = db.ProductionOrders.AsNoTracking().Include(x => x.Workstation).AsQueryable();
        if (filter.ArticleId.HasValue) query = query.Where(x => x.ArticleMasterId == filter.ArticleId);
        var today = DateTime.Today;
        query = filter.Mode switch
        {
            "Heute" => query.Where(x => x.RunSlots.Any(s => s.Date == today)),
            "Alle offenen" => query.Where(x => x.Status != "Abgeschlossen"),
            "Laufend" => query.Where(x => x.Status == "Läuft"),
            "Probleme" => query.Where(x => x.Status == "Problem"),
            "Abgeschlossen" => query.Where(x => x.Status == "Abgeschlossen"),
            _ => query
        };
        if (filter.CompletedFrom.HasValue)
        {
            var from = filter.CompletedFrom.Value.Date.ToUniversalTime();
            query = query.Where(x => x.CompletedAtUtc >= from);
        }
        if (filter.CompletedTo.HasValue)
        {
            var until = filter.CompletedTo.Value.Date.AddDays(1).ToUniversalTime();
            query = query.Where(x => x.CompletedAtUtc < until);
        }
        var orders = (filter.Mode == "Abgeschlossen"
            ? query.OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.Id)
            : query.OrderByDescending(x => x.Id)).ToList();
        var ids = orders.Select(x => x.Id).ToArray();
        var cards = db.JobCards.AsNoTracking().Where(x => ids.Contains(x.ProductionOrderId))
            .Select(x => new { x.ProductionOrderId, x.Status }).ToList().ToLookup(x => x.ProductionOrderId);
        var term = filter.Search.Trim();
        return orders.Where(x => term.Length == 0 || new[] { x.OrderNumber, x.ArticleNumber, x.BatchNumber, x.Product }
                .Any(v => v.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(x => ToRow(x, cards[x.Id].Count(), cards[x.Id].Count(c => c.Status == "Fertig"))).ToList();
    }

    public static BatchDetails GetDetails(int id)
    {
        using var db = new AppDbContext();
        var order = db.ProductionOrders.AsNoTracking().Include(x => x.Workstation).SingleOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Diese Charge existiert nicht mehr.");
        var cards = db.JobCards.AsNoTracking().Include(x => x.Workstation).Include(x => x.Employee)
            .Where(x => x.ProductionOrderId == id).OrderBy(x => x.SequenceNumber).ToList();
        var actuals = db.ProductionActuals.AsNoTracking().Include(x => x.Downtimes).Where(x => x.ProductionOrderId == id).OrderBy(x => x.Date).ToList();
        return new(ToRow(order, cards.Count, cards.Count(x => x.Status == "Fertig")), cards, actuals);
    }

    private static BatchRow ToRow(ProductionOrder x, int total, int completed) => new()
    {
        Id = x.Id, ArticleId = x.ArticleMasterId, OrderNumber = x.OrderNumber, BatchNumber = x.BatchNumber,
        ArticleNumber = x.ArticleNumber, Product = x.Product, Quantity = x.Quantity, Unit = x.Unit,
        Workstation = x.Workstation.Name, PlannedDate = x.PlannedDate, Status = x.Status,
        StartedAtUtc = x.StartedAtUtc, CompletedAtUtc = x.CompletedAtUtc, TotalSteps = total, CompletedSteps = completed
    };

    public static int CreateFromArticle(CreateBatchRequest request)
    {
        RequirePlanner();
        using var db = new AppDbContext();
        using var transaction = db.Database.BeginTransaction();
        var article = db.ArticleMasters.SingleOrDefault(x => x.Id == request.ArticleId && x.IsActive)
            ?? throw new InvalidOperationException("Bitte einen aktiven Artikel auswählen.");
        if (string.IsNullOrWhiteSpace(request.OrderNumber) || string.IsNullOrWhiteSpace(request.BatchNumber))
            throw new InvalidOperationException("Auftragsnummer und Chargennummer sind erforderlich.");
        if (!double.IsFinite(request.Quantity) || request.Quantity <= 0 || request.RequiredStaff < 1 || request.PlannedShiftCount is < 1 or > ProductionScheduleService.MaxPlannedShiftCount)
            throw new InvalidOperationException("Menge, Personalbedarf und Schichtanzahl sind ungültig.");
        var batch = request.BatchNumber.Trim();
        var number = request.OrderNumber.Trim();
        if (db.ProductionOrders.Any(x => x.OrderNumber == number)) throw new InvalidOperationException("Diese Auftragsnummer existiert bereits.");
        if (db.ProductionOrders.Any(x => (x.ArticleMasterId == article.Id || x.ArticleNumber == article.ArticleNumber) && x.BatchNumber == batch))
            throw new InvalidOperationException("Diese Chargennummer existiert für den Artikel bereits.");
        if (!db.Workstations.Any(x => x.Id == request.WorkstationId && x.IsActive))
            throw new InvalidOperationException("Bitte einen aktiven Arbeitsplatz auswählen.");
        var routingId = request.RoutingId ?? article.DefaultRoutingId;
        if (routingId.HasValue && !db.ManufacturingRoutings.Any(x => x.Id == routingId && x.IsActive))
            throw new InvalidOperationException("Der zugeordnete Arbeitsplan ist nicht aktiv.");
        var shift = db.Shifts.SingleOrDefault(x => x.Id == request.ShiftId) ?? throw new InvalidOperationException("Bitte Startschicht auswählen.");
        var order = new ProductionOrder
        {
            ArticleMasterId = article.Id, ArticleNumber = article.ArticleNumber, Product = article.Name, Unit = article.Unit,
            ManufacturingRoutingId = routingId, IdealRatePerHourSnapshot = article.DefaultIdealRatePerHour,
            BatchNumber = batch, OrderNumber = number, Quantity = request.Quantity, PlannedDate = request.PlannedDate.Date,
            WorkstationId = request.WorkstationId, ShiftId = shift.Id, PlannedStart = shift.StartTime, PlannedEnd = shift.EndTime,
            PlannedShiftCount = request.PlannedShiftCount, RequiredStaff = request.RequiredStaff, Status = "Geplant"
        };
        db.ProductionOrders.Add(order);
        db.SaveChanges();
        ProductionScheduleService.SyncRunSlots(db, order);
        db.SaveChanges();
        if (db.ProductionRunSlots.Count(x => x.ProductionOrderId == order.Id) != request.PlannedShiftCount)
            throw new InvalidOperationException("Die Terminierung ist für diesen Arbeitsplatz nicht vollständig freigegeben.");
        transaction.Commit();
        return order.Id;
    }

    public static void Complete(int id)
    {
        RequirePlanner();
        using var db = new AppDbContext();
        using var transaction = db.Database.BeginTransaction();
        var order = db.ProductionOrders.Single(x => x.Id == id);
        if (order.Status == "Abgeschlossen") return;
        if (db.JobCards.Any(x => x.ProductionOrderId == id && x.Status != "Fertig"))
            throw new InvalidOperationException("Zuerst alle Arbeitsgänge abschliessen.");
        order.Status = "Abgeschlossen";
        order.CompletedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        transaction.Commit();
    }

    public static void Reopen(int id, string reason)
    {
        RequirePlanner();
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Eine Begründung ist erforderlich.");
        using var db = new AppDbContext();
        using var transaction = db.Database.BeginTransaction();
        var order = db.ProductionOrders.Single(x => x.Id == id);
        if (order.Status != "Abgeschlossen") throw new InvalidOperationException("Die Charge ist bereits offen.");
        db.AuditLogs.Add(new AuditLog { Action = "Charge wieder geöffnet", EntityType = nameof(ProductionOrder), EntityId = id.ToString(),
            Username = SessionService.CurrentUser!.Username, Details = $"Bisheriger Abschluss: {order.CompletedAtUtc:O}; Grund: {reason.Trim()}" });
        order.Status = "Pausiert";
        order.CompletedAtUtc = null;
        db.SaveChanges();
        transaction.Commit();
    }

    public static bool CanEdit(AppDbContext db, int orderId, out string message)
    {
        message = !SessionService.IsPlannerOrAdmin
            ? "Nur Planer oder Administratoren dürfen Produktionsdaten ändern."
            : db.ProductionOrders.Any(x => x.Id == orderId) ? "" : "Die Charge bzw. der Auftrag ist nicht mehr aktiv vorhanden.";
        return message.Length == 0;
    }
}
