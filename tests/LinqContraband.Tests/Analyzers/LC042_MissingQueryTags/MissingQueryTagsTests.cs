using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC042_MissingQueryTags.MissingQueryTagsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC042_MissingQueryTags;

public class MissingQueryTagsTests
{
    private const string EfCoreMock = @"
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity>
    {
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static DbSet<TSource> Where<TSource>(this DbSet<TSource> source, Expression<Func<TSource, bool>> predicate) => source;
        public static DbSet<TResult> Select<TSource, TResult>(this DbSet<TSource> source, Expression<Func<TSource, TResult>> selector) => new DbSet<TResult>();
        public static DbSet<TSource> Include<TSource, TProperty>(this DbSet<TSource> source, Expression<Func<TSource, TProperty>> path) => source;
        public static DbSet<TSource> ThenInclude<TSource, TPreviousProperty, TProperty>(this DbSet<TSource> source, Expression<Func<TPreviousProperty, TProperty>> path) => source;
        public static DbSet<TSource> OrderBy<TSource, TKey>(this DbSet<TSource> source, Expression<Func<TSource, TKey>> keySelector) => source;
        public static DbSet<TSource> TagWith<TSource>(this DbSet<TSource> source, string tag) => source;
        public static DbSet<TSource> TagWithCallSite<TSource>(this DbSet<TSource> source) => source;
        public static List<TSource> ToList<TSource>(this DbSet<TSource> source) => new List<TSource>();
        public static TSource FirstOrDefault<TSource>(this DbSet<TSource> source) => default(TSource);
    }
}
";

    [Fact]
    public async Task ThreeStepQuery_WithoutTag_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User { public int Id { get; set; } public string Name { get; set; } }

    public class TestClass
    {
        public System.Collections.Generic.List<User> Run(DbSet<User> users)
        {
            return {|LC042:users.Where(x => x.Id > 0).Include(x => x.Name).OrderBy(x => x.Name).ToList()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TwoStepQuery_WithoutTag_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User { public int Id { get; set; } public string Name { get; set; } }

    public class TestClass
    {
        public System.Collections.Generic.List<string> Run(DbSet<User> users)
        {
            return users.Where(x => x.Id > 0).Select(x => x.Name).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaggedQuery_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User { public int Id { get; set; } public string Name { get; set; } }

    public class TestClass
    {
        public System.Collections.Generic.List<User> Run(DbSet<User> users)
        {
            return users.Where(x => x.Id > 0).Include(x => x.Name).OrderBy(x => x.Name).TagWith(""hot path"").ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TagWithCallSite_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User { public int Id { get; set; } public string Name { get; set; } }

    public class TestClass
    {
        public User Run(DbSet<User> users)
        {
            return users.Where(x => x.Id > 0).Include(x => x.Name).TagWithCallSite().FirstOrDefault();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
