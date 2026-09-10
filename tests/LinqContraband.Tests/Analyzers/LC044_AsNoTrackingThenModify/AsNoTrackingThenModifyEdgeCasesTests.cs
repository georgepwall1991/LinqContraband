using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public partial class AsNoTrackingThenModifyEdgeCasesTests
{
    private const string EfCoreMock = AsNoTrackingThenModifyAnalyzerTests.EfCoreMock;

    private const string Preamble = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;";

    [Fact]
    public async Task AsNoTracking_OnContextA_SaveChangesOnContextB_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctxA, TestCtx ctxB)
        {
            var u = ctxA.Users.AsNoTracking().First();
            u.Name = ""x"";
            ctxB.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EntityParameter_NoLocalOrigin_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, User u)
        {
            u.Name = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultipleReassignments_AmbiguousDataflow_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (flag)
                u = ctx.Users.First();
            u.Name = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationAfterSaveChanges_DoesNotTrigger()
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
            ctx.SaveChanges();
            u.Name = ""post-save mutation is a separate concern"";
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInBranchWithEarlyReturn_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (flag)
            {
                u.Name = ""never persisted but never saved either"";
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DifferentEntityMutated_DoesNotTrigger()
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
            var untracked = ctx.Users.AsNoTracking().First();
            var tracked = ctx.Users.First();
            tracked.Name = ""ok"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CollectionMutation_NotEntityProperty_DoesNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;" + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public List<string> Tags { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Tags.Add(""x""); // mutates a collection reference, not a User property
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInInvokedLocalFunction_Triggers()
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
            void Rename() => {|LC044:u.Name|} = ""changed"";
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInUninvokedLocalFunction_DoesNotTrigger()
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
            void Rename() => u.Name = ""changed"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInAwaitedAsyncLocalFunction_Triggers()
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
            async Task Rename() { {|LC044:u.Name|} = ""changed""; await Task.Yield(); }
            await Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInUnawaitedAsyncLocalFunction_DoesNotTrigger()
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
            async Task Rename() { u.Name = ""changed""; await Task.Yield(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationOnLocalFunctionParameter_DoesNotTrigger()
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
            void Rename(User other) => other.Name = ""changed"";
            Rename(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AttachInsideLocalFunction_DoesNotTrigger()
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
            void Rename() { ctx.Attach(u); u.Name = ""changed""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LambdaInsideLocalFunction_DoesNotTrigger()
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
            void Prepare() { System.Action unused = () => u.Name = ""changed""; }
            Prepare();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IteratorLocalFunction_DoesNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;" + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            IEnumerable<int> Rename() { u.Name = ""changed""; yield return 1; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowAfterMutateInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""changed""; throw new System.Exception(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
    [Fact]
    public async Task AttachDominatesNestedMutate_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { ctx.Attach(u); if (flag) { u.Name = ""x""; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowInOuterBlockAfterMutate_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) { u.Name = ""x""; } throw new System.Exception(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InnerSaveReportsOnce()
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
            void Work()
            {
                var u = ctx.Users.AsNoTracking().First();
                {|LC044:u.Name|} = ""x"";
                ctx.SaveChanges();
            }
            Work();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }


    [Fact]
    public async Task FinallyUpdateInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { u.Name = ""x""; } finally { ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedBlockThrowInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; { throw new System.Exception(); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrecedingBlockAttachInLocalFunction_DoesNotTrigger()
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
            void Rename() { { ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FinallyThrowInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { u.Name = ""x""; } finally { throw new System.Exception(); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DetachRootMutateChildInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User { public int Id { get; set; } public string Name { get; set; } public Address Address { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { ctx.Attach(u); ctx.Entry(u).State = EntityState.Detached; u.Address.City = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TryThrowFinallyEmptyInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; try { throw new System.Exception(); } finally { } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExhaustiveThrowBranchesInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; if (flag) throw new System.Exception(); else throw new System.Exception(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedClearUninvokedInLocalFunction_DoesNotTrigger()
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
            void Rename() { ctx.Attach(u); void Unused() { ctx.ChangeTracker.Clear(); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FollowingBlockUpdateInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; { ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CollectiveBranchAttachInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) { ctx.Attach(u); } else { ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RethrowingHandlerInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; try { throw new System.Exception(); } catch (System.Exception) { throw; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DeclaredAfterSaveLocalFunction_Triggers()
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
            Rename();
            ctx.SaveChanges();
            void Rename() => {|LC044:u.Name|} = ""x"";
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CaughtThrowInLocalFunction_Triggers()
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
            void Rename() { try { {|LC044:u.Name|} = ""x""; throw new System.Exception(); } catch (System.Exception) { } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FollowingTryUpdateInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; try { ctx.Update(u); } catch { throw; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrecedingReturnInLocalFunction_DoesNotTrigger()
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
            void Rename() { return; u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InfiniteLoopAfterMutateInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; while (true) { } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FinallyAttachPrecedesMutateInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { } finally { ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideInfiniteLoopInLocalFunction_DoesNotTrigger()
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
            void Rename() { while (true) { u.Name = ""x""; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TryCatchBothAttachInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { ctx.Attach(u); } catch { ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantFalseGuardInLocalFunction_DoesNotTrigger()
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
            void Rename() { if (false) { u.Name = ""x""; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoOnceAttachInLocalFunction_DoesNotTrigger()
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
            void Rename() { do { ctx.Attach(u); } while (false); u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantFalseForBodyInLocalFunction_DoesNotTrigger()
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
            void Rename() { for (; false;) { u.Name = ""x""; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedHelperAttachInLocalFunction_DoesNotTrigger()
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
            void Rename() { void Track() => ctx.Attach(u); Track(); u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchPathUpdateInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { u.Name = ""x""; throw new System.Exception(); } catch { ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DeadBranchInvocationInLocalFunction_DoesNotTrigger()
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
            void Rename() => u.Name = ""x"";
            if (false) { Rename(); }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ParentReturnHidesBlockInLocalFunction_DoesNotTrigger()
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
            void Rename() { return; { u.Name = ""x""; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LateDeclaredNestedAttachInLocalFunction_DoesNotTrigger()
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
            Rename();
            ctx.SaveChanges();
            void Rename() { void Track() => ctx.Attach(u); Track(); u.Name = ""x""; }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowingElseUpdateInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; if (flag) { ctx.Update(u); } else { throw new System.Exception(); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OuterDeclaredInnerSaveInLocalFunction_TriggersOnce()
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
            void Rename()
            {
                {|LC044:u.Name|} = ""x"";
                ctx.SaveChanges();
            }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConfigureAwaitCallInLocalFunction_Triggers()
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
            async Task Rename() { {|LC044:u.Name|} = ""x""; await Task.Yield(); }
            await Rename().ConfigureAwait(false);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchAttachAfterMutateInLocalFunction_Triggers()
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
            void Rename() { try { {|LC044:u.Name|} = ""x""; throw new System.Exception(); } catch { ctx.Attach(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MixedBranchPersistenceInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { {|LC044:u.Name|} = ""x""; if (flag) { ctx.Update(u); } else { ctx.Attach(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnBeforeThrowInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) { {|LC044:u.Name|} = ""x""; return; } throw new System.Exception(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LockAttachInLocalFunction_DoesNotTrigger()
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
            void Rename() { lock (ctx) { ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UserConversionNullInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public sealed class Wrapper
    {
        public string Value { get; set; }
        public static implicit operator Wrapper(string value) => new Wrapper { Value = value ?? ""d"" };
    }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; _ = (Wrapper?)(string)null ?? throw new System.Exception(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(75, 29, 75, 35).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThrowExpressionAfterMutateInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; _ = (string)null ?? throw new System.Exception(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CoalesceValueAfterMutateInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, string value)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; _ = value ?? ""d""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 29, 70, 35).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task FinallyExitGotoInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag, bool done)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { Retry: if (done) { try { goto End; } finally { ctx.Update(u); } } u.Name = ""x""; done = true; if (flag) goto Retry; ctx.Update(u); End: ; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExitArmPersistInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag, bool done)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { Retry: if (done) { ctx.Update(u); goto End; } u.Name = ""x""; done = true; if (flag) goto Retry; ctx.Update(u); End: ; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GotoOutSkipsUpdateInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag, bool done)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { Retry: if (done) goto End; u.Name = ""x""; done = true; if (flag) goto Retry; ctx.Update(u); End: ; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 56, 70, 62).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task FinallyUpdateBeforeReturnInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; if (flag) { try { return; } finally { ctx.Update(u); } } ctx.Update(u); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PersistBeforeReturnInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; if (flag) { ctx.Update(u); return; } ctx.Update(u); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FinallySuppressedReturnInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; if (flag) { try { return; } finally { throw new System.Exception(); } } ctx.Update(u); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExclusiveArmReturnInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) u.Name = ""x""; else return; ctx.Update(u); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task BackwardGotoSkipsUpdateInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag, bool done)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { Retry: if (done) return; u.Name = ""x""; done = true; if (flag) goto Retry; ctx.Update(u); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 54, 70, 60).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DeadReturnBeforeUpdateInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; { if (false) return; ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GotoBeforeUpdateInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; { if (flag) goto Done; System.Console.WriteLine(""work""); Done: ; ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InnerBreakBeforeUpdateInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; { while (flag) { break; } ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnBeforeUpdateInUsingInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { u.Name = ""x""; using (var scope = new System.IO.MemoryStream()) { if (flag) return; ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 29, 70, 35).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConditionalAttachInUsingInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { using (var scope = new System.IO.MemoryStream()) { if (flag) ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
}}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 107, 70, 113).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UsingBlockAttachInLocalFunction_DoesNotTrigger()
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
            void Rename() { using (var scope = new System.IO.MemoryStream()) { ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CoalesceLeftOperandReattachInLocalFunction_DoesNotTrigger()
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
            u.Name = (ctx.Entry(u).State = EntityState.Modified).ToString() ?? ""d"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedAssignRhsInLocalFunction_Triggers()
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
            string cached = null;
            u.Name = (cached = ""x"");
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(71, 13, 71, 19).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PlainAssignOperandReattachInLocalFunction_DoesNotTrigger()
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
            string cached = null;
            u.Name = (cached = (ctx.Entry(u).State = EntityState.Modified).ToString());
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CoalesceAssignNoReattachInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            string cached = flag ? ""c"" : null;
            u.Name = cached ??= ""d"";
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(71, 13, 71, 19).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UnbracedConditionalAttachInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) ctx.Attach(u); u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 54, 70, 60).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConditionalAttachNestedHelperInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Track() { if (flag) { ctx.Attach(u); } }
            void Rename() { Track(); u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(71, 38, 71, 44).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task IteratorHelperAttachInLocalFunction_Triggers()
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
            System.Collections.Generic.IEnumerable<int> Track() { ctx.Attach(u); yield return 0; }
            void Rename() { Track(); u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(71, 38, 71, 44).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ClearAfterHelperCallInLocalFunction_Triggers()
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
            void Track() => ctx.Attach(u);
            void Rename() { Track(); u.Name = ""x""; ctx.ChangeTracker.Clear(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(71, 38, 71, 44).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CoalesceAssignOperandReattachInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            string cached = flag ? ""c"" : null;
            u.Name = cached ??= (ctx.Entry(u).State = EntityState.Modified).ToString();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(71, 13, 71, 19).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConditionalOperandReattachInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = flag ? ""a"" : (ctx.Entry(u).State = EntityState.Modified).ToString();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 13, 70, 19).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UnconditionalOperandReattachInLocalFunction_DoesNotTrigger()
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
            u.Name = (ctx.Entry(u).State = EntityState.Modified).ToString() + """";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InnerSaveNoClearInLocalFunction_DoesNotTrigger()
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
            void Rename() { ctx.Attach(u); u.Name = ""x""; ctx.SaveChanges(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedCallerHelpersInLocalFunction_DoesNotTrigger()
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
            Outer();
            void Outer()
            {
                var u = ctx.Users.AsNoTracking().First();
                void Track() => ctx.Attach(u);
                void Rename() { u.Name = ""x""; }
                Track();
                Rename();
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StatementAttachBeforeCallInLocalFunction_DoesNotTrigger()
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
            void Rename(object _) { u.Name = ""x""; }
            ctx.Attach(u);
            Rename(null);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UpdateStateInArgumentInLocalFunction_DoesNotTrigger()
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
            void Rename(object _) { u.Name = ""x""; }
            Rename(ctx.Entry(u).State = EntityState.Modified);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InnerSaveThenClearInLocalFunction_DoesNotTrigger()
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
            void Rename() { ctx.Attach(u); u.Name = ""x""; ctx.SaveChanges(); ctx.ChangeTracker.Clear(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DeadHelperInvalidationInLocalFunction_DoesNotTrigger()
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
            void Wipe() { ctx.ChangeTracker.Clear(); }
            void Rename() { ctx.Attach(u); u.Name = ""x""; }
            Rename();
            if (false) { Wipe(); }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnBranchHelperInvalidationInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Wipe() { ctx.ChangeTracker.Clear(); }
            void Rename() { ctx.Attach(u); u.Name = ""x""; }
            Rename();
            if (flag) { Wipe(); return; }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AttachMutateBaselineInLocalFunction_DoesNotTrigger()
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
            void Rename() { ctx.Attach(u); u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DirectAttachMutateClear_Triggers()
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
            ctx.Attach(u);
            u.Name = ""x"";
            ctx.ChangeTracker.Clear();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(71, 13, 71, 19).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConditionalAttachInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) { ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 58, 70, 64).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PersistingHelperAfterCallInLocalFunction_DoesNotTrigger()
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
            void Up() => ctx.Update(u);
            void Rename() { u.Name = ""x""; }
            Rename();
            Up();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CallerClearAfterCallInLocalFunction_Triggers()
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
            void Rename() { ctx.Attach(u); u.Name = ""x""; }
            Rename();
            ctx.ChangeTracker.Clear();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 44, 70, 50).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExclusiveDetachMutateInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { ctx.Attach(u); if (flag) { ctx.Entry(u).State = EntityState.Detached; } else { u.Name = ""x""; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnSkipsInvalidationInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { ctx.Attach(u); if (flag) { u.Name = ""x""; return; } ctx.ChangeTracker.Clear(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionalClearAfterMutateInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { ctx.Attach(u); if (flag) { u.Name = ""x""; } ctx.ChangeTracker.Clear(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        var expected = VerifyCS.Diagnostic().WithSpan(70, 56, 70, 62).WithArguments("u", "Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CallerHelperAttachInLocalFunction_DoesNotTrigger()
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
            void Track() => ctx.Attach(u);
            void Rename() { u.Name = ""x""; }
            Track();
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnFinallyThrowInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { u.Name = ""x""; return; } finally { throw new System.Exception(); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearThrowAfterMutateInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { ctx.Attach(u); u.Name = ""x""; if (flag) { ctx.ChangeTracker.Clear(); throw new System.Exception(); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearBeforeUpdateInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; ctx.ChangeTracker.Clear(); { ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UpdateBeforeClearInLocalFunction_Triggers()
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
            void Rename() { {|LC044:u.Name|} = ""x""; ctx.Update(u); ctx.ChangeTracker.Clear(); }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StateAssignmentArgumentInLocalFunction_DoesNotTrigger()
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
            void Rename(object entry) { u.Name = ""x""; }
            Rename(ctx.Entry(u).State = EntityState.Modified);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LockUpdateInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; lock (ctx) { ctx.Update(u); } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HelperAttachThenClearInLocalFunction_Triggers()
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
            void Track() { ctx.Attach(u); ctx.ChangeTracker.Clear(); }
            void Rename() { Track(); {|LC044:u.Name|} = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SecondSaveLosesWriteInLocalFunction_TriggersOnce()
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
            void Rename(string tag) { {|LC044:u.Name|} = tag; }
            Rename(""a"");
            ctx.Update(u);
            ctx.SaveChanges();
            ctx.ChangeTracker.Clear();
            Rename(""b"");
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearThenReturnInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { ctx.Attach(u); if (flag) { ctx.ChangeTracker.Clear(); return; } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExclusiveReturnAfterMutateInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) { u.Name = ""x""; throw new System.Exception(); } else { return; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }


    [Fact]
    public async Task FalseFilterCatchInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { ctx.Attach(u); } catch (System.Exception) when (false) { } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchAttachPrecedesMutateInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { throw new System.Exception(); } catch { ctx.Attach(u); } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DeadBranchClearInLocalFunction_DoesNotTrigger()
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
            void Rename() { ctx.Attach(u); if (false) ctx.ChangeTracker.Clear(); u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NullSkippedCallsInLocalFunction_DoesNotTrigger()
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
            bool Rename() { u.Name = ""x""; return true; }
            _ = (bool?)true ?? Rename();
            _ = ((string)null)?.Length.ToString().Contains(Rename().ToString());
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NullCoalesceRunsCallInLocalFunction_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool? maybe)
        {
            var u = ctx.Users.AsNoTracking().First();
            bool Rename() { {|LC044:u.Name|} = ""x""; return true; }
            _ = maybe ?? Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CompoundReturnGuardsInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) return; else return; u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TryReturnGuardInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { return; } finally { } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TryHangSkipsLoopCheckInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { u.Name = ""x""; while (true) { } } finally { } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EmptyTryCatchMutationInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { } catch { u.Name = ""x""; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnElseAttachInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { if (flag) { ctx.Attach(u); } else { return; } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SiblingHelperAttachInLocalFunction_DoesNotTrigger()
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
            void Track() => ctx.Attach(u);
            void Rename() { Track(); u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LateDeclaredCatchUpdateInLocalFunction_DoesNotTrigger()
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
            Rename();
            ctx.SaveChanges();
            void Rename() { try { u.Name = ""x""; throw new System.Exception(); } catch { ctx.Update(u); } }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ShortCircuitedCallInLocalFunction_DoesNotTrigger()
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
            bool Rename() { u.Name = ""x""; return true; }
            _ = false && Rename();
            _ = true || Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EmptyCatchLosesMutationInLocalFunction_Triggers()
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
            void Rename() { try { {|LC044:u.Name|} = ""x""; throw new System.Exception(); } catch { } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TryAttachCatchReturnInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { ctx.Attach(u); } catch { return; } u.Name = ""x""; }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderedCatchRethrowInLocalFunction_DoesNotTrigger()
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
            void Rename() { u.Name = ""x""; try { throw new System.InvalidOperationException(); } catch (System.InvalidOperationException) { throw; } catch (System.Exception) { } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExclusiveBranchClearInLocalFunction_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var u = ctx.Users.AsNoTracking().First();
            void Rename() { ctx.Attach(u); if (flag) { ctx.ChangeTracker.Clear(); } else { u.Name = ""x""; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchRethrowInLocalFunction_DoesNotTrigger()
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
            void Rename() { try { u.Name = ""x""; throw new System.Exception(); } catch (System.Exception) { throw; } }
            Rename();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
