using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer,
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer,
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyFixer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public class EntityMissingPrimaryKeyFixerTests
{
    [Fact]
    public async Task Fixer_ShouldAddIdProperty()
    {
        var test = @"
using Microsoft.EntityFrameworkCore;
namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> {|LC011:Products|} { get; set; }
    }
    public class Product
    {
        public string Name { get; set; }
    }
}
namespace Microsoft.EntityFrameworkCore { public class DbContext { } public class DbSet<T> { } }
";

        var fixedCode = @"
using Microsoft.EntityFrameworkCore;
namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> Products { get; set; }
    }
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
namespace Microsoft.EntityFrameworkCore { public class DbContext { } public class DbSet<T> { } }
";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotAddDuplicateId_WhenPrivateIdAlreadyExists()
    {
        var test = @"
using Microsoft.EntityFrameworkCore;
namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> {|LC011:Products|} { get; set; }
    }

    public class Product
    {
        private int Id { get; set; }
        public string Name { get; set; }
    }
}
namespace Microsoft.EntityFrameworkCore { public class DbContext { } public class DbSet<T> { } }
";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task FixAll_AddsIdPropertiesToMultipleEntities()
    {
        var test = @"
using Microsoft.EntityFrameworkCore;
namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> {|#0:Products|} { get; set; }
        public DbSet<Order> {|#1:Orders|} { get; set; }
    }

    public class Product
    {
        public string Name { get; set; }
    }

    public class Order
    {
        public string OrderNumber { get; set; }
    }
}
namespace Microsoft.EntityFrameworkCore { public class DbContext { } public class DbSet<T> { } }
";

        var fixedCode = @"
using Microsoft.EntityFrameworkCore;
namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
    }
}
namespace Microsoft.EntityFrameworkCore { public class DbContext { } public class DbSet<T> { } }
";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "AddIdProperty"
        };

        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC011")
                .WithLocation(0)
                .WithArguments("Product"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC011")
                .WithLocation(1)
                .WithArguments("Order"));

        await testObj.RunAsync();
    }
}
