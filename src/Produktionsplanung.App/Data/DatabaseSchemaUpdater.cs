using Microsoft.EntityFrameworkCore;

namespace Produktionsplanung.App.Data;

public static class DatabaseSchemaUpdater
{
    public static void Apply(AppDbContext db)
    {
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
    }
}
