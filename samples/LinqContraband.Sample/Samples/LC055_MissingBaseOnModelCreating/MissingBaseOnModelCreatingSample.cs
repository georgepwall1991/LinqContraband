using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC055_MissingBaseOnModelCreating;

public sealed class Tenant
{
    public int Id { get; set; }
    public bool IsDeleted { get; set; }
}

// A shared base context that hides soft-deleted rows, like ASP.NET Core Identity's IdentityDbContext
// configures its own keys and indexes.
public abstract class SoftDeleteDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().HasQueryFilter(tenant => !tenant.IsDeleted);
    }
}

public sealed class TenantDbContext : SoftDeleteDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // VIOLATION: the soft-delete filter from SoftDeleteDbContext is never applied, so deleted tenants come back.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().ToTable("Tenants");
    }
}

public sealed class FilteredTenantDbContext : SoftDeleteDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // CORRECT: apply the base configuration first, then refine it.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Tenant>().ToTable("Tenants");
    }
}
