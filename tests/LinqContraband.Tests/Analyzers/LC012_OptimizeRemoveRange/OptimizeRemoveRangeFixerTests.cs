using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public class OptimizeRemoveRangeFixerTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Collections.Generic;

namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public void RemoveRange(IEnumerable<TEntity> entities) { }
        public Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }
    public static class RelationalQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
    }
}
";

    [Fact]
    public async Task Fixer_ShouldReplaceRemoveRangeWithExecuteDelete()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            {|LC012:users.RemoveRange(query)|};
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            query.ExecuteDelete();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
