using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public class AsNoTrackingThenModifyEdgeCasesTests
{
    private const string EfCoreMock = AsNoTrackingThenModifyAnalyzerTests.EfCoreMock;

    private const string Preamble = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;";

    [Fact]
    public async Task AsNoTracking_Mutate_ContextUpdate_ThenSave_DoesNotTrigger()
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
            u.Name = ""x"";
            ctx.Update(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_DbSetUpdate_ThenSave_DoesNotTrigger()
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
            u.Name = ""x"";
            ctx.Users.Update(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ContextAttach_ThenSave_DoesNotTrigger()
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
            u.Name = ""x"";
            ctx.Attach(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttach_ThenMutate_ThenSave_DoesNotTrigger()
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
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_DbSetUpdate_ThenMutate_ThenSave_DoesNotTrigger()
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
            ctx.Users.Update(u);
            u.Name = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_EntryStateModified_ThenMutate_ThenSave_DoesNotTrigger()
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
            ctx.Entry(u).State = EntityState.Modified;
            u.Name = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttachInsideNestedBlock_ThenMutate_ThenSave_DoesNotTrigger()
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
            {
                ctx.Attach(u);
            }
            u.Name = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ConditionalContextAttach_ThenMutate_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool attach)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (attach)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_BracelessConditionalContextAttach_ThenMutate_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool attach)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (attach)
                ctx.Attach(u);
            {|LC044:u.Name|} = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_GuardedContextAttachWithElseReturn_ThenMutate_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool attach)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (attach)
                ctx.Attach(u);
            else
                return;
            u.Name = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_LoopGuardedContextAttachWithElseContinue_ThenMutate_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool attach)
        {
            while (true)
            {
                var u = ctx.Users.AsNoTracking().First();
                if (attach)
                    ctx.Attach(u);
                else
                    continue;
                u.Name = ""x"";
                ctx.SaveChanges();
                break;
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttach_ThenEntryStateDetached_ThenMutate_ThenSave_StillTriggers()
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
            ctx.Entry(u).State = EntityState.Detached;
            {|LC044:u.Name|} = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttach_ThenChangeTrackerClear_ThenMutate_ThenSave_StillTriggers()
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
            ctx.ChangeTracker.Clear();
            {|LC044:u.Name|} = ""x"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttach_ThenMutate_ThenEntryStateDetached_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""x"";
            ctx.Entry(u).State = EntityState.Detached;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttach_ThenMutate_ThenChangeTrackerClear_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""x"";
            ctx.ChangeTracker.Clear();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttach_ThenMutate_ClearAndReturnBeforeSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool cancel)
        {
            var u = ctx.Users.AsNoTracking().First();
            ctx.Attach(u);
            u.Name = ""x"";
            if (cancel)
            {
                ctx.ChangeTracker.Clear();
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachAttachThenClearBeforeMutation_StillTriggers()
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
            foreach (var u in ctx.Users.AsNoTracking())
            {
                ctx.Attach(u);
                ctx.ChangeTracker.Clear();
                {|LC044:u.Name|} = ""x"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_EntryStateModified_ThenSave_DoesNotTrigger()
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
            u.Name = ""x"";
            ctx.Entry(u).State = EntityState.Modified;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_EntryStateModified_ThenDetached_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""x"";
            ctx.Entry(u).State = EntityState.Modified;
            ctx.Entry(u).State = EntityState.Detached;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ContextAttach_ThenChangeTrackerClear_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""x"";
            ctx.Attach(u);
            ctx.ChangeTracker.Clear();
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

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
}
