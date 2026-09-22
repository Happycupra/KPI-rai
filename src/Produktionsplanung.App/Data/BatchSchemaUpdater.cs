using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Data;

internal static class BatchSchemaUpdater
{
    private static bool Applied(AppDbContext db)
    {
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='BatchSchemaV1';";
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    public static void BackupBeforeUpgrade(AppDbContext db)
    {
        if (Applied(db)) return;
        var path = Path.Combine(AppPaths.BackupsDirectory, $"before-batches-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.kpibackup");
        BackupService.CreateBackup(path, AppSettingsService.Load());
    }

    public static void Apply(AppDbContext db)
    {
        if (Applied(db)) return;
        using var transaction = db.Database.BeginTransaction();
        AddColumn(db, "ArticleMasters", "DefaultQuantity", "REAL NULL");
        AddColumn(db, "ArticleMasters", "DefaultRoutingId", "INTEGER NULL REFERENCES ManufacturingRoutings(Id)");
        AddColumn(db, "ProductionOrders", "ArticleMasterId", "INTEGER NULL REFERENCES ArticleMasters(Id)");
        AddColumn(db, "ProductionOrders", "ManufacturingRoutingId", "INTEGER NULL REFERENCES ManufacturingRoutings(Id)");
        AddColumn(db, "ProductionOrders", "StartedAtUtc", "TEXT NULL");
        AddColumn(db, "ProductionOrders", "CompletedAtUtc", "TEXT NULL");
        AddColumn(db, "ProductionOrders", "IdealRatePerHourSnapshot", "REAL NULL");
        db.Database.ExecuteSqlRaw("""
            UPDATE ProductionOrders SET ArticleMasterId=(SELECT a.Id FROM ArticleMasters a
              WHERE a.ArticleNumber=ProductionOrders.ArticleNumber AND a.Name=ProductionOrders.Product AND a.Unit=ProductionOrders.Unit)
            WHERE ArticleMasterId IS NULL AND trim(BatchNumber)<>''
              AND (SELECT COUNT(*) FROM ArticleMasters a WHERE a.ArticleNumber=ProductionOrders.ArticleNumber
                   AND a.Name=ProductionOrders.Product AND a.Unit=ProductionOrders.Unit)=1
              AND (SELECT COUNT(*) FROM ProductionOrders p WHERE p.ArticleNumber=ProductionOrders.ArticleNumber
                   AND p.BatchNumber=ProductionOrders.BatchNumber)=1
              AND NOT EXISTS (SELECT 1 FROM ProductionActuals a WHERE a.ProductionOrderId=ProductionOrders.Id
                   AND a.BatchNumber IS NOT NULL AND a.BatchNumber<>'' AND a.BatchNumber<>ProductionOrders.BatchNumber);
            UPDATE ProductionOrders SET CompletedAtUtc=(SELECT MAX(j.CompletedAtUtc) FROM JobCards j WHERE j.ProductionOrderId=ProductionOrders.Id)
            WHERE Status='Abgeschlossen' AND CompletedAtUtc IS NULL
              AND EXISTS (SELECT 1 FROM JobCards j WHERE j.ProductionOrderId=ProductionOrders.Id)
              AND NOT EXISTS (SELECT 1 FROM JobCards j WHERE j.ProductionOrderId=ProductionOrders.Id AND (j.Status<>'Fertig' OR j.CompletedAtUtc IS NULL));
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductionOrders_ArticleMasterId_BatchNumber ON ProductionOrders(ArticleMasterId,BatchNumber) WHERE ArticleMasterId IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_ProductionOrders_Status_CompletedAtUtc ON ProductionOrders(Status,CompletedAtUtc);
            CREATE TABLE BatchSchemaV1 (Version INTEGER NOT NULL);
            INSERT INTO BatchSchemaV1 VALUES (1);
            """);
        transaction.Commit();
    }

    private static void AddColumn(AppDbContext db, string table, string column, string definition)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"PRAGMA table_info({table});";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                if (reader.GetString(1) == column) return;
        command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        command.ExecuteNonQuery();
    }
}
