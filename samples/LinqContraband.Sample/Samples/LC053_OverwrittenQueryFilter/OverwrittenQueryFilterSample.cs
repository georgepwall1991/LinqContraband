using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LinqContraband.Sample.Samples.LC053_OverwrittenQueryFilter;

/// <summary>
///     Demonstrates a global query filter silently replaced by a second one (LC053).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling <c>HasQueryFilter</c> twice for one entity type, often a tenant filter in
///         one place and a soft-delete filter in another.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> EF Core keeps only the last unnamed filter. The first one stops applying
///         with no error, so queries can return other tenants' rows.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Combine the conditions with <c>&amp;&amp;</c> in one filter, or give each filter
///         a name (EF Core 10+).
///     </para>
/// </remarks>
public static class OverwrittenQueryFilterSample
{
    private static readonly string TenantCountry = "UK";

    public static void Run()
    {
        Console.WriteLine("Testing LC053 (design-time check, see Configure)...");
    }

    public static void Configure(ModelBuilder modelBuilder)
    {
        // VIOLATION: the second call replaces the first, so the country filter is dropped.
        modelBuilder.Entity<LargeEntity>().HasQueryFilter(e => e.Country == TenantCountry);
        modelBuilder.Entity<LargeEntity>().HasQueryFilter(e => e.Quantity > 0);

        // CORRECT: one filter with both conditions.
        modelBuilder.Entity<Product>().HasQueryFilter(p => p.Sku != "" && p.Name != "");
    }
}

// VIOLATION: two configuration classes each set an unnamed filter for Customer; only one survives.
public sealed class CustomerTenantConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder) => builder.HasQueryFilter(c => c.Name != "");
}

public sealed class CustomerVisibleConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder) => builder.HasQueryFilter(c => c.Id > 0);
}
