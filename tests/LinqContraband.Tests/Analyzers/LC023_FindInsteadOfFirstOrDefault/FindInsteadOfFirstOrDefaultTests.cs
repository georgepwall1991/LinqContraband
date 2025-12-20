using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer>;

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
        public void TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var result = {|LC023:users.FirstOrDefault(x => x.Id == 1)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingleAsync_WithId_ShouldTriggerLC023()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;" + EFCoreMock + @"
namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<TSource> SingleAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
    }
}
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public async Task TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var result = await {|LC023:users.SingleAsync(x => x.Id == 1)|};
        }
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithNonId_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestClass
    {
        public void TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var result = users.FirstOrDefault(x => x.Name == ""abc"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_OnQueryable_ShouldNotTrigger()
    {
        // Find only works on DbSet
        var test = @"using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(System.Collections.Generic.IEnumerable<User> users)
        {
            var result = users.AsQueryable().Where(x => x.Id > 0).FirstOrDefault(x => x.Id == 1);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
