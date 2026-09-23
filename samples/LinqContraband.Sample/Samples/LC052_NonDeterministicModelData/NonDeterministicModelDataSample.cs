using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC052_NonDeterministicModelData;

/// <summary>
///     Demonstrates model data that changes on every run (LC052).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Seeding <c>HasData</c> or setting <c>HasDefaultValue</c> with
///         <c>DateTime.UtcNow</c> or <c>Guid.NewGuid()</c>.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> The value is part of the model, so every run builds a different model.
///         Migrations never settle, and since EF Core 9 <c>Migrate()</c> throws <c>PendingModelChangesWarning</c>.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Seed fixed literals, and use <c>HasDefaultValueSql</c> for database-generated
///         timestamps.
///     </para>
/// </remarks>
public static class NonDeterministicModelDataSample
{
    public static void Run()
    {
        Console.WriteLine("Testing LC052 (design-time check, see Configure)...");
    }

    public static void Configure(ModelBuilder modelBuilder)
    {
        // VIOLATION: a new timestamp on every run, in seed data and in a column default.
        modelBuilder.Entity<LargeEntity>().HasData(new LargeEntity { Id = 1, Name = "Seed", CreatedAt = DateTime.UtcNow });
        modelBuilder.Entity<LargeEntity>().Property(e => e.UpdatedAt).HasDefaultValue(DateTime.UtcNow);

        // CORRECT: a fixed seed value, and the database supplies the default timestamp.
        modelBuilder.Entity<LargeEntity>().HasData(new LargeEntity
        {
            Id = 2,
            Name = "Seed",
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        modelBuilder.Entity<LargeEntity>().Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
