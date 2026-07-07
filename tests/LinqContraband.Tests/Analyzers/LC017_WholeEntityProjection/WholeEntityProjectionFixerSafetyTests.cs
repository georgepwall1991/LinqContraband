using System.Threading.Tasks;
using Xunit;
using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
        LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionAnalyzer,
        LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionFixer>;

namespace LinqContraband.Tests.Analyzers.LC017_WholeEntityProjection;

public partial class WholeEntityProjectionFixerTests
{
    [Fact]
    public async Task IndexedEntityEscape_HasNoFix()
    {
        var test = CommonUsings + MockEfCore + LargeEntity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = db.LargeEntities.ToList();
        Console.WriteLine(entities[0].Name);
        ProcessEntity(entities[0]);
    }

    private void ProcessEntity(LargeEntity entity) { }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(46, 24).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task CastAccessedProperty_HasNoFix()
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
            Use(e.Id);
            Use(((LargeEntity)e).Name);
        }
    }

    private static void Use<T>(T value) { }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(46, 24).WithArguments("LargeEntity", "2", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task InterfaceCastAccessedProperty_HasNoFix()
    {
        var entity = @"
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

        var test = CommonUsings + MockEfCore + entity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = {|#0:db.LargeEntities.ToList()|};
        foreach (var e in entities)
        {
            Use(e.Id);
            Use(((IHasName)e).Name);
        }
    }

    private static void Use<T>(T value) { }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(0).WithArguments("LargeEntity", "2", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task ConditionalInterfaceCastAccessedProperty_HasNoFix()
    {
        var entity = @"
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

        var test = CommonUsings + MockEfCore + entity + @"
class Program
{
    public void Process()
    {
        var db = new AppDbContext();
        var entities = {|#0:db.LargeEntities.ToList()|};
        foreach (var e in entities)
        {
            Use(e.Id);
            Use(((IHasName)e)?.Name);
        }
    }

    private static void Use<T>(T value) { }
}";

        var expected = VerifyCS.Diagnostic("LC017").WithLocation(0).WithArguments("LargeEntity", "1", "12");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
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
