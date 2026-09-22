using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public partial class AsNoTrackingThenModifyEdgeCasesTests
{
    [Fact]
    public async Task AsyncHelperResultObserved_Triggers()
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
            async Task<int> Rename() { {|LC044:u.Name|} = ""changed""; await Task.Yield(); return 0; }
            _ = Rename().Result;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperGetAwaiterGetResult_Triggers()
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
            async Task Rename() { {|LC044:u.Name|} = ""changed""; await Task.Yield(); }
            Rename().GetAwaiter().GetResult();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperWaitAll_Triggers()
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
            async Task Rename() { {|LC044:u.Name|} = ""changed""; await Task.Yield(); }
            Task.WaitAll(Rename());
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperWaitAllArray_Triggers()
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
            async Task Rename() { {|LC044:u.Name|} = ""changed""; await Task.Yield(); }
            Task.WaitAll(new[] { Rename() });
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
