using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// <c>ApplyConfigurationsFromAssembly</c> over this project's own assembly applies every
/// source-visible <c>IEntityTypeConfiguration&lt;T&gt;</c>, so an <c>AutoInclude()</c> in one of them
/// loads the navigation just as <c>ApplyConfiguration(new X())</c> does.
/// </summary>
public partial class MissingIncludeEdgeCasesTests
{
    private const string OrderAutoIncludeConfiguration = @"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude();
    }
}";

    [Theory]
    [InlineData("modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoIncludeDbContext).Assembly);")]
    [InlineData("modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderConfiguration).Assembly);")]
    [InlineData("modelBuilder.ApplyConfigurationsFromAssembly(System.Reflection.Assembly.GetExecutingAssembly());")]
    public async Task TestInnocent_AssemblyConfigurationAutoInclude_NoDiagnostic(string onModelCreating)
    {
        var test = CreateAssemblyConfigurationTest(
            OrderAutoIncludeConfiguration,
            onModelCreating,
            "Console.WriteLine(order.Customer.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NestedAssemblyConfigurationAutoInclude_NoDiagnostic()
    {
        var test = CreateAssemblyConfigurationTest(
            @"
static class Configurations
{
    public class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
        {
            builder.HasKey(o => o.Id);
            builder.Navigation(o => o.Customer).AutoInclude();
        }
    }
}",
            "modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoIncludeDbContext).Assembly);",
            "Console.WriteLine(order.Customer.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DirectAutoIncludeKeptAcrossAssemblyConfiguration_NoDiagnostic()
    {
        var test = CreateAssemblyConfigurationTest(
            @"
class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
    }
}",
            @"modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoIncludeDbContext).Assembly);",
            "Console.WriteLine(order.Customer.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Theory]
    // A predicate may filter the configuration out.
    [InlineData("modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoIncludeDbContext).Assembly, t => t.Name.StartsWith(\"Order\"));")]
    // Another assembly's configurations are not visible.
    [InlineData("modelBuilder.ApplyConfigurationsFromAssembly(typeof(string).Assembly);")]
    [InlineData("modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);")]
    // Only on some runs.
    [InlineData(@"if (DateTime.UtcNow.Ticks > 0)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoIncludeDbContext).Assembly);
        }")]
    public async Task TestCrime_UnprovenAssemblyConfiguration_DoesNotSuppress(string onModelCreating)
    {
        var test = CreateAssemblyConfigurationTest(
            OrderAutoIncludeConfiguration,
            onModelCreating,
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Theory]
    // EF skips abstract configurations and ones without a public parameterless constructor.
    [InlineData(@"
abstract class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude();
    }
}")]
    [InlineData(@"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public OrderConfiguration(string schema) { }

    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude();
    }
}")]
    // A different navigation.
    [InlineData(@"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.BillingCustomer).AutoInclude();
    }
}")]
    // Conditional.
    [InlineData(@"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        if (DateTime.UtcNow.Ticks > 0)
        {
            builder.Navigation(o => o.Customer).AutoInclude();
        }
    }
}")]
    // Any configuration in the assembly that hands the builder to unseen code makes the scan unproven.
    [InlineData(@"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude();
    }
}

class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Customer> builder)
    {
        Shared.Apply(builder);
    }
}

static class Shared
{
    public static void Apply<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> builder)
        where T : class { }
}")]
    // Two configurations changing the same entity would depend on EF's type-name ordering.
    [InlineData(@"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude();
    }
}

class LegacyOrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude(false);
    }
}")]
    public async Task TestCrime_AssemblyConfigurationNotProvingAutoInclude_DoesNotSuppress(string configurations)
    {
        var test = CreateAssemblyConfigurationTest(
            configurations,
            "modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoIncludeDbContext).Assembly);",
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_AssemblyConfigurationDisablesDirectAutoInclude_Reports()
    {
        var test = CreateAssemblyConfigurationTest(
            @"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude(false);
    }
}",
            @"modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutoIncludeDbContext).Assembly);",
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    private static string CreateAssemblyConfigurationTest(
        string configurations,
        string onModelCreating,
        string access
    )
    {
        return Usings
            + configurations
            + @"

class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        "
            + onModelCreating
            + @"
    }
}

class Program
{
    void Main()
    {
        var db = new AutoIncludeDbContext();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            "
            + access
            + @"
        }
    }
}
"
            + MockNamespace;
    }
}
