using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC051_ToAsyncEnumerableOnQuery.ToAsyncEnumerableOnQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC051_ToAsyncEnumerableOnQuery;

public class ToAsyncEnumerableOnQueryTests
{
    /// <summary>EF Core surface used by the tests. Kept separate so it can also be compiled as its own assembly.</summary>
    internal const string EfCoreMock = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => null;
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IAsyncEnumerable<TSource> AsAsyncEnumerable<TSource>(this IQueryable<TSource> source) => null;
        public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
    }
}
";

    /// <summary>The BCL's System.Linq.AsyncEnumerable (.NET 10) and the System.Linq.Async package share this shape.</summary>
    internal const string AsyncEnumerableMock = @"
namespace System.Linq
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public static class AsyncEnumerable
    {
        public static IAsyncEnumerable<TSource> ToAsyncEnumerable<TSource>(this IEnumerable<TSource> source) => null;
        public static IAsyncEnumerable<TSource> ToAsyncEnumerable<TSource>(this Task<TSource> task) => null;
    }
}
";

    internal const string Model = @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.EntityFrameworkCore;

    public class Order { public int Id { get; set; } public decimal Total { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }
}
";

    internal static string Wrap(string body, string usings = "    using Microsoft.EntityFrameworkCore;\n") => EfCoreMock + AsyncEnumerableMock + Model + @"
namespace TestApp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
" + usings + @"
    public class Queries
    {
        public async Task Run(AppDbContext db, List<Order> cached, IQueryable<Order> unknown)
        {
" + body + @"
            await Task.CompletedTask;
        }
    }
}";

    [Fact]
    public async Task DbSet_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            await foreach (var order in db.Orders.{|LC051:ToAsyncEnumerable|}())
            {
            }"));
    }

    [Fact]
    public async Task QueryChain_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = db.Orders.AsNoTracking().Where(o => o.Total > 0).OrderBy(o => o.Id).Select(o => o.Id).{|LC051:ToAsyncEnumerable|}();"));
    }

    [Fact]
    public async Task SetOfT_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = db.Set<Order>().Where(o => o.Total > 0).{|LC051:ToAsyncEnumerable|}();"));
    }

    [Fact]
    public async Task QuerySyntax_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = (from o in db.Orders where o.Total > 0 select o.Id).{|LC051:ToAsyncEnumerable|}();"));
    }

    [Fact]
    public async Task StaticForm_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = AsyncEnumerable.{|LC051:ToAsyncEnumerable|}(db.Orders.Where(o => o.Total > 0));"));
    }

    [Fact]
    public async Task AsAsyncEnumerable_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = db.Orders.Where(o => o.Total > 0).AsAsyncEnumerable();"));
    }

    [Fact]
    public async Task InMemorySources_StayQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var a = cached.ToAsyncEnumerable();
            var b = cached.AsQueryable().Where(o => o.Total > 0).ToAsyncEnumerable();
            var c = Task.FromResult(1).ToAsyncEnumerable();"));
    }

    [Fact]
    public async Task UnknownQueryable_StaysQuiet()
    {
        // The parameter may not be EF-backed, and AsAsyncEnumerable() throws on a queryable EF Core does not back.
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = unknown.Where(o => o.Total > 0).ToAsyncEnumerable();"));
    }

    [Fact]
    public async Task AfterAsEnumerable_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var stream = db.Orders.AsEnumerable().Where(o => o.Total > 0).ToAsyncEnumerable();"));
    }

    [Theory]
    [InlineData("10.0.0.0", true)]
    [InlineData("11.0.0.0", false)]
    public async Task EfCore11_OwnsTheCheck(string efVersion, bool reports)
    {
        var body = reports
            ? "            var stream = db.Orders.{|LC051:ToAsyncEnumerable|}();"
            : "            var stream = db.Orders.ToAsyncEnumerable();";

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC051_ToAsyncEnumerableOnQuery.ToAsyncEnumerableOnQueryAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = AsyncEnumerableMock + Model + @"
namespace TestApp
{
    using System.Linq;

    public class Queries
    {
        public void Run(AppDbContext db)
        {
" + body + @"
        }
    }
}"
        };

        var efProject = new ProjectState("Microsoft.EntityFrameworkCore", Microsoft.CodeAnalysis.LanguageNames.CSharp, "/ef/", "cs");
        test.TestState.AdditionalProjects.Add("Microsoft.EntityFrameworkCore", efProject);
        efProject.Sources.Add(("/ef/AssemblyInfo.cs", $"[assembly: System.Reflection.AssemblyVersion(\"{efVersion}\")]\n"));
        efProject.Sources.Add(("/ef/EfCore.cs", EfCoreMock));
        test.TestState.AdditionalProjectReferences.Add("Microsoft.EntityFrameworkCore");

        await test.RunAsync();
    }
}
