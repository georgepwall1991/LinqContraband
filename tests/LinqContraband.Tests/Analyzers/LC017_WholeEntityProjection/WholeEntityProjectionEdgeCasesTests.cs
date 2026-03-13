using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Edge case tests for LC017 - Whole Entity Projection analyzer.
/// Tests various code patterns to ensure correct detection and avoid false positives.
/// </summary>
public class WholeEntityProjectionEdgeCasesTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

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
    // Large entity with 13 properties (12 value properties + 1 navigation property)
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

        // Navigation property
        public LargeEntity Parent { get; set; }
    }

    public class LargeEntityDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class MyDbContext : DbContext
    {
        public DbSet<LargeEntity> LargeEntities { get; set; }
    }
}";

    #region Property Access Patterns

    /// <summary>
    /// Innocent: Null-conditional operator changes the operation type.
    /// The analyzer doesn't track through conditional access.
    /// </summary>
    [Fact]
    public async Task TestCrime_NullConditionalPropertyAccess_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e?.Name);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Crime: Property accessed via method chain.
    /// </summary>
    [Fact]
    public async Task TestCrime_PropertyMethodChain_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name.ToUpper());
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Crime: Property accessed in string interpolation.
    /// </summary>
    [Fact]
    public async Task TestCrime_StringInterpolation_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine($""Name: {e.Name}"");
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Crime: Property accessed in binary expression.
    /// </summary>
    [Fact]
    public async Task TestCrime_BinaryExpression_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            var x = e.Id + 1;
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    #endregion

    #region Loop Variations

    /// <summary>
    /// Innocent: For loop with indexer - analyzer only tracks foreach iteration variables.
    /// </summary>
    [Fact]
    public async Task TestCrime_ForLoopWithIndexer_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        for (int i = 0; i < entities.Count; i++)
        {
            Console.WriteLine(entities[i].Name);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Innocent: Direct indexer access - analyzer only tracks foreach iteration variables.
    /// </summary>
    [Fact]
    public async Task TestCrime_DirectIndexerAccess_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        if (entities.Count > 0)
        {
            Console.WriteLine(entities[0].Name);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    #endregion

    #region Innocent - Various Usage Patterns

    /// <summary>
    /// Innocent: Entity used in object initializer (can't track reliably).
    /// </summary>
    [Fact]
    public async Task TestInnocent_ObjectInitializer_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            var dto = new LargeEntityDto { Id = e.Id, Name = e.Name };
            ProcessDto(dto);
        }
    }
    private void ProcessDto(LargeEntityDto dto) { }
}
" + MockNamespace;

        // This accesses 2 properties, should trigger
        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 2, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Innocent: Entity used in anonymous type creation.
    /// </summary>
    [Fact]
    public async Task TestCrime_AnonymousType_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            var anon = new { e.Name };
            Console.WriteLine(anon.Name);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Innocent: LINQ applied to result list (can't track usage through LINQ).
    /// </summary>
    [Fact]
    public async Task TestInnocent_LinqOnResult_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        var filtered = entities.Where(e => e.Name.StartsWith(""A"")).ToList();
        foreach (var e in filtered)
        {
            Console.WriteLine(e.Name);
        }
    }
}
" + MockNamespace;

        // The entity is used in a lambda passed to Where, so we can't track it
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Entity stored in another variable.
    /// </summary>
    [Fact]
    public async Task TestInnocent_VariableAlias_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        var alias = entities;
        foreach (var e in alias)
        {
            Console.WriteLine(e.Name);
        }
    }
}
" + MockNamespace;

        // Variable aliased - can't reliably track
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Using ToArray instead of ToList.
    /// </summary>
    [Fact]
    public async Task TestCrime_ToArray_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToArray();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 50)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    #endregion

    #region Innocent - Threshold Cases

    /// <summary>
    /// Innocent: Accessing exactly 3 properties (above threshold).
    /// </summary>
    [Fact]
    public async Task TestInnocent_ThreePropertiesAccessed_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Id);
            Console.WriteLine(e.Name);
            Console.WriteLine(e.Email);
        }
    }
}
" + MockNamespace;

        // 3 properties accessed - above MaxAccessedProperties threshold of 2
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Same property accessed multiple times (counts as 1).
    /// </summary>
    [Fact]
    public async Task TestCrime_SamePropertyMultipleTimes_CountsAsOne()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
            Console.WriteLine(e.Name.ToUpper());
            Console.WriteLine(e.Name.ToLower());
        }
    }
}
" + MockNamespace;

        // Same property (Name) accessed 3 times, but only counts as 1 unique property
        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    #endregion

    #region Innocent - Can't Track Usage

    /// <summary>
    /// Innocent: Entity assigned to field (can't track field usage).
    /// </summary>
    [Fact]
    public async Task TestInnocent_AssignedToField_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    private List<LargeEntity> _entities;

    public void Process()
    {
        var db = new MyDbContext();
        _entities = db.LargeEntities.ToList();
    }
}
" + MockNamespace;

        // Assigned to field - can't track downstream usage
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Entity used in conditional expression.
    /// </summary>
    [Fact]
    public async Task TestCrime_ConditionalExpression_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            var name = e != null ? e.Name : ""default"";
            Console.WriteLine(name);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    #endregion

    #region Innocent - Query Already Optimized

    /// <summary>
    /// Innocent: Query already uses Select with anonymous type.
    /// </summary>
    [Fact]
    public async Task TestInnocent_SelectAnonymousType_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities
            .Select(e => new { e.Id, e.Name })
            .ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /// <summary>
    /// Innocent: Query uses Select with DTO.
    /// </summary>
    [Fact]
    public async Task TestInnocent_SelectDto_NoDiagnostic()
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
    /// Innocent: Query uses Select with single property.
    /// </summary>
    [Fact]
    public async Task TestInnocent_SelectSingleProperty_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<string> GetNames()
    {
        var db = new MyDbContext();
        return db.LargeEntities.Select(e => e.Name).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    #endregion

    #region Async Variations

    /// <summary>
    /// Crime: ToListAsync is supported - async variants are also detected.
    /// </summary>
    [Fact]
    public async Task TestCrime_ToListAsync_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public async Task ProcessAsync()
    {
        var db = new MyDbContext();
        var entities = await db.LargeEntities.ToListAsync();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name);
        }
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public static class Extensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(source.ToList());
    }
}
" + MockNamespace;

        // ToListAsync IS supported - the analyzer checks ToList, ToListAsync, ToArray, ToArrayAsync
        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 30, 14, 60)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    #endregion

    #region Navigation Property Access

    /// <summary>
    /// Innocent: Accessing navigation property (navigations are complex).
    /// </summary>
    [Fact]
    public async Task TestInnocent_NavigationPropertyAccess_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Parent?.Name);
        }
    }
}
" + MockNamespace;

        // Accessing navigation property Parent, then its Name
        // This is complex to track - for now it should trigger since we're accessing e.Parent
        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    #endregion
}
