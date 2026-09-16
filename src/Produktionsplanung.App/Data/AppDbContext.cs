using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Data;

public class AppDbContext : DbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();
    public DbSet<Workstation> Workstations => Set<Workstation>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<OperatingCalendarDay> OperatingCalendarDays => Set<OperatingCalendarDay>();
    public DbSet<WorkTimeEntry> WorkTimeEntries => Set<WorkTimeEntry>();
    public DbSet<PlanningAssignment> PlanningAssignments => Set<PlanningAssignment>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionActual> ProductionActuals => Set<ProductionActual>();
    public DbSet<DowntimeEntry> DowntimeEntries => Set<DowntimeEntry>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        AppPaths.EnsureDirectories();
        optionsBuilder.UseSqlite($"Data Source={AppPaths.DatabasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>().HasIndex(x => x.PersonnelNumber).IsUnique();
        modelBuilder.Entity<EmployeeQualification>().HasKey(x => new { x.EmployeeId, x.QualificationId });
        modelBuilder.Entity<EmployeeQualification>().HasOne(x => x.Employee).WithMany(x => x.Qualifications).HasForeignKey(x => x.EmployeeId);
        modelBuilder.Entity<EmployeeQualification>().HasOne(x => x.Qualification).WithMany(x => x.Employees).HasForeignKey(x => x.QualificationId);

        modelBuilder.Entity<Workstation>()
            .HasOne(x => x.RequiredQualification)
            .WithMany()
            .HasForeignKey(x => x.RequiredQualificationId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<OperatingCalendarDay>().HasIndex(x => x.Date).IsUnique();
        modelBuilder.Entity<WorkTimeEntry>().HasIndex(x => new { x.EmployeeId, x.Date });
        modelBuilder.Entity<WorkTimeEntry>().HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProductionOrder>().HasIndex(x => x.OrderNumber).IsUnique();
        modelBuilder.Entity<ProductionOrder>().HasIndex(x => new { x.PlannedDate, x.WorkstationId, x.ShiftId });
        modelBuilder.Entity<ProductionOrder>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionOrder>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ProductionActual>().HasIndex(x => new { x.ProductionOrderId, x.Date });
        modelBuilder.Entity<ProductionActual>().HasOne(x => x.ProductionOrder).WithMany(x => x.Actuals).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<DowntimeEntry>().HasIndex(x => x.ProductionActualId);
        modelBuilder.Entity<DowntimeEntry>().HasOne(x => x.ProductionActual).WithMany(x => x.Downtimes).HasForeignKey(x => x.ProductionActualId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserAccount>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<AuditLog>().HasIndex(x => x.TimestampUtc);
        modelBuilder.Entity<AuditLog>().HasIndex(x => x.Username);
    }

    public override int SaveChanges()
    {
        var snapshots = CaptureAuditSnapshots();
        var result = base.SaveChanges();
        WriteAuditSnapshots(snapshots);
        return result;
    }

    private List<AuditSnapshot> CaptureAuditSnapshots()
    {
        if (!SessionService.IsAuthenticated)
            return new List<AuditSnapshot>();

        var snapshots = new List<AuditSnapshot>();
        foreach (var entry in ChangeTracker.Entries().Where(x =>
                     x.Entity is not AuditLog &&
                     x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var modified = entry.State == EntityState.Modified
                ? entry.Properties
                    .Where(x => x.IsModified && x.Metadata.Name is not nameof(UserAccount.PasswordHash) and not nameof(UserAccount.PasswordSalt))
                    .Select(x => x.Metadata.Name)
                    .ToArray()
                : Array.Empty<string>();

            snapshots.Add(new AuditSnapshot(
                entry,
                entry.State,
                GetEntityId(entry),
                modified.Length == 0 ? null : $"Geänderte Felder: {string.Join(", ", modified)}"));
        }
        return snapshots;
    }

    private void WriteAuditSnapshots(List<AuditSnapshot> snapshots)
    {
        if (snapshots.Count == 0 || SessionService.CurrentUser is null)
            return;

        foreach (var snapshot in snapshots)
        {
            var entityId = snapshot.State == EntityState.Added ? GetEntityId(snapshot.Entry) : snapshot.EntityIdBefore;
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
                EntityId = entityId,
                Details = snapshot.Details
            });
        }

        base.SaveChanges();
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
