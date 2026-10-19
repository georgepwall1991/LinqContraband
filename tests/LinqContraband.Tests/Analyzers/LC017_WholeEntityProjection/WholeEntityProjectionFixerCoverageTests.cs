using System.Threading.Tasks;
using Xunit;
using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
        LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionAnalyzer,
        LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionFixer>;

namespace LinqContraband.Tests.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Leftover LC017 mixed-access fixer arms the original fixer fixtures do not isolate:
/// <c>as</c> conversions (the existing pins use casts), extra-parenthesized <c>as</c>,
/// indexed <c>?.</c> property access, and a property declared on a base type.
/// Dropping the <c>AsExpression</c> arm fails only the two <c>as</c> pins;
/// <c>ConditionalInterfaceCastAccessedProperty_HasNoFix</c> stays green.
/// A grammar-required parenthesis wraps every <c>as</c>/<c>cast</c> conditional receiver, so
/// dropping parenthesized conversion unwrap also fails the existing parenthesized-cast pin.
/// Extra parentheses around <c>as</c> isolate recursive unwrap: a one-level match still
/// passes <c>(e as IHasName)?.Name</c> and the cast pin.
/// Dropping the indexed conditional skip fails only the indexed <c>?.</c> include pin;
/// <c>IndexedAccessedProperty_IsIncludedInProjection</c> stays green.
/// Dropping the fixer <c>BaseType</c> walk fails only the inherited-property pin.
/// </summary>
public partial class WholeEntityProjectionFixerTests
{
    private const string InterfaceEntity = @"
interface IHasName
{
    string Name { get; }
}

class LargeEntity : IHasName
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

    [Fact]
    public async Task AsInterfaceConditionalAccessedProperty_HasNoFix()
    {
        var test = CommonUsings + MockEfCore + InterfaceEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = {|#0:db.LargeEntities.ToList()|};
        foreach (var e in entities)
        {
            Use(e.Id);
            Use((e as IHasName)?.Name);
        }
    }

    private static void Use<T>(T value) { }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(0).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task ParenthesizedAsInterfaceConditionalAccessedProperty_HasNoFix()
    {
        var test = CommonUsings + MockEfCore + InterfaceEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = {|#0:db.LargeEntities.ToList()|};
        foreach (var e in entities)
        {
            Use(e.Id);
            Use(((e as IHasName))?.Name);
        }
    }

    private static void Use<T>(T value) { }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(0).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task IndexedConditionalAccessedProperty_IsIncludedInProjection()
    {
        var test = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = {|#0:db.LargeEntities.ToList()|};
        Console.WriteLine(entities[0]?.Name);
    }
}";

        var fixedCode = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.Select(e => new { e.Name }).ToList();
        Console.WriteLine(entities[0]?.Name);
    }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(0).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task InheritedBaseProperty_IsIncludedInProjection()
    {
        var entity = @"
class EntityBase
{
    public string Name { get; set; }
}

class LargeEntity : EntityBase
{
    public int Id { get; set; }
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
    public string Sku { get; set; }
}

class AppDbContext : DbContext { public DbSet<LargeEntity> LargeEntities { get; set; } }
";

        var test = CommonUsings + MockEfCore + entity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = {|#0:db.LargeEntities.ToList()|};
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}";

        var fixedCode = CommonUsings + MockEfCore + entity + @"
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

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(0).WithArguments("LargeEntity", "1", "13");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
