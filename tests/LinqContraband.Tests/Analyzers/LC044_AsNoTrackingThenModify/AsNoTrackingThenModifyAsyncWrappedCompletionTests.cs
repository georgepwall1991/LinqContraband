using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public partial class AsNoTrackingThenModifyEdgeCasesTests
{
    // Post-await mutations are not in the synchronous prefix, so this fixture
    // isolates CompletesBeforeSave's conversion unwrap. Bare `await Rename()`
    // stays green if that unwrap is deleted. Parenthesized `await (Rename())`
    // is omitted from the IOperation tree and does not isolate the arm.
    // `await Task.WhenAll((Task)Rename())` is a pre-existing FN (params array
    // initializer sits between the cast and the combinator walk).

    [Fact]
    public async Task AsyncHelperCastAwait_Triggers()
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
            await (Task)Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperNestedLambdaAwaitBeforeMutation_Triggers()
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
            async Task Rename()
            {
                System.Func<Task> nested = async () => await Task.Yield();
                {|LC044:u.Name|} = ""changed"";
                await Task.Yield();
            }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperNestedLocalFunctionAwaitBeforeMutation_Triggers()
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
            async Task Rename()
            {
                async Task Nested() { await Task.Yield(); }
                {|LC044:u.Name|} = ""changed"";
                await Task.Yield();
            }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncHelperNestedAnonymousAwaitBeforeMutation_Triggers()
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
            async Task Rename()
            {
                System.Func<Task> nested = async delegate { await Task.Yield(); };
                {|LC044:u.Name|} = ""changed"";
                await Task.Yield();
            }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
