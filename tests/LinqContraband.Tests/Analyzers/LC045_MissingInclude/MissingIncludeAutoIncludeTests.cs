using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC045_MissingInclude.MissingIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

public partial class MissingIncludeEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_ContextAutoIncludeCoversAccess_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
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
            Console.WriteLine(order.Customer.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_IgnoreAutoIncludesRestoresDiagnostic()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
    }
}

class Program
{
    void Main()
    {
        var db = new AutoIncludeDbContext();
        var orders = db.Orders.IgnoreAutoIncludes().ToList();
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
    public async Task TestCrime_AutoIncludeOnDifferentContext_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class OtherDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
    }
}

class Program
{
    void Main()
    {
        var db = new MyDbContext();
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
    public async Task TestCrime_AutoIncludeOnDifferentNavigation_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
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
            Console.WriteLine({|#0:order.Items|}.Count);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, Diagnostic(0, "Items", "Order"));
    }

    [Fact]
    public async Task TestCrime_AutoIncludeDisabledAfterEnable_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude(false);
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
    public async Task TestCrime_ConditionalAutoInclude_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (DateTime.UtcNow.Ticks > 0)
        {
            modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
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
    public async Task TestInnocent_DirectForeachHonorsContextAutoInclude_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
    }
}

class Program
{
    void Main()
    {
        var db = new AutoIncludeDbContext();
        foreach (var order in db.Orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_HiddenOnModelCreatingLookalike_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class HiddenConfigurationDbContext : MyDbContext
{
    protected new void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
    }
}

class Program
{
    void Main()
    {
        var db = new HiddenConfigurationDbContext();
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
    public async Task TestCrime_AutoIncludeAfterEarlyReturn_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (DateTime.UtcNow.Ticks > 0)
        {
            return;
        }

        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
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
    public async Task TestCrime_ConditionalAutoIncludeDisable_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        if (DateTime.UtcNow.Ticks > 0)
        {
            modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude(false);
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
    public async Task TestCrime_RuntimeAutoIncludeSettingAfterEnable_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude(DateTime.UtcNow.Ticks > 0);
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
    public async Task TestCrime_OverrideOfHiddenOnModelCreatingSlot_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class HiddenConfigurationBase : MyDbContext
{
    protected new virtual void OnModelCreating(ModelBuilder modelBuilder) { }
}

class HiddenConfigurationDbContext : HiddenConfigurationBase
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
    }
}

class Program
{
    void Main()
    {
        var db = new HiddenConfigurationDbContext();
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
    public async Task TestCrime_BaseConfigurationAfterEnable_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class BaseConfigurationDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude(false);
    }
}

class AutoIncludeDbContext : BaseConfigurationDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        base.OnModelCreating(modelBuilder);
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
    public async Task TestCrime_HelperConfigurationAfterEnable_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    private static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        Configure((ModelBuilder)modelBuilder);
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
    public async Task TestCrime_AliasedBuilderHelperAfterEnable_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    private static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        var builder = modelBuilder;
        Configure(builder);
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
    public async Task TestCrime_MultiplyAssignedBuilderAliasAfterEnable_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    private static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
        var builder = modelBuilder;
        builder = modelBuilder;
        Configure(builder);
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
    public async Task TestCrime_FluentAutoIncludeDisable_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude().AutoInclude(false);
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
    public async Task TestCrime_DeferredAutoInclude_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    private static void Defer(Action configuration) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Defer(() => modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude());
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
    public async Task TestCrime_ConditionalExpressionAutoInclude_DoesNotSuppress()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = DateTime.UtcNow.Ticks > 0
            ? modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude()
            : null;
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
    public async Task TestInnocent_ConstructedGenericContextAutoInclude_NoDiagnostic()
    {
        var test =
            Usings
            + @"
class AutoIncludeDbContext<TMarker> : MyDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
    }
}

class Program
{
    void Main()
    {
        var db = new AutoIncludeDbContext<int>();
        var orders = db.Orders.ToList();
        foreach (var order in orders)
        {
            Console.WriteLine(order.Customer.Name);
        }
    }
}
"
            + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
