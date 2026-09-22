using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class ArticleService
{
    public static List<ArticleMaster> Search(string? search = null, bool activeOnly = false)
    {
        using var db = new AppDbContext();
        var query = db.ArticleMasters.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(x => x.IsActive);
        var term = search?.Trim() ?? "";
        return query.OrderBy(x => x.ArticleNumber).AsEnumerable()
            .Where(x => term.Length == 0 || x.ArticleNumber.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static ArticleMaster GetDetails(int id)
    {
        using var db = new AppDbContext();
        return db.ArticleMasters.AsNoTracking().Single(x => x.Id == id);
    }

    public static int Save(ArticleMaster input)
    {
        BatchService.RequirePlanner();
        if (string.IsNullOrWhiteSpace(input.ArticleNumber) || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Unit))
            throw new InvalidOperationException("Artikelnummer, Name und Einheit sind erforderlich.");
        if (!double.IsFinite(input.DefaultIdealRatePerHour) || input.DefaultIdealRatePerHour <= 0 ||
            input.DefaultQuantity.HasValue && (!double.IsFinite(input.DefaultQuantity.Value) || input.DefaultQuantity <= 0))
            throw new InvalidOperationException("Sollrate und optionale Standardmenge müssen grösser als 0 sein.");
        using var db = new AppDbContext();
        using var transaction = db.Database.BeginTransaction();
        var number = input.ArticleNumber.Trim();
        if (db.ArticleMasters.Any(x => x.Id != input.Id && x.ArticleNumber == number))
            throw new InvalidOperationException("Diese Artikelnummer existiert bereits.");
        if (input.DefaultRoutingId.HasValue && !db.ManufacturingRoutings.Any(x => x.Id == input.DefaultRoutingId && x.IsActive))
            throw new InvalidOperationException("Bitte einen aktiven Arbeitsplan auswählen.");
        var entity = input.Id == 0 ? new ArticleMaster() : db.ArticleMasters.Single(x => x.Id == input.Id);
        if (entity.Id != 0 && entity.ArticleNumber != number && db.ProductionOrders.Any(x => x.ArticleMasterId == entity.Id || x.ArticleNumber == entity.ArticleNumber))
            throw new InvalidOperationException("Die Artikelnummer wird bereits verwendet und ist gesperrt.");
        if (entity.Id == 0) db.ArticleMasters.Add(entity);
        entity.ArticleNumber = number;
        entity.Name = input.Name.Trim();
        entity.Unit = input.Unit.Trim();
        entity.DefaultQuantity = input.DefaultQuantity;
        entity.DefaultIdealRatePerHour = input.DefaultIdealRatePerHour;
        entity.DefaultRoutingId = input.DefaultRoutingId;
        entity.Notes = input.Notes?.Trim();
        entity.IsActive = input.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        transaction.Commit();
        return entity.Id;
    }

    public static void Deactivate(int id)
    {
        var article = GetDetails(id);
        article.IsActive = false;
        Save(article);
    }
}
