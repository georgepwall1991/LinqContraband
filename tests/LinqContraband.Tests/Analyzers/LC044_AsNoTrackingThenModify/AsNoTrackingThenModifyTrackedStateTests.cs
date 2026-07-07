using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public partial class AsNoTrackingThenModifyEdgeCasesTests
{
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
}
