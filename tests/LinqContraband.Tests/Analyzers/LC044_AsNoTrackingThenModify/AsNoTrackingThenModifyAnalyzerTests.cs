using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public class AsNoTrackingThenModifyAnalyzerTests
{
    internal const string EfCoreMock = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public enum EntityState { Detached, Unchanged, Deleted, Modified, Added }

    public class EntityEntry
    {
        public EntityState State { get; set; }
    }

    public class DbContext
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public void Update(object entity) { }
        public void Attach(object entity) { }
        public EntityEntry Entry(object entity) => new EntityEntry();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public void Update(TEntity entity) { }
        public void Attach(TEntity entity) { }
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        IEnumerator<TEntity> IEnumerable<TEntity>.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) where TSource : class => source;
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default) => Task.FromResult(default(TSource));
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken ct = default) => Task.FromResult(default(TSource));
        public static Task<TSource> SingleOrDefaultAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default) => Task.FromResult(default(TSource));
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default) => Task.FromResult(new List<TSource>());
    }
}
";

    private const string Preamble = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;";

    [Fact]
    public async Task AsNoTracking_ThenMutateProperty_ThenSaveChanges_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().FirstOrDefault(u => u.Id == 1);
            {|LC044:user.Name|} = ""new"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ThenMutate_ThenSaveChangesAsync_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public async Task M(TestCtx ctx)
        {
            var user = await ctx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == 1);
            {|LC044:user.Name|} = ""new"";
            await ctx.SaveChangesAsync();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Single_ThenMutate_ThenSave_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Email { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, int id, string newEmail)
        {
            var u = ctx.Users.AsNoTracking().Single(x => x.Id == id);
            {|LC044:u.Email|} = newEmail;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultipleMutations_Flagged_AtFirst()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } public string Email { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""a"";
            u.Email = ""b"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachOverAsNoTrackingList_MutateLoopVar_ThenSave_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            foreach (var u in ctx.Users.AsNoTracking().ToList())
            {
                {|LC044:u.Name|} = ""x"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TrackedQuery_Mutate_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.FirstOrDefault(x => x.Id == 1);
            u.Name = ""new"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ReadOnly_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().FirstOrDefault();
            var name = u.Name;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
