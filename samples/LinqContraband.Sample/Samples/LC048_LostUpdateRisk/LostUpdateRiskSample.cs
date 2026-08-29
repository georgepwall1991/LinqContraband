using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC048_LostUpdateRisk;

public static class LostUpdateRiskSample
{
    public static void DemonstrateViolation(LostUpdateDbContext db)
    {
        var order = db.Orders.Single(candidate => candidate.Id == 1);

        // VIOLATION: the replacement is derived from tracked state and can overwrite a concurrent update.
        order.Quantity += 2;
        db.SaveChanges();
    }

    public static void DemonstrateConcurrencyProtection(LostUpdateDbContext db)
    {
        var protectedOrder = db.ConcurrencyProtectedOrders.Single(candidate => candidate.Id == 1);

        // CORRECT: Quantity is an application-managed concurrency token, so its original value
        // participates in the update predicate and a competing update causes DbUpdateConcurrencyException.
        protectedOrder.Quantity += 2;

        db.SaveChanges();
    }
}

public sealed class LostUpdateOrder
{
    public int Id { get; set; }
    public int Quantity { get; set; }
}

public sealed class ConcurrencyProtectedOrder
{
    public int Id { get; set; }
    public int Quantity { get; set; }
}

public sealed class LostUpdateDbContext : DbContext
{
    public DbSet<LostUpdateOrder> Orders { get; set; } = null!;
    public DbSet<ConcurrencyProtectedOrder> ConcurrencyProtectedOrders { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("LC048");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<ConcurrencyProtectedOrder>()
            .Property(order => order.Quantity)
            .IsConcurrencyToken();
    }
}
