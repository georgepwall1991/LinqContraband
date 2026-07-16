using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public partial class AsNoTrackingThenModifyEdgeCasesTests
{
    [Fact]
    public async Task AsNoTracking_MutateNestedMember_ContextUpdateNestedEntity_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User { public int Id { get; set; } public Address Address { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            user.Address.City = ""London"";
            ctx.Update(user.Address);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateNestedMember_EntryNestedEntityModified_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User { public int Id { get; set; } public Address Address { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            user.Address.City = ""London"";
            ctx.Entry(user.Address).State = EntityState.Modified;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateNestedMember_UpdateDifferentNestedEntity_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User
    {
        public int Id { get; set; }
        public Address Address { get; set; }
        public Address BillingAddress { get; set; }
    }

    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            {|LC044:user.Address.City|} = ""London"";
            ctx.Update(user.BillingAddress);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ReattachFirstNestedMutation_ThenMutateSibling_StillTriggersOnSibling()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User
    {
        public int Id { get; set; }
        public Address Home { get; set; }
        public Address Work { get; set; }
    }

    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            user.Home.City = ""safe"";
            ctx.Update(user.Home);
            {|LC044:user.Work.City|} = ""lost"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_PriorReattachFirstNestedMutation_ThenMutateSibling_StillTriggersOnSibling()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User
    {
        public int Id { get; set; }
        public Address Home { get; set; }
        public Address Work { get; set; }
    }

    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            ctx.Update(user.Home);
            user.Home.City = ""safe"";
            {|LC044:user.Work.City|} = ""lost"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ConditionalReattach_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""lost when attach is false"";
            if (attach)
                ctx.Attach(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateAndReattachInSameConditionalBranch_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool update)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (update)
            {
                u.Name = ""persisted"";
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachMutate_ConditionalReattach_ThenSave_StillTriggers()
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
            foreach (var u in ctx.Users.AsNoTracking())
            {
                {|LC044:u.Name|} = ""lost when attach is false"";
                if (attach)
                    ctx.Attach(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachMutateAndReattachInSameConditionalBranch_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool update)
        {
            foreach (var u in ctx.Users.AsNoTracking())
            {
                if (update)
                {
                    u.Name = ""persisted"";
                    ctx.Update(u);
                }
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutationAndEarlierSaveInSiblingBranches_ThenFinalSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool mutate)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (mutate)
            {
                {|LC044:u.Name|} = ""lost"";
            }
            if (!mutate)
            {
                ctx.SaveChanges();
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_BreakBeforeReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool stop)
        {
            var u = ctx.Users.AsNoTracking().First();
            while (true)
            {
                {|LC044:u.Name|} = ""lost when stop is true"";
                if (stop)
                    break;
                ctx.Attach(u);
                break;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachMutate_ContinueBeforeReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool skip)
        {
            foreach (var u in ctx.Users.AsNoTracking())
            {
                {|LC044:u.Name|} = ""lost when skip is true"";
                if (skip)
                    continue;
                ctx.Attach(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachMutate_ReturnBeforeReattach_ThenSave_DoesNotTrigger()
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
            foreach (var u in ctx.Users.AsNoTracking())
            {
                u.Name = ""persisted when save is reached"";
                if (cancel)
                    return;
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtThrowBeforeReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                {|LC044:u.Name|} = ""lost when fail is true"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Attach(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_BreakToReattachAfterLoop_ThenSave_DoesNotTrigger()
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
            while (true)
            {
                u.Name = ""persisted"";
                break;
            }
            ctx.Update(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_GotoReattachLabel_ThenSave_DoesNotTrigger()
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
            u.Name = ""persisted"";
            goto Attach;

        Attach:
            ctx.Update(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ThrowWithNonMatchingCatchBeforeReattach_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                u.Name = ""persisted whenever save is reached"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.ArgumentException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutationAndCaughtThrowInExclusiveBranches_ThenReattach_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool update)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                if (update)
                    u.Name = ""persisted"";
                else
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ThrowCaughtByBaseTypeBeforeReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                {|LC044:u.Name|} = ""lost when fail is true"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Attach(u);
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtThrowHandlerReturnsBeforeSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                u.Name = ""persisted whenever save is reached"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtThrowThenReattachAfterTry_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                u.Name = ""persisted"";
                if (fail)
                    throw new System.InvalidOperationException();
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.Update(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ConstantFalseCatchFilterBeforeReattach_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                u.Name = ""persisted whenever save is reached"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException) when (1 + 1 == 3)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtThrowHandlerGotoPastSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                u.Name = ""persisted whenever save is reached"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
                goto AfterSave;
            }
            ctx.SaveChanges();

        AfterSave:
            return;
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ComplementaryBranchReattach_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool useUpdate)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted"";
            if (useUpdate)
                ctx.Update(u);
            else
                ctx.Update(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_OptionalNestedComplementaryBranchReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool reattach, bool useUpdate)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when reattach is false"";
            if (reattach)
            {
                if (useUpdate)
                    ctx.Update(u);
                else
                    ctx.Attach(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtThrowSkipsComplementaryBranchReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool useUpdate, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when fail is true"";
            try
            {
                if (useUpdate)
                {
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                else
                {
                    ctx.Attach(u);
                }
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_TryAndCatchBothReattach_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                u.Name = ""persisted"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtThrowSkipsCatchReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool failInner, bool failHandler)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when failHandler is true"";
                    if (failInner)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    if (failHandler)
                        throw new System.ArgumentException();
                    ctx.Attach(u);
                }
            }
            catch (System.ArgumentException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_TryAndCatchBothReattachWithHarmlessFinally_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                u.Name = ""persisted"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
                ctx.Update(u);
            }
            finally
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_TryAndCatchBothReattachThenFinallyClears_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                {|LC044:u.Name|} = ""lost after clear"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            finally
            {
                ctx.ChangeTracker.Clear();
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchReattachesBeforeOuterCatch_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    u.Name = ""persisted"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    ctx.Update(u);
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_FilteredInnerCatchMayDeclineBeforeOuterCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, bool handleInner)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when the filter declines"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException) when (handleInner)
                {
                    ctx.Attach(u);
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ConstantTrueInnerCatchReattachesBeforeOuterCatch_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    u.Name = ""persisted"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException) when (1 + 1 == 2)
                {
                    ctx.Update(u);
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtConditionalThrowExpressionBeforeReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when fail is true"";
            try
            {
                _ = fail ? throw new System.InvalidOperationException() : 0;
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtCoalesceThrowExpressionBeforeReattach_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""lost when value is null"";
            try
            {
                _ = value ?? throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_CaughtThrowExpressionInsideMutationBeforeReattach_ThenSave_DoesNotTrigger()
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
            try
            {
                u.Name = value ?? throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateThenCaughtCoalesceThrowBeforeReattach_ThenSave_StillTriggers()
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
            try
            {
                _ = ({|LC044:u.Name|} = value) ?? throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateAndCaughtThrowInSiblingSwitchExpressionArm_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, int choice)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                _ = choice switch
                {
                    0 => u.Name = ""persisted"",
                    _ => throw new System.InvalidOperationException()
                };
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateInSwitchGoverningExpressionThenCaughtArmThrow_ThenSave_StillTriggers()
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
            try
            {
                _ = ({|LC044:u.Name|} = value).Length switch
                {
                    0 => throw new System.InvalidOperationException(),
                    _ => 1
                };
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_UninvokedLambdaThrowInsideTryBeforeReattach_ThenSave_DoesNotTrigger()
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
            u.Name = ""persisted"";
            try
            {
                _ = (System.Action)(() => { throw new System.InvalidOperationException(); });
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_UninvokedLocalFunctionThrowInsideTryBeforeReattach_ThenSave_DoesNotTrigger()
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
            u.Name = ""persisted"";
            try
            {
                void NeverInvoked() { throw new System.InvalidOperationException(); }
                _ = (System.Action)NeverInvoked;
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchRethrowsToTypedOuterCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when fail is true"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    throw;
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchMayRethrowBeforeReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, bool rethrow)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when both flags are true"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    if (rethrow)
                        throw;
                    ctx.Attach(u);
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchReattachesBeforeRethrowToOuterCatch_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    u.Name = ""persisted"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    ctx.Update(u);
                    throw;
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchExplicitlyRethrowsToOuterCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when fail is true"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException ex)
                {
                    throw ex;
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchReattachesBeforeExplicitRethrow_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    u.Name = ""persisted"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException ex)
                {
                    ctx.Update(u);
                    throw ex;
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchThrowsReplacementToOuterCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when fail is true"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    throw new System.ArgumentException();
                }
            }
            catch (System.ArgumentException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchThrowsLocalReplacementToOuterCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when fail is true"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    var replacement = new System.ArgumentException();
                    throw replacement;
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchReplacesUnknownExceptionToOuterCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, System.Exception original)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when fail is true"";
                    if (fail)
                        throw original;
                    ctx.Update(u);
                }
                catch (System.Exception)
                {
                    throw new System.ArgumentException();
                }
            }
            catch (System.ArgumentException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_OrderedInnerCatchesReplaceUnknownExceptionWithSameType_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, bool first, System.Exception original)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when fail is true"";
                    if (fail)
                        throw original;
                    ctx.Update(u);
                }
                catch (System.Exception) when (first)
                {
                    throw new System.ArgumentException();
                }
                catch (System.Exception)
                {
                    throw new System.ArgumentException();
                }
            }
            catch (System.ArgumentException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchReattachesBeforeThrowingReplacement_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    u.Name = ""persisted"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    ctx.Update(u);
                    throw new System.ArgumentException();
                }
            }
            catch (System.ArgumentException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchThrowsReplacementPastIncompatibleOuterCatch_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    u.Name = ""persisted or execution terminates"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    throw new System.ArgumentException();
                }
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ExplicitRethrowSkipsSiblingReattach_ThenOuterSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost when fail is true"";
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException ex)
                {
                    throw ex;
                }
                catch (System.Exception)
                {
                    ctx.Attach(u);
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtConvertedDerivedExceptionBeforeReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when fail is true"";
            try
            {
                if (fail)
                    throw (System.Exception)new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ConvertedBaseExceptionBeforeNarrowCatchAndReattach_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted or execution terminates"";
            try
            {
                if (fail)
                    throw (System.Exception)new System.Exception();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CaughtConvertedConditionalSubtypeBeforeReattach_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, bool chooseInvalidOperation)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost on the caught subtype"";
            try
            {
                if (fail)
                    throw (System.Exception)(chooseInvalidOperation
                        ? new System.InvalidOperationException()
                        : new System.ArgumentException());
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_InnerCatchConsumesConvertedConditionalSubtype_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, bool chooseInvalidOperation)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    u.Name = ""persisted or execution terminates"";
                    if (fail)
                        throw (System.Exception)(chooseInvalidOperation
                            ? new System.InvalidOperationException()
                            : new System.ArgumentException());
                    ctx.Update(u);
                }
                catch (System.InvalidOperationException)
                {
                    ctx.Update(u);
                }
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

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
    public async Task AsNoTracking_Mutate_ContextAttach_ThenSave_StillTriggers()
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
    public async Task AsNoTracking_Mutate_DbSetAttach_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""lost"";
            ctx.Users.Attach(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateNestedMember_ContextAttachNestedEntity_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User { public int Id { get; set; } public Address Address { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Address.City|} = ""London"";
            ctx.Attach(u.Address);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttachNestedEntity_ThenMutateNestedMember_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User { public int Id { get; set; } public Address Address { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            ctx.Attach(u.Address);
            u.Address.City = ""London"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ComplementaryUpdateAndAttachBranches_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool persist)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when persist is false"";
            if (persist)
                ctx.Update(u);
            else
                ctx.Attach(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_TryUpdatesAndCatchAttaches_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                {|LC044:u.Name|} = ""lost when fail is true"";
                if (fail)
                    throw new System.InvalidOperationException();
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachMutateThenAttach_ThenSave_StillTriggers()
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
                {|LC044:u.Name|} = ""lost"";
                ctx.Attach(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ContextAttachRangeWithEntityAfterFirstArgument_ThenSave_StillTriggers()
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
            var other = new User();
            {|LC044:u.Name|} = ""lost"";
            ctx.AttachRange(other, u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttachRangeBeforeMutation_ThenSave_DoesNotTrigger()
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
            ctx.AttachRange(u);
            u.Name = ""persisted"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ContextUpdateRange_ThenSave_DoesNotTrigger()
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
            u.Name = ""persisted"";
            ctx.UpdateRange(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_DbSetAttachRange_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""lost"";
            ctx.Users.AttachRange(u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_DoWhileUpdate_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool again)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted"";
            do
            {
                ctx.Update(u);
            }
            while (again);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_WhileUpdate_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool again)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when again is false"";
            while (again)
            {
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_DoWhileBreakBeforeUpdate_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool stop)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when stop is true"";
            do
            {
                if (stop)
                    break;
                ctx.Update(u);
            }
            while (false);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_DoWhileContinueBeforeUpdate_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool skip)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when skip is true"";
            do
            {
                if (skip)
                    continue;
                ctx.Update(u);
            }
            while (false);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_DoWhileGotoPastUpdate_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool skip)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when skip is true"";
            do
            {
                if (skip)
                    goto AfterUpdate;
                ctx.Update(u);
            AfterUpdate:
                ;
            }
            while (false);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_DoWhileReturnBeforeUpdate_ThenSave_DoesNotTrigger()
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
            u.Name = ""persisted when save is reached"";
            do
            {
                if (cancel)
                    return;
                ctx.Update(u);
            }
            while (false);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_BaseTypedThrowMayReachNarrowUnsafeCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, System.Exception original)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost for an InvalidOperationException runtime value"";
            try
            {
                if (fail)
                    throw original;
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
            }
            catch (System.Exception)
            {
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_BaseTypedThrowConstantFalseNarrowCatchThenBroadUpdate_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, System.Exception original)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted"";
            try
            {
                if (fail)
                    throw original;
                ctx.Update(u);
            }
            catch (System.InvalidOperationException) when (1 + 1 == 3)
            {
            }
            catch (System.Exception)
            {
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_BaseTypedThrowFilteredNarrowUnsafeCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, bool handleNarrow, System.Exception original)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when the narrow filter accepts"";
            try
            {
                if (fail)
                    throw original;
                ctx.Update(u);
            }
            catch (System.InvalidOperationException) when (handleNarrow)
            {
            }
            catch (System.Exception)
            {
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_MixedReplacementAndSafeRethrowToOuterCatch_ThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, bool replace, System.Exception original)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    {|LC044:u.Name|} = ""lost on the replacement path"";
                    if (fail)
                        throw original;
                    ctx.Update(u);
                }
                catch (System.Exception)
                {
                    if (replace)
                        throw new System.ArgumentException();
                    ctx.Update(u);
                    throw;
                }
            }
            catch (System.ArgumentException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_BaseTypedRethrowAfterUpdateToOuterCatch_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, System.Exception original)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    u.Name = ""persisted"";
                    if (fail)
                        throw original;
                    ctx.Update(u);
                }
                catch (System.Exception)
                {
                    ctx.Update(u);
                    throw;
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_DoWhileAttachBeforeMutation_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool again)
        {
            var u = ctx.Users.AsNoTracking().First();
            do
            {
                ctx.Attach(u);
            }
            while (again);
            u.Name = ""persisted"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_DoWhileBreakBeforeAttach_ThenMutate_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool skip)
        {
            var u = ctx.Users.AsNoTracking().First();
            do
            {
                if (skip)
                    break;
                ctx.Attach(u);
            }
            while (false);
            {|LC044:u.Name|} = ""lost when skip is true"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_DoWhileContinueBeforeAttach_ThenMutate_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool skip)
        {
            var u = ctx.Users.AsNoTracking().First();
            do
            {
                if (skip)
                    continue;
                ctx.Attach(u);
            }
            while (false);
            {|LC044:u.Name|} = ""lost when skip is true"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_DoWhileGotoPastAttach_ThenMutate_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool skip)
        {
            var u = ctx.Users.AsNoTracking().First();
            do
            {
                if (skip)
                    goto AfterAttach;
                ctx.Attach(u);
            AfterAttach:
                ;
            }
            while (false);
            {|LC044:u.Name|} = ""lost when skip is true"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_DoWhileReturnBeforeAttach_ThenMutate_DoesNotTrigger()
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
            do
            {
                if (cancel)
                    return;
                ctx.Attach(u);
            }
            while (false);
            u.Name = ""persisted when save is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_DoWhileCaughtThrowBeforeAttach_ThenMutate_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                do
                {
                    if (fail)
                        throw new System.InvalidOperationException();
                    ctx.Attach(u);
                }
                while (false);
            }
            catch (System.InvalidOperationException)
            {
            }
            {|LC044:u.Name|} = ""lost when fail is true"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_SoleCatchUpdateThenCompatibleRethrow_ThenSave_DoesNotTrigger()
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
            u.Name = ""persisted"";
            try
            {
                try
                {
                    throw new System.InvalidOperationException();
                }
                catch (System.InvalidOperationException)
                {
                    ctx.Update(u);
                    throw;
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_NestedIncompatibleReplacementAlwaysTerminates_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool mutate)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                if (mutate)
                {
                    u.Name = ""never reaches save"";
                    try
                    {
                        throw new System.InvalidOperationException();
                    }
                    catch (System.InvalidOperationException)
                    {
                        throw new System.ArgumentException();
                    }
                }
            }
            catch (System.InvalidOperationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ContextAttachRangeSecondArgumentBeforeMutation_ThenSave_DoesNotTrigger()
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
            var other = new User();
            ctx.AttachRange(other, u);
            u.Name = ""persisted"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ContextUpdateRangeSecondArgument_ThenSave_DoesNotTrigger()
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
            var other = new User();
            u.Name = ""persisted"";
            ctx.UpdateRange(other, u);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_BaseTypedThrowSafeNarrowCatchThenUnsafeBroadCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail, System.Exception original)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost for a non-narrow runtime type"";
            try
            {
                if (fail)
                    throw original;
                ctx.Update(u);
            }
            catch (System.InvalidOperationException)
            {
                ctx.Update(u);
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_EntryStateAddedBeforeMutation_ThenSave_DoesNotTrigger()
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
            ctx.Entry(u).State = EntityState.Added;
            u.Name = ""persisted"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_EntryStateAdded_ThenSave_DoesNotTrigger()
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
            u.Name = ""persisted"";
            ctx.Entry(u).State = EntityState.Added;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateInTry_ThrowToCatchSave_Triggers()
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
            try
            {
                {|LC044:u.Name|} = ""lost"";
                throw new System.InvalidOperationException();
            }
            catch (System.InvalidOperationException)
            {
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateInTry_InvocationCanTransferToCatchSave_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void MaybeThrow() { }

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                {|LC044:u.Name|} = ""lost"";
                MaybeThrow();
            }
            catch (System.Exception)
            {
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateInOuterTry_UnhandledNestedThrowToOuterCatchSave_Triggers()
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
            try
            {
                {|LC044:u.Name|} = ""lost"";
                try
                {
                    throw new System.InvalidOperationException();
                }
                catch (System.ArgumentException)
                {
                }
            }
            catch (System.InvalidOperationException)
            {
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateInOuterTry_NestedThrowHandledBeforeOuterCatchSave_DoesNotTrigger()
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
            try
            {
                u.Name = ""never reaches the outer catch save"";
                try
                {
                    throw new System.InvalidOperationException();
                }
                catch (System.InvalidOperationException)
                {
                }
            }
            catch (System.InvalidOperationException)
            {
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateInTry_SaveInFinally_Triggers()
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
            try
            {
                {|LC044:u.Name|} = ""lost"";
            }
            finally
            {
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_CatchOnlyUpdateWithNormalPath_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool fail)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when fail is false"";
            try
            {
                if (fail)
                    throw new System.InvalidOperationException();
            }
            catch (System.InvalidOperationException)
            {
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachMutationOnlyOnReturningPath_DoesNotTrigger()
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
            foreach (var u in ctx.Users.AsNoTracking())
            {
                if (cancel)
                {
                    u.Name = ""never reaches save"";
                    return;
                }
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateIndexedElement_UpdateDifferentIndex_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User
    {
        public int Id { get; set; }
        public System.Collections.Generic.List<Address> Addresses { get; set; }
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Addresses[0].City|} = ""lost"";
            ctx.Update(u.Addresses[1]);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ImplicitThrowCanBypassUpdate_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void MaybeThrow() { }

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when the call throws"";
            try
            {
                MaybeThrow();
                ctx.Update(u);
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ThrowingConditionCanBypassBothBranchUpdates_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static bool ShouldUpdate() => true;

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when the condition throws"";
            try
            {
                if (ShouldUpdate())
                    ctx.Update(u);
                else
                    ctx.Update(u);
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ImplicitThrowInsideCoveredBranchCanBypassUpdate_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void MaybeThrow() { }

        public void M(TestCtx ctx, bool first)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when the first branch throws"";
            try
            {
                if (first)
                {
                    MaybeThrow();
                    ctx.Update(u);
                }
                else
                {
                    ctx.Update(u);
                }
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ImplicitThrowInsideCoveredBranchHandlerReturns_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void MaybeThrow() { }

        public void M(TestCtx ctx, bool first)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted whenever save is reached"";
            try
            {
                if (first)
                {
                    MaybeThrow();
                    ctx.Update(u);
                }
                else
                {
                    ctx.Update(u);
                }
            }
            catch (System.Exception)
            {
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ThrowingConditionHandlerReturnsOrBranchUpdates_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static bool ShouldUpdate() => true;

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted whenever save is reached"";
            try
            {
                if (ShouldUpdate())
                    ctx.Update(u);
                else
                    ctx.Update(u);
            }
            catch (System.Exception)
            {
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ThrowingPropertyGetterCanBypassUpdate_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private int Value => 0;

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when the getter throws"";
            try
            {
                _ = Value;
                ctx.Update(u);
            }
            catch (System.Exception)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ThrowingPropertyGetterHandlerReturnsOrUpdateRuns_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private int Value => 0;

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted whenever save is reached"";
            try
            {
                _ = Value;
                ctx.Update(u);
            }
            catch (System.Exception)
            {
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateIndexedElement_UpdateSameIndex_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User
    {
        public int Id { get; set; }
        public System.Collections.Generic.List<Address> Addresses { get; set; }
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Addresses[0].City = ""persisted"";
            ctx.Update(u.Addresses[0]);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateNullIndexedElement_UpdateLiteralNullMarkerIndex_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class AddressBook
    {
        public Address this[string key] => null;
    }
    public class User
    {
        public int Id { get; set; }
        public AddressBook Addresses { get; set; }
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Addresses[null].City|} = ""lost"";
            ctx.Update(u.Addresses[""<null>""]);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateNullIndexedElement_UpdateSameNullIndex_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class AddressBook
    {
        public Address this[string key] => null;
    }
    public class User
    {
        public int Id { get; set; }
        public AddressBook Addresses { get; set; }
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Addresses[null].City = ""persisted"";
            ctx.Update(u.Addresses[null]);
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateInTry_NonMatchingCatchSave_DoesNotTrigger()
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
            try
            {
                u.Name = ""never reaches this save"";
                throw new System.InvalidOperationException();
            }
            catch (System.ArgumentException)
            {
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ImplicitThrowHandlerReturnsOrUpdateRuns_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void MaybeThrow() { }

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted whenever save is reached"";
            try
            {
                MaybeThrow();
                ctx.Update(u);
            }
            catch (System.Exception)
            {
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_AlwaysThrownCatchUpdate_DoesNotTrigger()
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
            u.Name = ""persisted"";
            try
            {
                throw new System.InvalidOperationException();
            }
            catch (System.InvalidOperationException)
            {
                ctx.Update(u);
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
