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

public class AppDbContext : DbContext
{
    private readonly AppDatabaseMode databaseMode;

    public AppDbContext() : this(AppDatabaseMode.Automatic) { }

    internal AppDbContext(AppDatabaseMode databaseMode)
    {
        this.databaseMode = databaseMode;
    }

    internal bool IsCentralMode { get; private set; }
    internal string CentralSchemaName { get; private set; } = string.Empty;

    public DbSet<ArticleMaster> ArticleMasters => Set<ArticleMaster>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();
    public DbSet<Workstation> Workstations => Set<Workstation>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<WorkstationShiftRule> WorkstationShiftRules => Set<WorkstationShiftRule>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<OperatingCalendarDay> OperatingCalendarDays => Set<OperatingCalendarDay>();
    public DbSet<WorkTimeEntry> WorkTimeEntries => Set<WorkTimeEntry>();
    public DbSet<PlanningAssignment> PlanningAssignments => Set<PlanningAssignment>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionRunSlot> ProductionRunSlots => Set<ProductionRunSlot>();
    public DbSet<ProductionActual> ProductionActuals => Set<ProductionActual>();
    public DbSet<DowntimeEntry> DowntimeEntries => Set<DowntimeEntry>();
    public DbSet<OperationDefinition> OperationDefinitions => Set<OperationDefinition>();
    public DbSet<ManufacturingRouting> ManufacturingRoutings => Set<ManufacturingRouting>();
    public DbSet<RoutingStep> RoutingSteps => Set<RoutingStep>();
    public DbSet<JobCard> JobCards => Set<JobCard>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RecycleBinItem> RecycleBinItems => Set<RecycleBinItem>();
    public DbSet<ShiftHandover> ShiftHandovers => Set<ShiftHandover>();
    public DbSet<UserMessage> UserMessages => Set<UserMessage>();

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

    protected override void OnModelCreating(ModelBuilder m)
    {
        if (IsCentralMode)
            m.HasDefaultSchema(CentralSchemaName);

        m.Entity<ArticleMaster>().HasOne(x => x.DefaultRouting).WithMany().HasForeignKey(x => x.DefaultRoutingId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasOne(x => x.ArticleMaster).WithMany().HasForeignKey(x => x.ArticleMasterId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasOne(x => x.ManufacturingRouting).WithMany().HasForeignKey(x => x.ManufacturingRoutingId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasIndex(x => new { x.ArticleMasterId, x.BatchNumber }).IsUnique().HasFilter("\"ArticleMasterId\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        m.Entity<ProductionOrder>().HasIndex(x => new { x.Status, x.CompletedAtUtc });
        m.Entity<ProductionOrder>().HasQueryFilter(x => !x.IsDeleted);
        m.Entity<Employee>().HasQueryFilter(x => !x.IsDeleted);
        m.Entity<ProductionActual>().HasQueryFilter(x => !x.IsDeleted);
        m.Entity<DowntimeEntry>().HasQueryFilter(x => !x.IsDeleted);

        m.Entity<ArticleMaster>().HasIndex(x => x.ArticleNumber).IsUnique();
        m.Entity<ArticleMaster>().HasIndex(x => x.Name);
        m.Entity<Employee>().HasIndex(x => x.PersonnelNumber).IsUnique();
        m.Entity<EmployeeQualification>().HasKey(x => new { x.EmployeeId, x.QualificationId });
        m.Entity<EmployeeQualification>().HasOne(x => x.Employee).WithMany(x => x.Qualifications).HasForeignKey(x => x.EmployeeId);
        m.Entity<EmployeeQualification>().HasOne(x => x.Qualification).WithMany(x => x.Employees).HasForeignKey(x => x.QualificationId);
        m.Entity<Workstation>().HasOne(x => x.RequiredQualification).WithMany().HasForeignKey(x => x.RequiredQualificationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<WorkstationShiftRule>().HasIndex(x => new { x.WorkstationId, x.ShiftId }).IsUnique();
        m.Entity<WorkstationShiftRule>().HasOne(x => x.Workstation).WithMany(x => x.ShiftRules).HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<WorkstationShiftRule>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<OperatingCalendarDay>().HasIndex(x => x.Date).IsUnique();
        m.Entity<WorkTimeEntry>().HasIndex(x => new { x.EmployeeId, x.Date });
        m.Entity<WorkTimeEntry>().HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasIndex(x => x.OrderNumber).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        m.Entity<ProductionOrder>().HasIndex(x => new { x.PlannedDate, x.WorkstationId, x.ShiftId });
        m.Entity<ProductionOrder>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ProductionRunSlot>().HasIndex(x => new { x.ProductionOrderId, x.SequenceNumber }).IsUnique();
        m.Entity<ProductionRunSlot>().HasIndex(x => new { x.Date, x.ShiftId });
        m.Entity<ProductionRunSlot>().HasOne(x => x.ProductionOrder).WithMany(x => x.RunSlots).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<ProductionRunSlot>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionActual>().HasIndex(x => new { x.ProductionOrderId, x.Date });
        m.Entity<ProductionActual>().HasIndex(x => x.ProductionRunSlotId);
        m.Entity<ProductionActual>().HasOne(x => x.ProductionOrder).WithMany(x => x.Actuals).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<ProductionActual>().HasOne(x => x.ProductionRunSlot).WithMany(x => x.Actuals).HasForeignKey(x => x.ProductionRunSlotId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<DowntimeEntry>().HasIndex(x => x.ProductionActualId);
        m.Entity<DowntimeEntry>().HasOne(x => x.ProductionActual).WithMany(x => x.Downtimes).HasForeignKey(x => x.ProductionActualId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<OperationDefinition>().HasIndex(x => x.Code).IsUnique();
        m.Entity<OperationDefinition>().HasOne(x => x.DefaultWorkstation).WithMany().HasForeignKey(x => x.DefaultWorkstationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<OperationDefinition>().HasOne(x => x.RequiredQualification).WithMany().HasForeignKey(x => x.RequiredQualificationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ManufacturingRouting>().HasIndex(x => x.Product);
        m.Entity<RoutingStep>().HasIndex(x => new { x.ManufacturingRoutingId, x.SequenceNumber }).IsUnique();
        m.Entity<RoutingStep>().HasOne(x => x.ManufacturingRouting).WithMany(x => x.Steps).HasForeignKey(x => x.ManufacturingRoutingId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<RoutingStep>().HasOne(x => x.OperationDefinition).WithMany(x => x.RoutingSteps).HasForeignKey(x => x.OperationDefinitionId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<RoutingStep>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JobCard>().HasIndex(x => new { x.ProductionOrderId, x.SequenceNumber }).IsUnique();
        m.Entity<JobCard>().HasIndex(x => new { x.WorkstationId, x.Status });
        m.Entity<JobCard>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<JobCard>().HasOne(x => x.RoutingStep).WithMany().HasForeignKey(x => x.RoutingStepId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<JobCard>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JobCard>().HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<JobCard>().HasOne(x => x.RequiredQualification).WithMany().HasForeignKey(x => x.RequiredQualificationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<UserAccount>().HasIndex(x => x.Username).IsUnique();
        m.Entity<AuditLog>().HasIndex(x => x.TimestampUtc);
        m.Entity<AuditLog>().HasIndex(x => x.Username);
        m.Entity<RecycleBinItem>().HasIndex(x => x.DeletedAtUtc);
        m.Entity<RecycleBinItem>().HasIndex(x => new { x.EntityType, x.EntityId });
        m.Entity<ShiftHandover>().HasIndex(x => new { x.HandoverDate, x.Status });
        m.Entity<ShiftHandover>().HasOne(x => x.FromShift).WithMany().HasForeignKey(x => x.FromShiftId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ShiftHandover>().HasOne(x => x.ToShift).WithMany().HasForeignKey(x => x.ToShiftId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ShiftHandover>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ShiftHandover>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<UserMessage>().HasIndex(x => new { x.RecipientUserId, x.AcknowledgedAtUtc, x.CreatedAtUtc });
        m.Entity<UserMessage>().HasIndex(x => new { x.SenderUserId, x.CreatedAtUtc });

        if (IsCentralMode)
        {
            foreach (var entityType in m.Model.GetEntityTypes().Where(x => !x.IsKeyless))
                m.Entity(entityType.ClrType).Property<Guid>("ConcurrencyToken").IsConcurrencyToken();
        }
    }

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
