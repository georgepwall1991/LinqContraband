using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public class MissingWhereBeforeExecuteDeleteUpdateTests
{
    private const string EfMock = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class RelationalQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
        public static Task<int> ExecuteDeleteAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(0);
        public static int ExecuteUpdate<TSource>(this IQueryable<TSource> source) => 0;
        public static Task<int> ExecuteUpdateAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(0);
        public static IQueryable<TSource> TagWith<TSource>(this IQueryable<TSource> source, string tag) => source;
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
    }
}
";

    [Fact]
    public async Task ExecuteDelete_WithoutWhere_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = {|LC035:db.Set<User>().ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_WithoutWhere_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = {|LC035:db.Set<User>().ExecuteUpdate()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithWhere_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = db.Set<User>().Where(u => u.Id > 10).ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithProjectLocalWhereLookalike_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public static class QueryExtensions
    {
        public static IQueryable<T> Where<T>(this IQueryable<T> source, string reason) => source;
    }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = {|LC035:db.Set<User>().Where(""reviewed"").ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_WithWhereAndTag_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = db.Set<User>().Where(u => u.Id > 10).TagWith(""bulk"").ExecuteUpdate();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithQuerySyntaxWhere_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result =
                (from user in db.Set<User>()
                 where user.Id > 10
                 select user)
                .ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_WithFilteredQuerySyntaxLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var filtered =
                from user in db.Set<User>()
                where user.Id > 10
                select user;

            var result = filtered.ExecuteUpdate();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithProjectLocalWhereLookalikeLocal_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public static class QueryExtensions
    {
        public static IQueryable<T> Where<T>(this IQueryable<T> source, string reason) => source;
    }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var filtered = db.Set<User>().Where(""reviewed"");
            var result = {|LC035:filtered.ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithUnfilteredQuerySyntax_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result =
                {|LC035:(from user in db.Set<User>()
                         select user)
                .ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_WithoutWhere_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public async System.Threading.Tasks.Task Run(DbContext db)
        {
            var result = await {|LC035:db.Set<User>().ExecuteDeleteAsync()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdateAsync_WithWhereThroughChainedOperators_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public async System.Threading.Tasks.Task Run(DbContext db)
        {
            var result = await db.Set<User>().AsNoTracking().Where(u => u.Id > 10).TagWith(""bulk"").ExecuteUpdateAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithFilteredLocalQuery_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var filtered = db.Set<User>().Where(u => u.Id > 10);
            var result = filtered.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithStraightLineFilteredReassignedLocalQuery_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            IQueryable<User> query = db.Set<User>();
            query = query.Where(u => u.Id > 10);

            var result = query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithConditionallyFilteredReassignedLocalQuery_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db, bool filter)
        {
            IQueryable<User> query = db.Set<User>();
            if (filter)
            {
                query = query.Where(u => u.Id > 10);
            }

            var result = {|LC035:query.ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithUnfilteredLocalQuery_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var allUsers = db.Set<User>();
            var result = {|LC035:allUsers.ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_InLookalikeNamespace_ShouldNotTrigger()
    {
        var test = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCoreFake;

namespace Microsoft.EntityFrameworkCoreFake
{
    public class DbContext
    {
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class RelationalQueryableExtensions
    {
        public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
    }
}

namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = db.Set<User>().ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
