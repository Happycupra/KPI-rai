using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Produktionsplanung.App.Data;

public static class DatabaseSchemaUpdater
{
    public static void Apply(AppDbContext db)
    {
        EnsureColumn(db, "Workstations", "RequiredQualificationId", "INTEGER NULL");
        EnsureColumn(db, "Workstations", "RequiredQualificationLevel", "INTEGER NOT NULL DEFAULT 0");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Workstations_RequiredQualificationId ON Workstations (RequiredQualificationId);");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ProductionOrders (
                Id INTEGER NOT NULL CONSTRAINT PK_ProductionOrders PRIMARY KEY AUTOINCREMENT,
                OrderNumber TEXT NOT NULL,
                Product TEXT NOT NULL,
                Description TEXT NULL,
                Quantity REAL NOT NULL,
                Unit TEXT NOT NULL,
                Priority TEXT NOT NULL,
                PlannedDate TEXT NOT NULL,
                PlannedStart TEXT NULL,
                PlannedEnd TEXT NULL,
                WorkstationId INTEGER NOT NULL,
                ShiftId INTEGER NULL,
                RequiredStaff INTEGER NOT NULL,
                Status TEXT NOT NULL,
                Comment TEXT NULL,
                CONSTRAINT FK_ProductionOrders_Workstations_WorkstationId
                    FOREIGN KEY (WorkstationId) REFERENCES Workstations (Id) ON DELETE RESTRICT,
                CONSTRAINT FK_ProductionOrders_Shifts_ShiftId
                    FOREIGN KEY (ShiftId) REFERENCES Shifts (Id) ON DELETE SET NULL
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductionOrders_OrderNumber ON ProductionOrders (OrderNumber);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ProductionOrders_PlannedDate_WorkstationId_ShiftId ON ProductionOrders (PlannedDate, WorkstationId, ShiftId);");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ProductionActuals (
                Id INTEGER NOT NULL CONSTRAINT PK_ProductionActuals PRIMARY KEY AUTOINCREMENT,
                ProductionOrderId INTEGER NOT NULL,
                Date TEXT NOT NULL,
                TotalQuantity REAL NOT NULL,
                GoodQuantity REAL NOT NULL,
                ScrapQuantity REAL NOT NULL,
                PlannedProductionMinutes REAL NOT NULL,
                RunMinutes REAL NOT NULL,
                IdealRatePerHour REAL NOT NULL,
                Comment TEXT NULL,
                CONSTRAINT FK_ProductionActuals_ProductionOrders_ProductionOrderId
                    FOREIGN KEY (ProductionOrderId) REFERENCES ProductionOrders (Id) ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ProductionActuals_ProductionOrderId_Date ON ProductionActuals (ProductionOrderId, Date);");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS DowntimeEntries (
                Id INTEGER NOT NULL CONSTRAINT PK_DowntimeEntries PRIMARY KEY AUTOINCREMENT,
                ProductionActualId INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                Minutes REAL NOT NULL,
                Comment TEXT NULL,
                CONSTRAINT FK_DowntimeEntries_ProductionActuals_ProductionActualId
                    FOREIGN KEY (ProductionActualId) REFERENCES ProductionActuals (Id) ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_DowntimeEntries_ProductionActualId ON DowntimeEntries (ProductionActualId);");
        db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER IF NOT EXISTS PreventOrderHistoryDeletion
            BEFORE DELETE ON ProductionOrders
            WHEN EXISTS (SELECT 1 FROM ProductionActuals WHERE ProductionOrderId = OLD.Id)
            BEGIN
                SELECT RAISE(ABORT, 'Auftrag mit Ist-Produktion darf nicht gelöscht werden.');
            END;
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS UserAccounts (
                Id INTEGER NOT NULL CONSTRAINT PK_UserAccounts PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                Role TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                LastLoginAtUtc TEXT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_UserAccounts_Username ON UserAccounts (Username);");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS AuditLogs (
                Id INTEGER NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                Username TEXT NOT NULL,
                Action TEXT NOT NULL,
                EntityType TEXT NOT NULL,
                EntityId TEXT NULL,
                Details TEXT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_AuditLogs_TimestampUtc ON AuditLogs (TimestampUtc);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_AuditLogs_Username ON AuditLogs (Username);");
    }

    private static void EnsureColumn(AppDbContext db, string tableName, string columnName, string definition)
    {
        var connection = db.Database.GetDbConnection();
        var closeAfter = connection.State != ConnectionState.Open;
        if (closeAfter)
            connection.Open();

        try
        {
            using var check = connection.CreateCommand();
            check.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            using var reader = check.ExecuteReader();
            var exists = false;
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            reader.Close();

            if (exists)
                return;

            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {definition};";
            alter.ExecuteNonQuery();
        }
        finally
        {
            if (closeAfter)
                connection.Close();
        }
    }
}
