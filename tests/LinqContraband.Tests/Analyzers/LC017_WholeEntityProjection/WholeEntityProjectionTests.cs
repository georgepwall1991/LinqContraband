using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC017_WholeEntityProjection;

public class WholeEntityProjectionTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    // MockNamespace includes a LargeEntity with 12 properties (exceeds 10 threshold)
    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
    }
}

namespace TestNamespace
{
    // Large entity with 12 properties (exceeds 10 threshold for conservative detection)
    public class LargeEntity
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

    // Small entity with only 3 properties (below threshold)
    public class SmallEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Active { get; set; }
    }

    public class LargeEntityDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class MyDbContext : DbContext
    {
        public DbSet<LargeEntity> LargeEntities { get; set; }
        public DbSet<SmallEntity> SmallEntities { get; set; }
    }
}";

    /// <summary>
    /// Crime: Load large entity (12 properties), only access 1 property in a loop.
    /// This is a clear case of wasted bandwidth/memory.
    /// </summary>
    [Fact]
    public async Task TestCrime_LargeEntity_OnlyOnePropertyAccessed_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void ProcessNames()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.Where(e => e.Id > 0).ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name); // Only Name accessed!
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 70) // Location of ToList()
            .WithArguments("LargeEntity", 1, 12);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Crime: Load large entity, access 2 properties - still below threshold.
    /// </summary>
    [Fact]
    public async Task TestCrime_LargeEntity_TwoPropertiesAccessed_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void ProcessData()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Id + "": "" + e.Name);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49) // Location of ToList()
            .WithArguments("LargeEntity", 2, 12);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Innocent: Already using Select() projection - no need for diagnostic.
    /// </summary>
    [Fact]
    public async Task TestInnocent_WithSelectProjection_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<LargeEntityDto> GetDtos()
    {
        var db = new MyDbContext();
        return db.LargeEntities
            .Select(e => new LargeEntityDto { Id = e.Id, Name = e.Name })
            .ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Small entity (3 properties) - not worth flagging.
    /// </summary>
    [Fact]
    public async Task TestInnocent_SmallEntity_BelowPropertyThreshold_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void ProcessSmall()
    {
        var db = new MyDbContext();
        var entities = db.SmallEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Entity is returned from method - can't track downstream usage.
    /// </summary>
    [Fact]
    public async Task TestInnocent_EntityReturned_CantTrackUsage_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<LargeEntity> GetAllEntities()
    {
        var db = new MyDbContext();
        return db.LargeEntities.ToList(); // Can't know how caller uses it
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Entity is passed to external method - can't track usage.
    /// </summary>
    [Fact]
    public async Task TestInnocent_EntityPassedToMethod_CantTrackUsage_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void ProcessEntities()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        ProcessList(entities); // Passed to method, can't track
    }

    private void ProcessList(List<LargeEntity> items) { }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Most properties are accessed (more than 50%).
    /// </summary>
    [Fact]
    public async Task TestInnocent_MostPropertiesAccessed_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void ProcessAll()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            // Accessing 7 of 12 properties (> 50%)
            Console.WriteLine(e.Id);
            Console.WriteLine(e.Name);
            Console.WriteLine(e.Description);
            Console.WriteLine(e.Email);
            Console.WriteLine(e.Phone);
            Console.WriteLine(e.Address);
            Console.WriteLine(e.City);
        }
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: First/Single materializers with single property access are often legitimate.
    /// The overhead is less significant for single entity queries.
    /// </summary>
    [Fact]
    public async Task TestInnocent_FirstOrSingle_SingleEntity_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void GetSingle()
    {
        var db = new MyDbContext();
        var entity = db.LargeEntities.First(e => e.Id == 1);
        Console.WriteLine(entity.Name);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Variable used in lambda/delegate - can't reliably track.
    /// </summary>
    [Fact]
    public async Task TestInnocent_VariableInLambda_CantTrack_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void ProcessWithLambda()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        entities.ForEach(e => DoSomething(e)); // Lambda reference
    }

    private void DoSomething(LargeEntity e) { }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ManualIterationLocalCapturedAfterLambdaDeclaration_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void ProcessWithCapturedManualLocal()
    {
        LargeEntity current = null;
        Action useLater = () => DoSomething(current);
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        current = entities[0];
        Console.WriteLine(current.Name);
        useLater();
    }

    private void DoSomething(LargeEntity e) { }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
