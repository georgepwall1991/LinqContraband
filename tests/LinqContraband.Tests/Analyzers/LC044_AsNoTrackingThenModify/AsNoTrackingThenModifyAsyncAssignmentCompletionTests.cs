using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public partial class AsNoTrackingThenModifyEdgeCasesTests
{
    // Post-await mutations plus assignment-store / collection-expression
    // completion isolate CompletesBeforeSave and CompletesStoredTask arms
    // that prefix pins and PR #494's declarator/array AfterAwait fixtures
    // do not cover. Prefix-mutation pins stay green if these walks are deleted.

    [Fact]
    public async Task AsyncHelperAssignedThenAwaitedAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task t;
            t = Rename();
            await t;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperWhenAllCollectionAwaitedAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            await Task.WhenAll([Rename()]);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperAssignedThenConfigureAwaitAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task t;
            t = Rename();
            await t.ConfigureAwait(false);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperAssignedThenWaitedAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task t;
            t = Rename();
            t.Wait();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperAssignedThenResultAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task<int> Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; return 0; }
            Task<int> t;
            t = Rename();
            _ = t.Result;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperAssignedThenWhenAllAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task t;
            t = Rename();
            await Task.WhenAll(t);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperAssignedThenWaitAllAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task t;
            t = Rename();
            Task.WaitAll(t);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperAssignedThenWhenAllCollectionAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task t;
            t = Rename();
            await Task.WhenAll([t]);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperAssignedThenGetResultAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task t;
            t = Rename();
            t.GetAwaiter().GetResult();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
