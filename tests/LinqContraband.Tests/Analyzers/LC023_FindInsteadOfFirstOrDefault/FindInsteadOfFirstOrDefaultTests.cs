using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer,
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultFixer>;

namespace LinqContraband.Tests.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public class FindInsteadOfFirstOrDefaultTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public TEntity Find(params object[] keyValues) => null;
        public ValueTask<TEntity> FindAsync(params object[] keyValues) => default;

        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
        public static Task<TSource> SingleOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
    }
}
";

    [Fact]
    public async Task FirstOrDefault_WithId_ShouldTriggerLC023()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result = {|LC023:users.FirstOrDefault(x => x.Id == 1)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithId_ShouldTriggerLC023()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var result = await {|LC023:users.SingleOrDefaultAsync(x => x.Id == 1)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceFirstOrDefaultWithFind()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result = {|LC023:users.FirstOrDefault(x => x.Id == 123)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var result = users.Find(123);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceSingleOrDefaultAsyncWithFindAsync()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var result = await {|LC023:users.SingleOrDefaultAsync(x => x.Id == 456)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public async Task TestMethod(DbSet<User> users)
        {
            var result = await users.FindAsync(456);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task First_WithId_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users)
        {
            return users.First(x => x.Id == 1);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithAsNoTracking_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users)
        {
            return users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithNonId_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users)
        {
            return users.FirstOrDefault(x => x.Name == ""abc"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
