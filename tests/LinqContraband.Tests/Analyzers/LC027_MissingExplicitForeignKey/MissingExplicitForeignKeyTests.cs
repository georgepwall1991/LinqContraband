using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC027_MissingExplicitForeignKey.MissingExplicitForeignKeyAnalyzer>;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC027_MissingExplicitForeignKey.MissingExplicitForeignKeyAnalyzer,
    LinqContraband.Analyzers.LC027_MissingExplicitForeignKey.MissingExplicitForeignKeyFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC027_MissingExplicitForeignKey;

public class MissingExplicitForeignKeyTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public class DbContext
    {
    }
}

namespace System.ComponentModel.DataAnnotations.Schema
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class ForeignKeyAttribute : Attribute
    {
        public ForeignKeyAttribute(string name) { Name = name; }
        public string Name { get; }
    }
}
";

    [Fact]
    public async Task Navigation_WithoutFK_ShouldTriggerLC027()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer {|LC027:Customer|} { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithConventionalFK_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public Customer Customer { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CollectionNavigation_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Customer
    {
        public int Id { get; set; }
        public ICollection<Order> Orders { get; set; }
    }

    public class Order { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Order> Orders { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithForeignKeyAttribute_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        [ForeignKey(""Customer"")]
        public int CustId { get; set; }
        public Customer Customer { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_NonEntityType_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Address ShippingAddress { get; set; }
    }

    public class Address { public string Street { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldAddForeignKeyProperty()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer {|LC027:Customer|} { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
    }
}";

        var fixedCode = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public Customer Customer { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
    }
}";

        await new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipLocalDiagnosticCheck
        }.RunAsync();
    }
}
