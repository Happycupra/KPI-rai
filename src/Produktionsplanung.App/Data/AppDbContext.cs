using SolutionCompakt.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Data;

public enum AppDatabaseMode
{
    Automatic,
    Local,
    Central
}

public class AppDbContext : OperationalDbContext
{
    private readonly AppDatabaseMode databaseMode;

    public AppDbContext() : this(AppDatabaseMode.Automatic) { }

    internal AppDbContext(AppDatabaseMode databaseMode)
    {
        this.databaseMode = databaseMode;
    }

    internal bool IsCentralMode { get; private set; }
    internal string CentralSchemaName { get; private set; } = string.Empty;

    protected override void OnConfiguring(DbContextOptionsBuilder builder)
    {
        AppPaths.EnsureDirectories();
        builder.ReplaceService<IModelCacheKeyFactory, AppDbContextModelCacheKeyFactory>();

        IsCentralMode = databaseMode == AppDatabaseMode.Central ||
                        (databaseMode == AppDatabaseMode.Automatic && CentralModeService.IsEnabled);

        if (IsCentralMode)
        {
            var central = CentralModeService.RequireEnabledSettings();
            CentralSchemaName = CentralModeService.GetCompanySchemaName();
            builder.UseNpgsql(central.DatabaseConnectionString, options =>
                options.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null));
        }
        else
        {
            builder.UseSqlite($"Data Source={AppPaths.DatabasePath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder m) =>
        OperationalModel.Configure(m, IsCentralMode, CentralSchemaName);

    public override int SaveChanges()
    {
        if (ChangeTracker.Entries<AuditLog>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit-Einträge sind unveränderlich.");

        var changedTypes = CaptureChangedEntityTypes();
        PrepareCentralConcurrencyTokens();
        var snapshots = CaptureAuditSnapshots();
        try
        {
            var result = base.SaveChanges();
            WriteAuditSnapshots(snapshots);
            if (IsCentralMode && changedTypes.Count > 0)
                CentralRealtimeService.QueueChange(changedTypes);
            return result;
        }
        catch (DbUpdateConcurrencyException ex) when (IsCentralMode)
        {
            throw new InvalidOperationException(
                "Der Datensatz wurde zwischenzeitlich von einem anderen Benutzer geändert. Bitte Ansicht aktualisieren und die Änderung erneut durchführen.", ex);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ChangeTracker.Entries<AuditLog>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit-Einträge sind unveränderlich.");

        var changedTypes = CaptureChangedEntityTypes();
        PrepareCentralConcurrencyTokens();
        var snapshots = CaptureAuditSnapshots();
        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            await WriteAuditSnapshotsAsync(snapshots, cancellationToken);
            if (IsCentralMode && changedTypes.Count > 0)
                CentralRealtimeService.QueueChange(changedTypes);
            return result;
        }
        catch (DbUpdateConcurrencyException ex) when (IsCentralMode)
        {
            throw new InvalidOperationException(
                "Der Datensatz wurde zwischenzeitlich von einem anderen Benutzer geändert. Bitte Ansicht aktualisieren und die Änderung erneut durchführen.", ex);
        }
    }

    private HashSet<string> CaptureChangedEntityTypes() => ChangeTracker.Entries()
        .Where(x => x.Entity is not AuditLog && x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
        .Select(x => x.Metadata.ClrType.Name)
        .ToHashSet(StringComparer.Ordinal);

    private void PrepareCentralConcurrencyTokens()
    {
        if (!IsCentralMode) return;
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("ConcurrencyToken") is null) continue;
            entry.Property("ConcurrencyToken").CurrentValue = Guid.NewGuid();
        }
    }

    private List<AuditSnapshot> CaptureAuditSnapshots()
    {
        if (!SessionService.IsAuthenticated) return new();
        var snapshots = new List<AuditSnapshot>();
        foreach (var entry in ChangeTracker.Entries().Where(x => x.Entity is not AuditLog && x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            string? details = entry.State switch
            {
                EntityState.Added => BuildStateDetails(entry, false),
                EntityState.Deleted => BuildStateDetails(entry, true),
                EntityState.Modified => BuildChangeDetails(entry),
                _ => null
            };
            snapshots.Add(new(entry, entry.State, GetEntityId(entry), details));
        }
        return snapshots;
    }

    private static string? BuildChangeDetails(EntityEntry entry)
    {
        var changes = entry.Properties
            .Where(x => x.IsModified && !IsAuditExcluded(x.Metadata.Name) && x.Metadata.Name != "ConcurrencyToken")
            .Select(x => $"{x.Metadata.Name}: {AuditValue(x.OriginalValue)} → {AuditValue(x.CurrentValue)}")
            .ToArray();
        return changes.Length == 0 ? null : string.Join("; ", changes);
    }

    private static string? BuildStateDetails(EntityEntry entry, bool original)
    {
        var values = entry.Properties
            .Where(x => !IsAuditExcluded(x.Metadata.Name) && x.Metadata.Name != "ConcurrencyToken")
            .Select(x => $"{x.Metadata.Name}: {AuditValue(original ? x.OriginalValue : x.CurrentValue)}")
            .ToArray();
        return values.Length == 0 ? null : string.Join("; ", values);
    }

    private static bool IsAuditExcluded(string name) =>
        name is nameof(UserAccount.PasswordHash) or nameof(UserAccount.PasswordSalt) or nameof(RecycleBinItem.SnapshotJson);

    private static string AuditValue(object? value)
    {
        if (value is null) return "∅";
        var text = value switch
        {
            DateTime dt => dt.ToUniversalTime().ToString("O"),
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
            bool b => b ? "Ja" : "Nein",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "∅"
        };
        return text.Length <= 160 ? text : text[..157] + "…";
    }

    private void WriteAuditSnapshots(List<AuditSnapshot> snapshots)
    {
        if (snapshots.Count == 0 || SessionService.CurrentUser is null) return;
        foreach (var snapshot in snapshots)
        {
            var id = snapshot.State == EntityState.Added ? GetEntityId(snapshot.Entry) : snapshot.EntityIdBefore;
            AuditLogs.Add(new AuditLog
            {
                TimestampUtc = DateTime.UtcNow,
                Username = SessionService.CurrentUser.Username,
                Action = snapshot.State switch
                {
                    EntityState.Added => "Erstellt",
                    EntityState.Modified => "Geändert",
                    EntityState.Deleted => "Gelöscht",
                    _ => snapshot.State.ToString()
                },
                EntityType = snapshot.Entry.Metadata.ClrType.Name,
                EntityId = id,
                Details = snapshot.Details
            });
        }
        PrepareCentralConcurrencyTokens();
        base.SaveChanges();
    }

    private async Task WriteAuditSnapshotsAsync(List<AuditSnapshot> snapshots, CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0 || SessionService.CurrentUser is null) return;
        foreach (var snapshot in snapshots)
        {
            var id = snapshot.State == EntityState.Added ? GetEntityId(snapshot.Entry) : snapshot.EntityIdBefore;
            AuditLogs.Add(new AuditLog
            {
                TimestampUtc = DateTime.UtcNow,
                Username = SessionService.CurrentUser.Username,
                Action = snapshot.State switch
                {
                    EntityState.Added => "Erstellt",
                    EntityState.Modified => "Geändert",
                    EntityState.Deleted => "Gelöscht",
                    _ => snapshot.State.ToString()
                },
                EntityType = snapshot.Entry.Metadata.ClrType.Name,
                EntityId = id,
                Details = snapshot.Details
            });
        }
        PrepareCentralConcurrencyTokens();
        await base.SaveChangesAsync(cancellationToken);
    }

    private static string? GetEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;
        var values = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var result = string.Join("/", values);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private sealed record AuditSnapshot(EntityEntry Entry, EntityState State, string? EntityIdBefore, string? Details);
}

public sealed class AppDbContextModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context) => Create(context, false);

    public object Create(DbContext context, bool designTime)
    {
        return context is AppDbContext app
            ? (context.GetType(), app.IsCentralMode, app.CentralSchemaName, designTime)
            : (context.GetType(), designTime);
    }
}
