using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer,
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateFixer>;

namespace LinqContraband.Tests.Analyzers.LC025_AsNoTrackingWithUpdate;

public class AsNoTrackingWithUpdateTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Collections.Generic;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { 
        public void Update(object entity) { }
        public void Remove(object entity) { }
    }
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public void Update(TEntity entity) { }
        public void Remove(TEntity entity) { }
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
    public async Task AsNoTracking_ThenUpdate_ShouldTriggerLC025()
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
            var user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update({|LC025:user|});
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldRemoveAsNoTracking()
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
            var user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update({|LC025:user|});
            }
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
            var user = users.FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update(user);
            }
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task AsNoTracking_ThenRemove_ShouldTriggerLC025()
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
            var user = users.AsNoTracking().First();
            users.Remove({|LC025:user|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Tracked_ThenUpdate_ShouldNotTrigger()
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
            if (user != null)
            {
                users.Update(user);
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
