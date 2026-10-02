using Microsoft.EntityFrameworkCore;
using SolutionCompakt.Server.Security;

namespace SolutionCompakt.Server.Data;

public sealed class CentralDbContext(
    DbContextOptions<CentralDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<CompanyTenant> Companies => Set<CompanyTenant>();
    public DbSet<CentralUser> Users => Set<CentralUser>();
    public DbSet<CentralChangeEvent> ChangeEvents => Set<CentralChangeEvent>();

    private Guid? CurrentCompanyId => tenantContext.CompanyIdOrNull;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var company = modelBuilder.Entity<CompanyTenant>();
        company.ToTable("Companies");
        company.HasKey(x => x.Id);
        company.HasIndex(x => x.CompanyCode).IsUnique();
        company.Property(x => x.CompanyCode).HasMaxLength(24);
        company.Property(x => x.Name).HasMaxLength(200);
        company.Property(x => x.ConcurrencyToken).IsConcurrencyToken();

        var user = modelBuilder.Entity<CentralUser>();
        user.ToTable("Users");
        user.HasKey(x => x.Id);
        user.HasIndex(x => new { x.CompanyId, x.SourceUserId }).IsUnique();
        user.HasIndex(x => new { x.CompanyId, x.UsernameNormalized }).IsUnique();
        user.Property(x => x.Username).HasMaxLength(100);
        user.Property(x => x.UsernameNormalized).HasMaxLength(100);
        user.Property(x => x.DisplayName).HasMaxLength(160);
        user.Property(x => x.Role).HasMaxLength(32);
        user.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        user.HasOne<CompanyTenant>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        user.HasQueryFilter(x => CurrentCompanyId.HasValue && x.CompanyId == CurrentCompanyId.Value);

        var change = modelBuilder.Entity<CentralChangeEvent>();
        change.ToTable("ChangeEvents");
        change.HasKey(x => x.Id);
        change.HasIndex(x => new { x.CompanyId, x.Id });
        change.Property(x => x.EntityType).HasMaxLength(120);
        change.Property(x => x.EntityId).HasMaxLength(120);
        change.Property(x => x.Operation).HasMaxLength(32);
        change.Property(x => x.PayloadJson).HasColumnType("jsonb");
        change.HasOne<CompanyTenant>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        change.HasQueryFilter(x => CurrentCompanyId.HasValue && x.CompanyId == CurrentCompanyId.Value);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareTenantChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareTenantChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareTenantChanges()
    {
        var companyId = CurrentCompanyId;
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ITenantEntity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (!companyId.HasValue)
                throw new InvalidOperationException("Tenant-scoped writes require an authenticated company context.");

            if (entry.State == EntityState.Added && entry.Entity.CompanyId == Guid.Empty)
                entry.Entity.CompanyId = companyId.Value;

            if (entry.Entity.CompanyId != companyId.Value)
                throw new InvalidOperationException("Cross-company writes are not allowed.");
        }

        foreach (var entry in ChangeTracker.Entries<CompanyTenant>().Where(x => x.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAtUtc = now;
            entry.Entity.ConcurrencyToken = Guid.NewGuid();
        }

        foreach (var entry in ChangeTracker.Entries<CentralUser>().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.UpdatedAtUtc = now;
            entry.Entity.ConcurrencyToken = Guid.NewGuid();
            entry.Entity.Username = entry.Entity.Username.Trim();
            entry.Entity.UsernameNormalized = entry.Entity.Username.Trim().ToLowerInvariant();
            entry.Entity.DisplayName = entry.Entity.DisplayName.Trim();
        }
    }
}
