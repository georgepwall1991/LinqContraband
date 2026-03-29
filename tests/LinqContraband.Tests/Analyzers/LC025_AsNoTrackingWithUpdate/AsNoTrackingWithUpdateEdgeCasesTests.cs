using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC025_AsNoTrackingWithUpdate;

public class AsNoTrackingWithUpdateEdgeCasesTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Collections.Generic;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public void Update(object entity) { }
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public void Update(TEntity entity) { }
        public Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
    }
}
";

    [Fact]
    public async Task ForeachVariable_FromAsNoTrackingCollection_ShouldTrigger()
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
            foreach (var user in users.AsNoTracking().ToList())
            {
                users.Update({|LC025:user|});
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LocalFromTrackedQuery_ShouldNotTrigger()
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
            var user = users.FirstOrDefault(x => x.Id == 1);
            users.Update(user);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
