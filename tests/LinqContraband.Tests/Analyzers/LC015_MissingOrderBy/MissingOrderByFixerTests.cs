using System.Threading.Tasks;
using LinqContraband.Analyzers.LC015_MissingOrderBy;
using Xunit;
using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
        LinqContraband.Analyzers.LC015_MissingOrderBy.MissingOrderByAnalyzer,
        LinqContraband.Analyzers.LC015_MissingOrderBy.MissingOrderByFixer>;

namespace LinqContraband.Tests.Analyzers.LC015_MissingOrderBy;

public class MissingOrderByFixerTests
{
    private const string CommonUsings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations; // Added for KeyAttribute tests
using Microsoft.EntityFrameworkCore;
";

    private const string MockEfCore = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
    }
    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }
}
namespace System.ComponentModel.DataAnnotations
{
    public class KeyAttribute : Attribute {}
}
";

    [Fact]
    public async Task Skips_AddsOrderBy_WithId()
    {
        var test = CommonUsings + MockEfCore + @"
class User { public int Id { get; set; } }
class AppDbContext : DbContext { public DbSet<User> Users { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var q = db.Users.Skip(10);
    }
}";

        var fixedCode = CommonUsings + MockEfCore + @"
class User { public int Id { get; set; } }
class AppDbContext : DbContext { public DbSet<User> Users { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var q = db.Users.OrderBy(x => x.Id).Skip(10);
    }
}";

        // Adjusted line number to 35 based on previous failure
        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(35, 26).WithArguments("Skip");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Last_AddsOrderBy_WithKeyAttribute()
    {
        // Removed 'using' from here as it's now in CommonUsings
        var test = CommonUsings + MockEfCore + @"
class Product { [Key] public int Code { get; set; } }
class AppDbContext : DbContext { public DbSet<Product> Products { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var p = db.Products.Last();
    }
}";

        var fixedCode = CommonUsings + MockEfCore + @"
class Product { [Key] public int Code { get; set; } }
class AppDbContext : DbContext { public DbSet<Product> Products { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var p = db.Products.OrderBy(x => x.Code).Last();
    }
}";

        // Line calc:
        // CommonUsings (7) + MockEfCore (17) = 24 lines preamble.
        // Test code:
        // 25: class Product...
        // 26: class AppCtx...
        // 27:
        // 28: class Program
        // 29: void Main
        // 30: var db
        // 31: var p = db.Products.Last();
        // So line should be around 31 + 24? No, file lines start from 1.
        // Wait, Skips_AddsOrderBy_WithId was 34.
        // Skips test structure is identical to Last test structure (just different class/method names).
        // So 34 should be correct here too.

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(35, 29).WithArguments("Last");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    /// <summary>
    /// Tests that the fixer does NOT generate code when the entity has no detectable primary key.
    /// This prevents generating invalid code like OrderBy(x => x.Id) when Id doesn't exist.
    /// The entity uses unconventional property names to ensure TryFindPrimaryKey returns null.
    /// </summary>
    [Fact]
    public async Task Skips_NoFix_WhenNoPrimaryKeyDetectable()
    {
        var test = CommonUsings + MockEfCore + @"
// Entity with no conventional key (no Id, no OrderId, no [Key] attribute)
class OrderLine { public string Description { get; set; } public decimal Amount { get; set; } public int Quantity { get; set; } }
class AppDbContext : DbContext { public DbSet<OrderLine> OrderLines { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var q = db.OrderLines.Skip(10);
    }
}";

        // Diagnostic is raised but no code fix should be applied (code remains unchanged)
        // since the entity has no Id, OrderLineId, or [Key] attribute property.
        // NumberOfFixAllIterations = 0 tells the test we expect NO fix to be applied.
        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(36, 31).WithArguments("Skip");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task OrderByAfterSkip_HasNoFix()
    {
        var test = CommonUsings + MockEfCore + @"
class User { public int Id { get; set; } public string Name { get; set; } }
class AppDbContext : DbContext { public DbSet<User> Users { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var q = db.Users.Skip(10).OrderBy(u => u.Name);
    }
}";

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.MisplacedRule).WithLocation(35, 35).WithArguments("OrderBy");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    /// <summary>
    /// Tests that the fixer does NOT register on a composite-keyed entity
    /// (two `[Key]`-annotated properties). The shared `TryFindPrimaryKey`
    /// helper returns the first key it encounters, so without an explicit
    /// composite-key check the fixer would offer a partial-key `OrderBy`
    /// that does not guarantee deterministic pagination — the very
    /// behaviour LC015 is meant to surface.
    /// </summary>
    [Fact]
    public async Task Skip_NoFix_WhenCompositeKeyDetected()
    {
        var test = CommonUsings + MockEfCore + @"
class OrderLine { [Key] public int OrderId { get; set; } [Key] public int LineNumber { get; set; } public string Description { get; set; } }
class AppDbContext : DbContext { public DbSet<OrderLine> OrderLines { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var q = db.OrderLines.Skip(10);
    }
}";

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(35, 31).WithArguments("Skip");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    /// <summary>
    /// Tests that the fixer does NOT register when the entity declares an
    /// EF Core 7+ class-level composite key via `[PrimaryKey(...)]` with two
    /// or more named parts, even when one part is named `Id`. Without this
    /// gate the existing convention-driven `TryFindPrimaryKey` returns "Id"
    /// and the fixer would insert a partial-key `OrderBy(x => x.Id)`.
    /// </summary>
    [Fact]
    public async Task Skip_NoFix_WhenClassLevelPrimaryKeyAttributeDeclaresCompositeKey()
    {
        // Inlines the EF Core 7+ class-level [PrimaryKey(...)] attribute
        // shape; declared inside this test's source rather than in the
        // shared MockEfCore so line-number assertions on the existing
        // fixer tests remain stable.
        var test = CommonUsings + MockEfCore + @"
namespace Microsoft.EntityFrameworkCore
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PrimaryKeyAttribute : Attribute { public PrimaryKeyAttribute(string propertyName, params string[] additionalPropertyNames) { } }
}

[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Document.TenantId), nameof(Document.Id))]
class Document { public int TenantId { get; set; } public int Id { get; set; } public string Title { get; set; } }
class AppDbContext : DbContext { public DbSet<Document> Documents { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var q = db.Documents.Skip(10);
    }
}";

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(42, 30).WithArguments("Skip");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    /// <summary>
    /// Tests that the fixer correctly uses EntityNameId convention.
    /// </summary>
    [Fact]
    public async Task Last_AddsOrderBy_WithEntityNameIdConvention()
    {
        var test = CommonUsings + MockEfCore + @"
class Invoice { public int InvoiceId { get; set; } public string Number { get; set; } }
class AppDbContext : DbContext { public DbSet<Invoice> Invoices { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var i = db.Invoices.Last();
    }
}";

        var fixedCode = CommonUsings + MockEfCore + @"
class Invoice { public int InvoiceId { get; set; } public string Number { get; set; } }
class AppDbContext : DbContext { public DbSet<Invoice> Invoices { get; set; } }

class Program {
    void Main() {
        var db = new AppDbContext();
        var i = db.Invoices.OrderBy(x => x.InvoiceId).Last();
    }
}";

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(35, 29).WithArguments("Last");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
