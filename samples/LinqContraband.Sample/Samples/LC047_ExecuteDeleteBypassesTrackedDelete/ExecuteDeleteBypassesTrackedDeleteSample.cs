using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC047_ExecuteDeleteBypassesTrackedDelete;

public static class ExecuteDeleteBypassesTrackedDeleteSample
{
    public static void Run()
    {
        Console.WriteLine("Testing LC047...");
        using var db = new SoftDeleteDbContext();
        var cutoff = DateTime.UtcNow.AddYears(-1);

        // VIOLATION: ExecuteDelete issues a SQL DELETE and skips SaveChanges soft-delete conversion.
        db.Users.Where(user => user.LastLogin < cutoff).ExecuteDelete();

        // CORRECT: keep set-based performance with the converted property.
        db.Users
            .Where(user => user.LastLogin < cutoff)
            .ExecuteUpdate(setters => setters.SetProperty(user => user.IsDeleted, true));
    }
}

public sealed class SoftDeleteUser
{
    public int Id { get; set; }
    public DateTime LastLogin { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>
/// Dedicated context so the shared sample <c>AppDbContext</c> does not grow a delete conversion
/// that would suppress LC012/LC035 demonstrations.
/// </summary>
public sealed class SoftDeleteDbContext : DbContext
{
    public DbSet<SoftDeleteUser> Users { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("LC047");
    }

    public override int SaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                ((SoftDeleteUser)entry.Entity).IsDeleted = true;
            }
        }

        return base.SaveChanges();
    }
}
