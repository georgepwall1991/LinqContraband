using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC038_ExcessiveEagerLoading.ExcessiveEagerLoadingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC038_ExcessiveEagerLoading;

public class ExcessiveEagerLoadingTests
{
    private const string EFCoreMock = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public sealed class IncludableQueryable<TEntity, TPreviousProperty> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty>> navigationPropertyPath) where TEntity : class => new IncludableQueryable<TEntity, TProperty>();
        public static IncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(this IncludableQueryable<TEntity, TPreviousProperty> source, Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath) where TEntity : class => new IncludableQueryable<TEntity, TProperty>();
        public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> AsSplitQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> TagWith<TEntity>(this IQueryable<TEntity> source, string tag) where TEntity : class => source;
        public static List<TEntity> ToList<TEntity>(this IQueryable<TEntity> source) => new List<TEntity>();
    }
}
";

    private const string Types = @"
namespace TestApp
{
    public class Child
    {
        public int Id { get; set; }
    }

    public class Parent
    {
        public int Id { get; set; }
        public Child Child1 { get; set; }
        public Child Child2 { get; set; }
        public Child Child3 { get; set; }
        public Child Child4 { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Parent> Parents { get; set; }
    }
}
";

    [Fact]
    public async Task FourIncludeSteps_TriggersByDefaultThreshold()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var parents = {|LC038:db.Parents
            .Include(p => p.Child1)
            .Include(p => p.Child2)
            .Include(p => p.Child3)
            .Include(p => p.Child4)|}
            .ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThreeIncludeSteps_DoesNotTriggerByDefaultThreshold()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var parents = db.Parents
            .Include(p => p.Child1)
            .Include(p => p.Child2)
            .Include(p => p.Child3)
            .ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThresholdFromAnalyzerConfig_SuppressesFourStepChain()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC038_ExcessiveEagerLoading.ExcessiveEagerLoadingAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var parents = db.Parents
            .Include(p => p.Child1)
            .Include(p => p.Child2)
            .Include(p => p.Child3)
            .Include(p => p.Child4)
            .ToList();
    }
}"
        };

        test.TestState.AnalyzerConfigFiles.Add(("/0/.editorconfig", """
root = true

[*.cs]
dotnet_code_quality.LC038.include_threshold = 5
"""));

        await test.RunAsync();
    }

    [Fact]
    public async Task DirectDbSetSetInvocation_WithFiveSteps_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var parents = {|LC038:db.Set<TestApp.Parent>()
            .Include(p => p.Child1)
            .ThenInclude(c => c.Id)
            .Include(p => p.Child2)
            .ThenInclude(c => c.Id)
            .Include(p => p.Child3)|}
            .ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FilteredQueryBeforeIncludeChain_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var parents = {|LC038:db.Parents
            .Where(p => p.Id > 0)
            .OrderBy(p => p.Id)
            .Take(100)
            .Include(p => p.Child1)
            .Include(p => p.Child2)
            .Include(p => p.Child3)
            .Include(p => p.Child4)|}
            .ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EfQueryOptionsBeforeIncludeChain_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(TestApp.AppDbContext db)
    {
        var parents = {|LC038:db.Parents
            .AsNoTracking()
            .AsSplitQuery()
            .TagWith(""reviewed-load"")
            .Include(p => p.Child1)
            .Include(p => p.Child2)
            .Include(p => p.Child3)
            .Include(p => p.Child4)|}
            .ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
