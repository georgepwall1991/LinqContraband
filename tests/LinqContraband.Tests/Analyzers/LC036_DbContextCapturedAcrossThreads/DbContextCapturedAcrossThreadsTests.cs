using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC036_DbContextCapturedAcrossThreads.DbContextCapturedAcrossThreadsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC036_DbContextCapturedAcrossThreads;

public class DbContextCapturedAcrossThreadsTests
{
    private const string EfMock = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public interface IDbContextFactory<TContext> where TContext : DbContext
    {
        TContext CreateDbContext();
    }
}
";

    [Fact]
    public async Task TaskRun_CapturingDbContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Task<int> Run(DbContext db)
        {
            return Task.Run(() => db.SaveChanges());
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC036").WithSpan(37, 20, 37, 52).WithArguments("db", "Run");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ParallelForEach_CapturingDbContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public void Run(DbContext db, IEnumerable<User> users)
        {
            Parallel.ForEach(users, user => db.SaveChanges());
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC036").WithSpan(40, 13, 40, 62).WithArguments("db", "ForEach");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThreadPoolQueueUserWorkItem_CapturingDbContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            ThreadPool.QueueUserWorkItem(_ => db.SaveChanges());
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC036").WithSpan(37, 13, 37, 64).WithArguments("db", "QueueUserWorkItem");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CreatingContextInsideTask_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Task<int> Run()
        {
            return Task.Run(() =>
            {
                var db = new DbContext();
                return db.SaveChanges();
            });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingFactoryInsideTask_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Task<int> Run(IDbContextFactory<DbContext> factory)
        {
            return Task.Run(() =>
            {
                var db = factory.CreateDbContext();
                return db.SaveChanges();
            });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
