using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public partial class AsNoTrackingThenModifyEdgeCasesTests
{
    // Post-await mutations are not in the synchronous prefix, so these fixtures
    // isolate CompletesBeforeSave / CompletesStoredTask. Prefix-mutation pins
    // from #489 stay green if those walkers are deleted.

    [Fact]
    public async Task AsyncHelperAwaitedAfterAwait_Triggers()
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
            await Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperWhenAllAwaitedAfterAwait_Triggers()
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
            await Task.WhenAll(Rename());
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperWhenAllArrayAwaitedAfterAwait_Triggers()
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
            await Task.WhenAll(new[] { Rename() });
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperWaitAllAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task.WaitAll(Rename());
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperWaitAllArrayAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Task.WaitAll(new[] { Rename() });
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperWaitedAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Rename().Wait();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperResultAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task<int> Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; return 0; }
            _ = Rename().Result;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperGetResultAfterAwait_Triggers()
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
            var u = ctx.Users.AsNoTracking().First();
            async Task Rename() { await Task.Yield(); {|LC044:u.Name|} = ""changed""; }
            Rename().GetAwaiter().GetResult();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperConfigureAwaitAfterAwait_Triggers()
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
            await Rename().ConfigureAwait(false);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperStoredThenAwaitedAfterAwait_Triggers()
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
            var t = Rename();
            await t;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
