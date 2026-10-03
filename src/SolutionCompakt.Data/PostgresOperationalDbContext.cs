using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SolutionCompakt.Data;

/// <summary>Operational model without desktop state; each instance is bound to one company.</summary>
public sealed class PostgresOperationalDbContext : OperationalDbContext
{
    public Guid CompanyId { get; }
    public string SchemaName => "company_" + CompanyId.ToString("N");

    public PostgresOperationalDbContext(DbContextOptions<PostgresOperationalDbContext> options, Guid companyId) : base(options)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company ID is required.", nameof(companyId));
        CompanyId = companyId;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, CompanyModelCacheKeyFactory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        OperationalModel.Configure(modelBuilder, central: true, SchemaName);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareVersions()
    {
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
            entry.Property("ConcurrencyToken").CurrentValue = Guid.NewGuid();
    }
}

public sealed class CompanyModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context) => Create(context, false);
    public object Create(DbContext context, bool designTime) => context is PostgresOperationalDbContext operational
        ? (context.GetType(), operational.CompanyId, designTime)
        : (context.GetType(), designTime);
}
