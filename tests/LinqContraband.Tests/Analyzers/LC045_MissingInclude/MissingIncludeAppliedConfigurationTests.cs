using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_AppliedConfigurationAutoInclude_NoDiagnostic()
    {
        var test = CreateAppliedConfigurationTest(
            "builder.Navigation(o => o.Customer).AutoInclude();",
            "Console.WriteLine(order.Customer.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_UnrelatedEfBuilderCallsBeforeAutoInclude_NoDiagnostic()
    {
        var test = CreateAppliedConfigurationTest(
            @"builder.HasKey(o => o.Id);
        builder.Navigation(o => o.Customer).AutoInclude();",
            "Console.WriteLine(order.Customer.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_IgnoreAfterAutoInclude_DoesNotSuppress()
    {
        var test = CreateAppliedConfigurationTest(
            @"builder.Navigation(o => o.Customer).AutoInclude();
        builder.Ignore(o => o.Customer);",
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestInnocent_RelationalBuilderExtensionBeforeAutoInclude_NoDiagnostic()
    {
        var test = CreateAppliedConfigurationTest(
            @"builder.ToTable(""Orders"");
        builder.Navigation(o => o.Customer).AutoInclude();",
            "Console.WriteLine(order.Customer.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_AppliedConfigurationDifferentNavigation_DoesNotSuppress()
    {
        var test = CreateAppliedConfigurationTest(
            "builder.Navigation(o => o.BillingCustomer).AutoInclude();",
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_AppliedConfigurationDisabledAutoInclude_DoesNotSuppress()
    {
        var test = CreateAppliedConfigurationTest(
            "builder.Navigation(o => o.Customer).AutoInclude(false);",
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_AppliedConfigurationRuntimeAutoInclude_DoesNotSuppress()
    {
        var test = CreateAppliedConfigurationTest(
            "builder.Navigation(o => o.Customer).AutoInclude(DateTime.UtcNow.Ticks > 0);",
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_AppliedConfigurationConditionalAutoInclude_DoesNotSuppress()
    {
        var test = CreateAppliedConfigurationTest(
            @"if (DateTime.UtcNow.Ticks > 0)
        {
            builder.Navigation(o => o.Customer).AutoInclude();
        }",
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_AppliedConfigurationHelperBoundary_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    private static void ConfigureNavigation(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude(false);
    }

    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude();
        if (DateTime.UtcNow.Ticks > 0)
        {
            ConfigureNavigation(builder);
        }
    }
}

class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
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
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_SourceDefinedEfNamespaceHelper_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
namespace Microsoft.EntityFrameworkCore
{
    public static class CustomBuilderExtensions
    {
        public static Metadata.Builders.EntityTypeBuilder<Order> ConfigureAutoInclude(
            this Metadata.Builders.EntityTypeBuilder<Order> builder)
        {
            builder.Navigation(o => o.Customer).AutoInclude(false);
            return builder;
        }
    }
}

class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude();
        builder.ConfigureAutoInclude();
    }
}

class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
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
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_LaterAppliedConfigurationDisableWins()
    {
        var test =
            Usings
            + @"
class EnableOrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude();
    }
}

class DisableOrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude(false);
    }
}

class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EnableOrderConfiguration());
        modelBuilder.ApplyConfiguration(new DisableOrderConfiguration());
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
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_ConditionalAppliedConfigurationDisableInvalidatesEarlierEnable()
    {
        var test =
            Usings
            + @"
class DisableOrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(o => o.Customer).AutoInclude(false);
    }
}

class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        if (DateTime.UtcNow.Ticks > 0)
        {
            modelBuilder.ApplyConfiguration(new DisableOrderConfiguration());
        }
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
            Console.WriteLine({|#0:order.Customer|}.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestInnocent_LaterFluentAutoIncludeEnableWins()
    {
        var test = CreateAppliedConfigurationTest(
            "builder.Navigation(o => o.Customer).AutoInclude(false).AutoInclude();",
            "Console.WriteLine(order.Customer.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LaterFluentAutoIncludeDisableWins()
    {
        var test = CreateAppliedConfigurationTest(
            "builder.Navigation(o => o.Customer).AutoInclude().AutoInclude(false);",
            "Console.WriteLine({|#0:order.Customer|}.Name);"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    [Fact]
    public async Task TestCrime_IgnoreAutoIncludesOverridesAppliedConfiguration()
    {
        var test = CreateAppliedConfigurationTest(
            "builder.Navigation(o => o.Customer).AutoInclude();",
            "Console.WriteLine({|#0:order.Customer|}.Name);",
            "db.Orders.IgnoreAutoIncludes().ToList()"
        );

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Customer", "Order"));
    }

    private static string CreateAppliedConfigurationTest(
        string configurationBody,
        string access,
        string query = "db.Orders.ToList()"
    )
    {
        return Usings
            + @"
class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        "
            + configurationBody
            + @"
    }
}

class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
    }
}

class Program
{
    void Main()
    {
        var db = new AutoIncludeDbContext();
        var orders = "
            + query
            + @";
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
