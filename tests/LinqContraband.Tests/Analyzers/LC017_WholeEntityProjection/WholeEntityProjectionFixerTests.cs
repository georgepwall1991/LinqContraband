using System.Threading.Tasks;
using LinqContraband.Analyzers.LC017_WholeEntityProjection;
using Xunit;
using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
        LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionAnalyzer,
        LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionFixer>;

namespace LinqContraband.Tests.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Tests for the LC017 WholeEntityProjection code fixer.
/// </summary>
public class WholeEntityProjectionFixerTests
{
    private const string CommonUsings = @"
using System;
using System.Collections.Generic;
using System.Linq;
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
";

    // Large entity with 12 properties for testing
    private const string LargeEntity = @"
class LargeEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Address { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}
class AppDbContext : DbContext { public DbSet<LargeEntity> LargeEntities { get; set; } }
";

    /// <summary>
    /// Tests that the fixer adds .Select() with single property projection.
    /// </summary>
    [Fact]
    public async Task SinglePropertyAccess_AddsSelectProjection()
    {
        var test = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}";

        // Uses anonymous type so downstream code (e.Name) still compiles
        var fixedCode = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.Select(e => new { e.Name }).ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(46, 24).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    /// <summary>
    /// Tests that the fixer adds .Select() with multiple property projection.
    /// </summary>
    [Fact]
    public async Task TwoPropertiesAccessed_AddsSelectProjectionWithBothProperties()
    {
        var test = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Id);
            Console.WriteLine(e.Name);
        }
    }
}";

        var fixedCode = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.Select(e => new { e.Id, e.Name }).ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Id);
            Console.WriteLine(e.Name);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(46, 24).WithArguments("LargeEntity", "2", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    /// <summary>
    /// Tests that the fixer works with ToArray() as well.
    /// </summary>
    [Fact]
    public async Task ToArray_AddsSelectProjection()
    {
        var test = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.ToArray();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}";

        // Uses anonymous type so downstream code (e.Name) still compiles
        var fixedCode = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.Select(e => new { e.Name }).ToArray();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(46, 24).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    /// <summary>
    /// Tests that the fixer works when there's a Where clause in the chain.
    /// </summary>
    [Fact]
    public async Task WithWhereClause_AddsSelectBeforeToList()
    {
        var test = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.Where(x => x.Price > 100).ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}";

        // Uses anonymous type so downstream code (e.Name) still compiles
        var fixedCode = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.Where(x => x.Price > 100).Select(e => new { e.Name }).ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(46, 24).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    /// <summary>
    /// Tests that properties are sorted alphabetically in the projection.
    /// </summary>
    [Fact]
    public async Task PropertiesAreSortedAlphabetically()
    {
        var test = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);  // Accessed second but comes first alphabetically
            Console.WriteLine(e.Email); // Accessed first but comes second alphabetically
        }
    }
}";

        // Properties should be sorted: Email, Name
        var fixedCode = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.Select(e => new { e.Email, e.Name }).ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);  // Accessed second but comes first alphabetically
            Console.WriteLine(e.Email); // Accessed first but comes second alphabetically
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(46, 24).WithArguments("LargeEntity", "2", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ExplicitlyTypedCollection_HasNoFix()
    {
        var test = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        List<LargeEntity> entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(46, 38).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

}
