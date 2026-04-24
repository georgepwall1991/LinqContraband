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
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
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

namespace Microsoft.Extensions.DependencyInjection
{
    using System;
    using Microsoft.EntityFrameworkCore;

    public interface IServiceScope : IDisposable
    {
        IServiceProvider ServiceProvider { get; }
    }

    public interface IServiceScopeFactory
    {
        IServiceScope CreateScope();
    }

    public static class ServiceProviderServiceExtensions
    {
        public static T GetRequiredService<T>(this IServiceProvider provider) => default;
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
            return {|LC036:Task.Run(() => db.SaveChanges())|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
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
            {|LC036:Parallel.ForEach(users, user => db.SaveChanges())|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
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
            {|LC036:ThreadPool.QueueUserWorkItem(_ => db.SaveChanges())|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskFactoryStartNew_CapturingDbContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Task<int> Run(DbContext db)
        {
            return {|LC036:Task.Factory.StartNew(() => db.SaveChanges())|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NewThread_CapturingDbContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            {|LC036:new Thread(() => db.SaveChanges())|}.Start();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TimerCallback_CapturingDbContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Timer Run(DbContext db)
        {
            return {|LC036:new Timer(_ => db.SaveChanges(), null, 0, 1000)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskRun_AsyncLambdaCapturingDbContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Task<int> Run(DbContext db)
        {
            return {|LC036:Task.Run(async () => await db.SaveChangesAsync())|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskRun_CapturingDbContextMember_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        private readonly DbContext _db = new DbContext();

        public Task<int> Run()
        {
            return {|LC036:Task.Run(() => _db.SaveChanges())|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
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

    [Fact]
    public async Task CreatingScopeInsideTask_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Task<int> Run(IServiceScopeFactory scopeFactory)
        {
            return Task.Run(() =>
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DbContext>();
                return db.SaveChanges();
            });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskRun_PassingScalarOnly_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Task<int> Run(DbContext db)
        {
            var count = db.SaveChanges();
            return Task.Run(() => count);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskRun_AfterMaterializationWithoutContextCapture_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public Task<int> Run(DbContext db)
        {
            var saved = db.SaveChanges();
            var values = new List<int> { saved };
            return Task.Run(() => values.Count);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
