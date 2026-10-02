using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed record CentralDatabaseInitializationResult(bool Created, bool Migrated, string SchemaName, string Message);

public static class CentralDatabaseService
{
    public static async Task<CentralDatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var centralSettings = CentralModeService.RequireEnabledSettings();
        var schema = CentralModeService.GetCompanySchemaName();

        await using var central = new AppDbContext(AppDatabaseMode.Central);
        await central.Database.OpenConnectionAsync(cancellationToken);

        var exists = await TableExistsAsync(central.Database.GetDbConnection(), schema, "UserAccounts", cancellationToken);
        var created = false;
        var migrated = false;

        if (!exists)
        {
            var script = central.Database.GenerateCreateScript();
            script = script.Replace(
                $"CREATE SCHEMA \"{schema}\";",
                $"CREATE SCHEMA IF NOT EXISTS \"{schema}\";",
                StringComparison.Ordinal);
            await central.Database.ExecuteSqlRawAsync(script, cancellationToken);
            created = true;

            if (centralSettings.AutoMigrateLocalData && File.Exists(AppPaths.DatabasePath))
                migrated = await MigrateLocalDatabaseAsync(central, cancellationToken);
        }

        if (!await central.UserAccounts.AsNoTracking().AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Die zentrale Datenbank enthält noch keine Benutzer. Aktivieren Sie den Zentralbetrieb zuerst auf einer bestehenden SolutionCompakt-Installation mit lokaler Firmen- und Benutzerkonfiguration, damit die lokalen Daten einmalig übernommen werden können.");
        }

        return new CentralDatabaseInitializationResult(
            created,
            migrated,
            schema,
            migrated
                ? "Zentrale Datenbank wurde erstellt und die lokale Datenbank vollständig übernommen."
                : created
                    ? "Zentrale Datenbank wurde neu erstellt."
                    : "Zentrale Datenbank ist erreichbar.");
    }

    public static async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        CentralModeService.RequireEnabledSettings();
        await using var db = new AppDbContext(AppDatabaseMode.Central);
        return await db.Database.CanConnectAsync(cancellationToken);
    }

    private static async Task<bool> MigrateLocalDatabaseAsync(AppDbContext central, CancellationToken cancellationToken)
    {
        await using var local = new AppDbContext(AppDatabaseMode.Local);
        if (!await local.UserAccounts.AsNoTracking().AnyAsync(cancellationToken))
            return false;

        await using var transaction = await central.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await CopyAsync<Qualification>(local, central, cancellationToken);
            await CopyAsync<Employee>(local, central, cancellationToken);
            await CopyAsync<EmployeeQualification>(local, central, cancellationToken);
            await CopyAsync<Shift>(local, central, cancellationToken);
            await CopyAsync<Workstation>(local, central, cancellationToken);
            await CopyAsync<WorkstationShiftRule>(local, central, cancellationToken);
            await CopyAsync<Absence>(local, central, cancellationToken);
            await CopyAsync<OperatingCalendarDay>(local, central, cancellationToken);
            await CopyAsync<WorkTimeEntry>(local, central, cancellationToken);
            await CopyAsync<PlanningAssignment>(local, central, cancellationToken);
            await CopyAsync<OperationDefinition>(local, central, cancellationToken);
            await CopyAsync<ManufacturingRouting>(local, central, cancellationToken);
            await CopyAsync<RoutingStep>(local, central, cancellationToken);
            await CopyAsync<ArticleMaster>(local, central, cancellationToken);
            await CopyAsync<ProductionOrder>(local, central, cancellationToken);
            await CopyAsync<ProductionRunSlot>(local, central, cancellationToken);
            await CopyAsync<ProductionActual>(local, central, cancellationToken);
            await CopyAsync<DowntimeEntry>(local, central, cancellationToken);
            await CopyAsync<JobCard>(local, central, cancellationToken);
            await CopyAsync<UserAccount>(local, central, cancellationToken);
            await CopyAsync<ShiftHandover>(local, central, cancellationToken);
            await CopyAsync<UserMessage>(local, central, cancellationToken);
            await CopyAsync<AuditLog>(local, central, cancellationToken);
            await CopyAsync<RecycleBinItem>(local, central, cancellationToken);

            await ResetSequencesAsync(central, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task CopyAsync<TEntity>(
        AppDbContext local,
        AppDbContext central,
        CancellationToken cancellationToken) where TEntity : class
    {
        var items = await local.Set<TEntity>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (items.Count == 0) return;

        central.Set<TEntity>().AddRange(items);
        await central.SaveChangesAsync(cancellationToken);
        central.ChangeTracker.Clear();
    }

    private static async Task ResetSequencesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();
            if (key is null || key.Properties.Count != 1) continue;
            var property = key.Properties[0];
            if (property.ClrType != typeof(int) && property.ClrType != typeof(long)) continue;
            if (property.ValueGenerated == ValueGenerated.Never) continue;

            var table = entityType.GetTableName();
            var schema = entityType.GetSchema();
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(schema)) continue;
            var store = StoreObjectIdentifier.Table(table, schema);
            var column = property.GetColumnName(store);
            if (string.IsNullOrWhiteSpace(column)) continue;

            var qualified = $"\"{schema}\".\"{table}\"";
            var sequenceTarget = qualified.Replace("'", "''", StringComparison.Ordinal);
            var sql = $"SELECT setval(pg_get_serial_sequence('{sequenceTarget}', '{column}'), " +
                      $"GREATEST(COALESCE(MAX(\"{column}\"), 0), 1), " +
                      $"COALESCE(MAX(\"{column}\"), 0) > 0) FROM {qualified};";
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table);";
        var pSchema = command.CreateParameter();
        pSchema.ParameterName = "schema";
        pSchema.Value = schema;
        command.Parameters.Add(pSchema);
        var pTable = command.CreateParameter();
        pTable.ParameterName = "table";
        pTable.Value = table;
        command.Parameters.Add(pTable);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }
}
