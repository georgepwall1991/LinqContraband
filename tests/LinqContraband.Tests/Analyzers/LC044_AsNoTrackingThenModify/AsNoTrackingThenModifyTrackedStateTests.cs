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
    public async Task AsNoTracking_MutateNestedMember_ContextUpdateRoot_ThenSave_DoesNotTrigger()
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
            user.Address.City = ""persisted by graph update"";
            ctx.Update(user);
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
    public async Task AsNoTracking_MutateNestedMember_EntryRootModified_ThenSave_StillTriggers()
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
            {|LC044:user.Address.City|} = ""not covered by the root entry state"";
            ctx.Entry(user).State = EntityState.Modified;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateNestedMember_UpdateRootThenDetachRoot_ThenSave_DoesNotTrigger()
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
            user.Address.City = ""persisted by the still-tracked nested entity"";
            ctx.Update(user);
            ctx.Entry(user).State = EntityState.Detached;
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_UpdateRootThenDetachNestedEntity_MutateNestedMember_ThenSave_StillTriggers()
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
            ctx.Update(user);
            ctx.Entry(user.Address).State = EntityState.Detached;
            {|LC044:user.Address.City|} = ""lost after the nested entity is detached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateNestedMember_UpdateNestedEntity_SiblingDetach_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User { public int Id { get; set; } public Address Address { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var user = ctx.Users.AsNoTracking().First();
            if (flag)
            {
                user.Address.City = ""persisted on the mutation path"";
                ctx.Update(user.Address);
            }
            else
            {
                ctx.Entry(user.Address).State = EntityState.Detached;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateNestedMember_UpdateNestedEntity_SiblingTrackerClear_ThenSave_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class User { public int Id { get; set; } public Address Address { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag)
        {
            var user = ctx.Users.AsNoTracking().First();
            if (flag)
            {
                user.Address.City = ""persisted on the mutation path"";
                ctx.Update(user.Address);
            }
            else
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
    public async Task AsNoTracking_PriorAttachCanThrowIntoFallThroughCatch_MutateThenSave_StillTriggers()
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
                ctx.Attach(u);
            }
            catch (System.Exception)
            {
            }
            {|LC044:u.Name|} = ""lost if Attach throws"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_PriorEntryModifiedCanThrowIntoFallThroughCatch_MutateThenSave_StillTriggers()
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
                ctx.Entry(u).State = EntityState.Modified;
            }
            catch (System.Exception)
            {
            }
            {|LC044:u.Name|} = ""lost if Entry or State evaluation throws"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachPriorAttachCanThrowIntoFallThroughCatch_MutateThenSave_StillTriggers()
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
                try
                {
                    ctx.Attach(u);
                }
                catch (System.Exception)
                {
                }
                {|LC044:u.Name|} = ""lost if Attach throws"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_PriorAttachCanThrowIntoTerminatingCatch_MutateThenSave_DoesNotTrigger()
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
                ctx.Attach(u);
            }
            catch (System.Exception)
            {
                return;
            }
            u.Name = ""persisted whenever save is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_OptionalCatchOnlyPriorAttach_MutateThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Risk() { }

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                Risk();
            }
            catch (System.Exception)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost on the normal try path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachOptionalCatchOnlyPriorEntryModified_MutateThenSave_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Risk() { }

        public void M(TestCtx ctx)
        {
            foreach (var u in ctx.Users.AsNoTracking())
            {
                try
                {
                    Risk();
                }
                catch (System.Exception)
                {
                    ctx.Entry(u).State = EntityState.Modified;
                }
                {|LC044:u.Name|} = ""lost on the normal try path"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_MutateThenSave_DoesNotTrigger()
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
                throw new System.InvalidOperationException();
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever save is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_AlternateFallThroughCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Risk() { }

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                Risk();
                throw new System.InvalidOperationException();
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost on the alternate catch path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachMandatoryCatchPriorEntry_AlternateFallThroughCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Risk() { }

        public void M(TestCtx ctx)
        {
            foreach (var u in ctx.Users.AsNoTracking())
            {
                try
                {
                    Risk();
                    throw new System.InvalidOperationException();
                }
                catch (System.ArgumentException)
                {
                }
                catch (System.InvalidOperationException)
                {
                    ctx.Entry(u).State = EntityState.Modified;
                }
                {|LC044:u.Name|} = ""lost on the alternate catch path"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_AlternateCatchReturns_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Risk() { }

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                Risk();
                throw new System.InvalidOperationException();
            }
            catch (System.ArgumentException)
            {
                return;
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever save is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ThrowFactoryCanReachAlternateCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static System.InvalidOperationException CreateException() => new System.InvalidOperationException();

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw CreateException();
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the throw factory reaches the alternate catch"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ThrowConstructorCanReachAlternateCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class CustomException : System.InvalidOperationException
    {
        public CustomException()
        {
            throw new System.ArgumentException();
        }
    }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw new CustomException();
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when construction reaches the alternate catch"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NestedMetadataConstructorCanReachAlternateCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, int count)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw new System.InvalidOperationException(new string('x', count));
            }
            catch (System.ArgumentOutOfRangeException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when nested metadata construction reaches the alternate catch"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UserDefinedSystemNamespaceConstructorCanReachAlternateCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace System.Custom
{
    public sealed class UserException : Exception
    {
        public UserException() => throw new ArgumentException();
    }
}
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
                throw new System.Custom.UserException();
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.Custom.UserException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the source constructor reaches the alternate catch"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ParenthesizedSystemException_DoesNotTrigger()
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
                throw (new System.InvalidOperationException());
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ParenthesizedNestedSystemException_DoesNotTrigger()
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
                throw (new System.IO.IOException());
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.IO.IOException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_FieldOperandCannotReachAlternateCatch_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class Holder { public System.InvalidOperationException Exception; }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, Holder holder)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw holder.Exception;
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted on every path that reaches the mutation"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NullableThrowOperandCanReachAlternateCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, System.InvalidOperationException exception)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when a null operand reaches the alternate catch"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NullOperandCatchAlsoAttaches_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, System.InvalidOperationException exception)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
                ctx.Attach(u);
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted on every reachable handler path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_CoalescedThrowExpressionCannotProduceNull_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, System.InvalidOperationException exception)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw exception ?? throw new System.InvalidOperationException();
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_DefinitelyNonNullLocalThrowOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ReassignedLocalThrowOperandCanBeNull_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            exception = null;
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the reassigned operand reaches the null handler"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_LocalWriteAfterThrowDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
                exception = null;
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UninvokedLambdaLocalWriteDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action overwrite = () => exception = null;
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvokedLambdaCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action overwrite = () => exception = null;
            overwrite();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the invoked lambda nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvokedLocalFunctionCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            void Overwrite() => exception = null;
            Overwrite();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the invoked local function nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_LaterDeclaredInvokedLocalFunctionCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            Overwrite();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            void Overwrite() => exception = null;
            {|LC044:u.Name|} = ""lost when the later-declared invoked local function nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ExplicitDelegateInvokeCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action overwrite = () => exception = null;
            overwrite.Invoke();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the explicitly invoked delegate nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_AssignedLambdaCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action overwrite;
            overwrite = () => exception = null;
            overwrite();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the assigned invoked lambda nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConstructedDelegateCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = new System.Action(() => exception = null);
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the constructed delegate nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ChainedAssignedLambdaCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action first;
            System.Action second;
            first = second = () => exception = null;
            first.Invoke();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the chained assigned lambda nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UninvokedInnerLambdaWriteDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action outer = () =>
            {
                System.Action inner = () => exception = null;
            };
            outer();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_RemovedLambdaWriteDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            handler -= () => exception = null;
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_SkippedCoalescingLambdaWriteDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            handler ??= () => exception = null;
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvocationBeforeLambdaAssignmentDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            handler();
            handler = () => exception = null;
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_OverwrittenLambdaDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler = () => { };
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_OptionalLambdaAssignmentMayNullOperand_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool install)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            if (install)
            {
                handler = () => exception = null;
            }
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the optional assignment installs the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_OptionalLambdaInvocationMayNullOperand_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool invoke)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            if (invoke)
            {
                handler();
            }
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the optional invocation runs the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_FinallyInvokedLambdaCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            try
            {
            }
            finally
            {
                handler();
            }
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the finally-invoked lambda nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_GotoSkippedLambdaInvocationDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
#pragma warning disable CS0162
            goto AfterInvoke;
            handler();
#pragma warning restore CS0162
        AfterInvoke:
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConditionalGotoMayInvokeLambda_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            if (skip)
                goto AfterInvoke;
            handler();
        AfterInvoke:
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the non-skipping path invokes the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_AdditiveLambdaCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            handler += () => exception = null;
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the additive lambda nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_RetainedAdditiveLambdaCanNullLocalOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler += () => { };
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the retained additive lambda nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvokedLocalReplacementRemovesLambdaWrite_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            void Replace() => handler = () => { };
            Replace();
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConditionalLocalReplacementMayPreserveLambda_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool replace)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            void Replace()
            {
                if (replace)
                    handler = () => { };
            }
            Replace();
            handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the conditional replacement does not run"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_SelfAssignedDelegateRetainsLambdaWrite_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler = handler;
            handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when self-assignment retains the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UnmatchedRemovalRetainsLambdaWrite_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler -= () => { };
            handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when unmatched removal retains the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NonMutatingRefCallRetainsLambdaWrite_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Inspect(ref System.Action handler) { }
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            Inspect(ref handler);
            handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the ref call retains the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvocationPathReturnsBeforeThrow_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            if (stop)
            {
                handler();
                return;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConditionalAccessInvocationMayNullOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler?.Invoke();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when conditional access invokes the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NestedInvokedDelegateCanNullOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action outer = () =>
            {
                System.Action inner = () => exception = null;
                inner();
            };
            outer();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the nested delegate invokes the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_LocalMemberWriteDoesNotReassignOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            exception.Data[""key""] = ""value"";
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConditionalSelfPreservingReplacementMayRetainWriter_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool replace)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler = replace ? () => { } : handler;
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the original writer is retained"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_CaughtThrowMaySkipNestedReplacement_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            void Replace()
            {
                try
                {
                    MaybeThrow();
                    handler = () => { };
                }
                catch (System.Exception)
                {
                }
            }
            Replace();
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the nested replacement is skipped"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConditionalInitializerMayInstallWriter_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool install)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = install ? () => exception = null : () => { };
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the conditional installs the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_SwitchInitializerMayInstallWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = choice switch
            {
                0 => () => exception = null,
                _ => () => { },
            };
            handler();
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when the switch installs the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_CaughtThrowAfterInvocationStillReachesOperand_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            try
            {
                handler();
                throw new System.InvalidOperationException();
            }
            catch (System.InvalidOperationException)
            {
            }
            try
            {
                throw exception;
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost after the locally caught throw"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_RecursiveDelegateWriter_StillTriggersWithoutRecursingAnalyzer()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = null;
            handler = () =>
            {
                exception = null;
                handler();
            };
            handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the recursive writer nulls the operand"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ThrowingNestedReplacementFactoryMayRetainWriter_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static System.Action CreateNoop() => () => { };

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            void Replace()
            {
                try { handler = CreateNoop(); }
                catch (System.Exception) { }
            }
            Replace();
            handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the replacement factory throws"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_AliasedDelegateWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => exception = null;
            System.Action alias = writer;
            alias();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the alias invokes the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_MatchedSelfRemovalEliminatesWriter_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler -= handler;
            handler?.Invoke();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_MutuallyExclusiveInstallAndInvoke_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool install)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            if (install) handler = () => exception = null;
            if (!install) handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted on every feasible path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_TerminatingTryAfterInvocation_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler();
            try { throw new System.InvalidOperationException(); }
            catch (System.InvalidOperationException) { return; }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""unreachable"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_EarlierTerminatingCatchInterceptsThrow_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            var terminal = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler();
            try { throw terminal; }
            catch (System.InvalidOperationException) { return; }
            catch (System.Exception) { }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""unreachable after the intercepted throw"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConditionalNestedConstructorsRemainOpen_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool first)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw first
                    ? new System.InvalidOperationException()
                    : new System.InvalidOperationException();
            }
            catch (System.NullReferenceException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost if a nested constructor reaches the alternate catch"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_StaticFieldTypeInitializerCanReachAlternateCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public static class Holder
    {
        public static readonly System.InvalidOperationException Exception;
        static Holder() => throw new System.Exception();
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw Holder.Exception;
            }
            catch (System.TypeInitializationException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            {|LC044:u.Name|} = ""lost when type initialization reaches the alternate catch"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_StaticFieldCannotReachIncompatibleAlternateCatch_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public static class Holder
    {
        public static readonly System.InvalidOperationException Exception = new();
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw Holder.Exception;
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConstantFieldOperandCannotRunTypeInitializer_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public static class Holder { public const string Message = ""safe""; }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw new System.InvalidOperationException(Holder.Message);
            }
            catch (System.TypeInitializationException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever the mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_EarlierConstantTrueCatchInterceptsStaticFieldFailure_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public static class Holder
    {
        public static readonly System.InvalidOperationException Exception;
        static Holder() => throw new System.Exception();
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                throw Holder.Exception;
            }
            catch (System.TypeInitializationException) when (true)
            {
                ctx.Attach(u);
            }
            catch (System.TypeInitializationException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted on every reachable handler path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_AlternateCatchAlsoAttaches_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Risk() { }

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                Risk();
                throw new System.InvalidOperationException();
            }
            catch (System.ArgumentException)
            {
                ctx.Attach(u);
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted on every handler path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NestedCatchSwallowsEarlierThrow_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Risk() { }

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                try
                {
                    Risk();
                }
                catch (System.Exception)
                {
                }
                throw new System.InvalidOperationException();
            }
            catch (System.ArgumentException)
            {
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted after the nested catch swallows the earlier throw"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_PriorTryAndCatchBothAttach_MutateThenSave_DoesNotTrigger()
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
                ctx.Attach(u);
            }
            catch (System.Exception)
            {
                ctx.Attach(u);
            }
            u.Name = ""persisted whenever mutation is reached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ForeachPriorTryAndCatchBothUpdate_MutateThenSave_DoesNotTrigger()
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
                try
                {
                    ctx.Update(u);
                }
                catch (System.Exception)
                {
                    ctx.Update(u);
                }
                u.Name = ""persisted whenever mutation is reached"";
            }
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
            }
            catch (System.ArgumentException)
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
    public async Task AsNoTracking_Mutate_InnerCatchReattachesBeforeOuterCatch_ThenSave_StillTriggers()
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
                    {|LC044:u.Name|} = ""lost if the catch-side Update throws"";
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
    public async Task AsNoTracking_Mutate_ConstantTrueInnerCatchReattachesBeforeOuterCatch_ThenSave_StillTriggers()
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
                    {|LC044:u.Name|} = ""lost if the catch-side Update throws"";
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
    public async Task AsNoTracking_Mutate_InnerCatchReattachesBeforeRethrowToOuterCatch_ThenSave_StillTriggers()
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
                    {|LC044:u.Name|} = ""lost if the catch-side Update throws"";
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
    public async Task AsNoTracking_Mutate_InnerCatchReattachesBeforeExplicitRethrow_ThenSave_StillTriggers()
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
                    {|LC044:u.Name|} = ""lost if the catch-side Update throws"";
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
    public async Task AsNoTracking_Mutate_InnerCatchReattachesBeforeThrowingReplacement_ThenSave_StillTriggers()
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
                    {|LC044:u.Name|} = ""lost if the catch-side Update throws"";
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
    public async Task AsNoTracking_Mutate_InnerCatchConsumesConvertedConditionalSubtype_ThenSave_StillTriggers()
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
                    {|LC044:u.Name|} = ""lost if the catch-side Update throws"";
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
                ctx.Update(u);
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
    public async Task AsNoTracking_Mutate_SoleCatchUpdateThenCompatibleRethrow_ThenSave_StillTriggers()
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
            {|LC044:u.Name|} = ""lost if the catch-side Update throws"";
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
    public async Task AsNoTracking_Mutate_UpdateCanThrowIntoFallThroughCatch_StillTriggers()
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
            {|LC044:u.Name|} = ""lost when Update throws before tracking"";
            try
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
    public async Task AsNoTracking_Mutate_CollectiveCatchUpdateCanThrowIntoOuterCatch_StillTriggers()
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
            {|LC044:u.Name|} = ""lost if both Update calls throw"";
            try
            {
                try
                {
                    ctx.Update(u);
                }
                catch (System.Exception)
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
    public async Task AsNoTracking_Mutate_CollectiveCatchUpdateOuterHandlerReturns_DoesNotTrigger()
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
            u.Name = ""persisted whenever save is reached"";
            try
            {
                try
                {
                    ctx.Update(u);
                }
                catch (System.Exception)
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
    public async Task AsNoTracking_Mutate_ImplicitThrowHandledInsideCoveredBranchBeforeUpdate_DoesNotTrigger()
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
            u.Name = ""persisted"";
            if (first)
            {
                try
                {
                    MaybeThrow();
                }
                catch (System.Exception)
                {
                }
                ctx.Update(u);
            }
            else
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
    public async Task AsNoTracking_Mutate_ImplicitThrowHandledInsideCoveredBranchWithinOuterCatch_StillTriggers()
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
            {|LC044:u.Name|} = ""lost if a branch Update throws"";
            try
            {
                if (first)
                {
                    try
                    {
                        MaybeThrow();
                    }
                    catch (System.Exception)
                    {
                    }
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
    public async Task AsNoTracking_Mutate_ImplicitThrowNotFullyHandledInsideCoveredBranch_StillTriggers()
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
            {|LC044:u.Name|} = ""lost when another implicit exception reaches the outer catch"";
            try
            {
                if (first)
                {
                    try
                    {
                        MaybeThrow();
                    }
                    catch (System.InvalidOperationException)
                    {
                    }
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
    public async Task AsNoTracking_Mutate_InstanceFieldReadCanBypassUpdate_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class Holder { public int Value = 0; }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, Holder holder)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when the field receiver is null"";
            try
            {
                _ = holder.Value;
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
    public async Task AsNoTracking_Mutate_CurrentInstanceFieldReadBeforeUpdate_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private int value = 0;

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted"";
            try
            {
                _ = value;
            }
            catch (System.Exception)
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
    public async Task AsNoTracking_Mutate_ConstantFieldBeforeUpdateThatCanThrow_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public static class Holder { public const int Value = 1; }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost if Update itself throws into the handler"";
            try
            {
                _ = Holder.Value;
                ctx.Update(u);
            }
            catch (System.TypeInitializationException)
            {
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_Mutate_ConditionalInstanceFieldReadBeforeUpdate_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class Holder { public int Value = 0; }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, Holder holder)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted"";
            try
            {
                _ = holder?.Value;
            }
            catch (System.Exception)
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
    public async Task AsNoTracking_Mutate_ImplicitThrowInSiblingBranchBeforeUpdate_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void MaybeThrow() { }

        public void M(TestCtx ctx, bool mutate)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                if (mutate)
                {
                    u.Name = ""persisted"";
                }
                else
                {
                    MaybeThrow();
                }
            }
            catch (System.Exception)
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
    public async Task AsNoTracking_Mutate_NestedBlockReattachInCoveredBranch_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool first)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted"";
            if (first)
            {
                {
                    ctx.Update(u);
                }
            }
            else
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
    public async Task AsNoTracking_Mutate_MandatoryDoReattachInCoveredBranch_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool first)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted"";
            if (first)
            {
                do
                {
                    ctx.Update(u);
                }
                while (false);
            }
            else
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
    public async Task AsNoTracking_Mutate_NestedExhaustiveBranchReattach_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool first, bool second)
        {
            var u = ctx.Users.AsNoTracking().First();
            u.Name = ""persisted"";
            if (first)
            {
                if (second)
                    ctx.Update(u);
                else
                    ctx.Update(u);
            }
            else
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
    public async Task AsNoTracking_Mutate_NestedOptionalBranchReattach_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool first, bool second)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when both conditions are true"";
            if (first)
            {
                if (second)
                    ctx.Update(u);
            }
            else
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
    public async Task AsNoTracking_Mutate_NestedThrowCanReachOuterCatchBeforeReattach_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool first, bool second)
        {
            var u = ctx.Users.AsNoTracking().First();
            {|LC044:u.Name|} = ""lost when the nested throw reaches the handler"";
            try
            {
                if (first)
                {
                    if (second)
                        throw new System.InvalidOperationException();
                    else
                        ctx.Update(u);
                }
                else
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
    public async Task AsNoTracking_Mutate_BranchThrowCatchReattaches_DoesNotTrigger()
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
            u.Name = ""persisted"";
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
                ctx.Update(u);
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

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_LaterLoopAssignmentCanRunOnNextIteration_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 2; i++)
            {
                handler();
                handler = () => exception = null;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost on the second loop iteration"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ForIteratorCanInstallWriterForNextIteration_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 2; i++, handler = () => exception = null)
            {
                handler();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the iterator installs the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_FinalForIteratorInstallationDoesNotRunWriter_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the writer is installed after the final invocation"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_OverflowingForCounterCanReachInstalledWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            var iterations = 0;
            for (var i = int.MaxValue; i <= int.MaxValue; i++, handler = () => exception = null)
            {
                handler();
                if (++iterations == 2) break;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the counter wraps and the writer runs"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvocationBranchBreakSkipsForIterator_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool run)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 2; i++, handler = () => exception = null)
            {
                if (run)
                {
                    handler();
                    break;
                }
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because every invocation skips the iterator"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_CaughtThrowStillReachesForIterator_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 2; i++, handler = () => exception = null)
            {
                try
                {
                    handler();
                    throw new System.InvalidOperationException();
                }
                catch (System.InvalidOperationException) { }
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the caught throw reaches the iterator"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_TupleCounterWriteCanReachInstalledWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            var iterations = 0;
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                if (++iterations == 2) break;
                (i, _) = (-1, 0);
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the tuple write repeats the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_RefAliasCounterWriteCanReachInstalledWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            var iterations = 0;
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                ref var alias = ref i;
                handler();
                if (++iterations == 2) break;
                alias = -1;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the ref alias write repeats the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ContinueSkippedLoopAssignmentDoesNotInstallWriter_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool run)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            while (run)
            {
                handler();
                continue;
#pragma warning disable CS0162
                handler = () => exception = null;
#pragma warning restore CS0162
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the writer is unreachable"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NestedGuaranteedBreakAfterInstallationDoesNotRunWriter_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool run, bool first)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            while (run)
            {
                handler();
                handler = () => exception = null;
                if (first) break;
                else break;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the loop always exits after installation"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvokedLocalChangesGuardBeforeInvoke_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool install)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            if (install) handler = () => exception = null;
            void ClearGuard() => install = false;
            ClearGuard();
            if (!install) handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the invoked guard mutation"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_OuterGuardsMakeNestedInstallAndInvokeExclusive_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool install, bool enabled)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            if (install)
            {
                if (enabled) handler = () => exception = null;
            }
            if (!install) handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted on every feasible path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_PreGuardNestedWriteKeepsInverseGuardsExclusive_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool enabled)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            Disable();
            if (enabled) handler = () => exception = null;
            void Disable() => enabled = false;
            if (!enabled) handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the nested write ran before the first guard"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConditionalThrowCanEscapeTerminatingTry_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool resume, System.ArgumentException transfer)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler();
            try
            {
                if (resume) throw transfer;
                return;
            }
            catch (System.ArgumentException) { }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the explicit throw reaches its handler"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NullOperandBypassesTerminatingDeclaredCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, System.InvalidOperationException transfer)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            handler();
            try { throw transfer; }
            catch (System.InvalidOperationException) { return; }
            catch (System.NullReferenceException) { }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the nullable throw operand takes its NRE channel"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ThrowingTupleReplacementMayRetainWriter_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static System.Action CreateReplacement() =>
            throw new System.InvalidOperationException();

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => exception = null;
            try { (handler, _) = (CreateReplacement(), 0); }
            catch (System.InvalidOperationException) { }
            handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the tuple replacement does not complete"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_GotoTailStillReachesForIterator_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 2; i++, handler = () => exception = null)
            {
                handler();
                goto tail;
            tail:
                System.Console.WriteLine(i);
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the local goto reaches the iterator"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_GotoOutsideLoopSkipsForIterator_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 2; i++, handler = () => exception = null)
            {
                handler();
                goto done;
            }
        done:
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the goto skips the iterator"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_SingleStatementLoopReachesForIterator_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 2; i++, handler = () => exception = null)
                handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the iterator installs the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_IncrementorInvokedCounterWriterCanRepeatLoop_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            System.Action reset = () => { };
            var iterations = 0;
            for (var i = 0; i < 1;
                 i++, handler = () => exception = null, reset())
            {
                handler();
                if (++iterations == 2) break;
                reset = () => i = -1;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the incrementor invokes the counter writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_IncrementorInvokesBeforeCounterWriterInstall_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            System.Action reset = () => { };
            for (var i = 0; i < 1;
                 i++, handler = () => exception = null, reset(), reset = () => i = -1)
            {
                handler();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the incrementor invoked the old binding"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_IncrementorInvokedReplacedCounterWriterDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            System.Action reset = () => { };
            for (var i = 0; i < 1;
                 i++, handler = () => exception = null, reset())
            {
                handler();
                reset = () => i = -1;
                reset = () => { };
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the counter writer was replaced before the incrementor"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_IncompatibleInnerCatchDoesNotReachForIterator_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            try
            {
                for (var i = 0; i < 2; i++, handler = () => exception = null)
                {
                    try
                    {
                        handler();
                        throw new System.InvalidOperationException();
                    }
                    catch (System.ArgumentException) { }
                }
            }
            catch (System.InvalidOperationException) { }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the incompatible catch cannot resume the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_FalseFilterDoesNotReachForIterator_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            try
            {
                for (var i = 0; i < 2; i++, handler = () => exception = null)
                {
                    try
                    {
                        handler();
                        throw new System.InvalidOperationException();
                    }
                    catch (System.InvalidOperationException) when (1 + 1 == 3) { }
                }
            }
            catch (System.InvalidOperationException) { }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the false filter cannot resume the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_CounterUsedInAssignmentTargetDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            var items = new int[1];
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                items[i] = i;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because indexing does not write the counter"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UninvokedCounterWriterDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                void ResetCounter() => i = -1;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the counter writer is never invoked"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConstantFalseGuardedCounterWriterDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                void ResetCounter() => i = -1;
#pragma warning disable CS0162
                if (false) ResetCounter();
#pragma warning restore CS0162
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the counter writer is unreachable"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConstantTrueGuardedCounterWriterCanRepeatLoop_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            var iterations = 0;
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                if (++iterations == 2) break;
                void ResetCounter() => i = -1;
                if (true) ResetCounter();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the reachable guarded writer repeats the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvokedCounterWriterCanReachInstalledWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            var iterations = 0;
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                if (++iterations == 2) break;
                void ResetCounter() => i = -1;
                ResetCounter();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the invoked counter writer repeats the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_LaterAssignedCounterWriterDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            System.Action reset = () => { };
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                reset();
                reset = () => i = -1;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the counter writer was not installed when invoked"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_EarlierAssignedCounterWriterCanRepeatLoop_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            System.Action reset = () => { };
            var iterations = 0;
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                if (++iterations == 2) break;
                reset = () => i = -1;
                reset();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the installed counter writer repeats the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ReplacedCounterWriterDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            System.Action reset;
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                reset = () => i = -1;
                reset = () => { };
                reset();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the counter writer was replaced before invocation"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_TransitivelyInvokedCounterWriterCanReachInstalledWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            var iterations = 0;
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                if (++iterations == 2) break;
                void ResetCounter() => i = -1;
                void Outer() => ResetCounter();
                Outer();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the transitive counter writer repeats the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InvokedNestedRefAliasCounterWriterCanRepeatLoop_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            var iterations = 0;
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                if (++iterations == 2) break;
                void ResetCounter()
                {
                    ref var alias = ref i;
                    alias = -1;
                }
                ResetCounter();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the invoked nested ref alias repeats the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UninvokedNestedRefAliasCounterWriterDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                void ResetCounter()
                {
                    ref var alias = ref i;
                    alias = -1;
                }
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the nested alias writer is uninvoked"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_JumpSkippedTransitiveCounterWriterDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                void ResetCounter() => i = -1;
                void Outer()
                {
                    goto done;
#pragma warning disable CS0162
                    ResetCounter();
#pragma warning restore CS0162
                done:
                    return;
                }
                Outer();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the transitive writer is jump skipped"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_InCounterArgumentDoesNotRepeatLoop_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        private static void Consume(in int value) => System.Console.WriteLine(value);

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                handler();
                Consume(in i);
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because an in argument cannot write the counter"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ReadOnlyRefAliasDoesNotRepeatLoop_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = 0; i < 1; i++, handler = () => exception = null)
            {
                ref var alias = ref i;
                handler();
                System.Console.WriteLine(alias);
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the ref alias is read only"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_MaxValueIncrementLeavesLoopAfterWrap_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = int.MaxValue; i >= 0; i++, handler = () => exception = null)
            {
                handler();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because wrapping exits the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_MinValueDecrementLeavesLoopAfterWrap_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (var i = int.MinValue; i <= 0; i--, handler = () => exception = null)
            {
                handler();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because underflow exits the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UnsignedMaxValueIncrementLeavesLoopAfterWrap_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            for (uint i = uint.MaxValue; i > 0; i++, handler = () => exception = null)
            {
                handler();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because unsigned wrapping exits the loop"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_CheckedOverflowStopsBeforeNextIteration_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            try
            {
                checked
                {
                    for (var i = int.MaxValue; i <= int.MaxValue; handler = () => exception = null, i++)
                    {
                        handler();
                    }
                }
            }
            catch (System.OverflowException) { }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because checked overflow stops before another invocation"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_LoopCarriedDelegateAliasInvokesPriorIterationWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost after the carried writer runs"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_SingleIterationCannotCarryDelegateAlias_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 1; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because no back-edge exists"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NextIterationReplacementBeforeAlias_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                writer = () => { };
                alias = writer;
                alias();
                writer = () => exception = null;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the carried writer is replaced first"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_TrailingReplacementPreventsLoopCarriedAlias_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
                writer = () => { };
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the dangerous writer is replaced before the back-edge"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConstantFalseGuardedLocalFunctionDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            void Overwrite() => exception = null;
#pragma warning disable CS0162
            if (false) Overwrite();
#pragma warning restore CS0162
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the local function is unreachable"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConstantTrueElseNestedWriteKeepsInverseGuardsExclusive_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool enabled)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action handler = () => { };
            if (enabled) handler = () => exception = null;
            void Disable() => enabled = false;
#pragma warning disable CS0162
            if (true) { } else Disable();
#pragma warning restore CS0162
            if (!enabled) handler();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because inverse guards remain exclusive"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConditionalDelegateAliasCanInvokeWriter_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool chooseWriter)
        {
            var u = ctx.Users.AsNoTracking().First();
            var exception = new System.InvalidOperationException();
            System.Action writer = () => exception = null;
            System.Action noop = () => { };
            System.Action alias = chooseWriter ? writer : noop;
            alias();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost when the conditional alias selects the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ConstantFalseConditionalAliasCannotInvokeWriter_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => exception = null;
            System.Action noop = () => { };
#pragma warning disable CS0429
            System.Action alias = false ? writer : noop;
#pragma warning restore CS0429
            alias();
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the writer arm is unreachable"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_FalseAndGuardedLocalFunctionDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            bool Overwrite() { exception = null; return true; }
#pragma warning disable CS0429
            _ = false && Overwrite();
#pragma warning restore CS0429
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because short-circuit and skips the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_TrueOrGuardedLocalFunctionDoesNotChangeOperand_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            bool Overwrite() { exception = null; return false; }
#pragma warning disable CS0429
            _ = true || Overwrite();
#pragma warning restore CS0429
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because short-circuit or skips the writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UncalledCounterWriterDoesNotBlockLoopCarriedAlias_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
                void NeverCalled() => i = 42;
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost because the uncalled writer cannot stop the back-edge"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_CalledCounterWriterBlocksLoopCarriedAlias_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
                void StopLoop() => i = 42;
                StopLoop();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the called writer prevents another invocation"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ImmediatelyInvokedLambdaCallsCounterWriter_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
                void StopLoop() => i = 42;
                ((System.Action)(() => StopLoop()))();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because the invoked lambda calls the counter writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_ExplicitInvokeLambdaCallsCounterWriter_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
                void StopLoop() => i = 42;
                ((System.Action)(() => StopLoop())).Invoke();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because explicit Invoke calls the counter writer"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_NullForgivingLambdaCallsCounterWriter_DoesNotTrigger()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
                void StopLoop() => i = 42;
                ((System.Action)(() => StopLoop()))!();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            u.Name = ""persisted because null forgiveness preserves delegate invocation"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MandatoryCatchPriorAttach_UninvokedLambdaCallsCounterWriter_StillTriggers()
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
            var exception = new System.InvalidOperationException();
            System.Action writer = () => { };
            System.Action alias = () => { };
            for (var i = 0; i < 2; i++)
            {
                alias = writer;
                alias();
                writer = () => exception = null;
                void NeverCalled() => i = 42;
                System.Action wrapper = () => NeverCalled();
            }
            try { throw exception; }
            catch (System.NullReferenceException) { }
            catch (System.InvalidOperationException) { ctx.Attach(u); }
            {|LC044:u.Name|} = ""lost because the wrapper is never invoked"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateThenThrowOperandFailsIntoSavingCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class CustomException : System.Exception
    {
        public CustomException(int value) { }
    }
    public class C
    {
        private static int Risk() => 0;

        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                {|LC044:u.Name|} = ""lost when operand evaluation reaches the saving catch"";
                throw new CustomException(Risk());
            }
            catch (System.InvalidOperationException)
            {
                ctx.SaveChanges();
            }
            catch (CustomException)
            {
                ctx.Attach(u);
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateThenThrowExpressionOperandFailsIntoSavingCatch_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class CustomException : System.Exception
    {
        public CustomException(int value) { }
    }
    public class C
    {
        private static int Risk() => 0;

        public void M(TestCtx ctx, int? value)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                {|LC044:u.Name|} = ""lost when throw-expression operand evaluation reaches the saving catch"";
                _ = value ?? throw new CustomException(Risk());
            }
            catch (System.InvalidOperationException)
            {
                ctx.SaveChanges();
            }
            catch (CustomException)
            {
                ctx.Attach(u);
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_MutateThenSystemThrowExpressionCannotReachSavingCatch_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, int? value)
        {
            var u = ctx.Users.AsNoTracking().First();
            try
            {
                u.Name = ""persisted because the exact throw-expression catch attaches"";
                _ = value ?? throw new System.InvalidOperationException();
            }
            catch (System.ArgumentException)
            {
                ctx.SaveChanges();
            }
            catch (System.InvalidOperationException)
            {
                ctx.Attach(u);
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_UpdateConcreteNavigationThenDetachInterfaceNavigation_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public interface IHasAddress { Address Address { get; } }
    public class User : IHasAddress
    {
        public int Id { get; set; }
        public Address Address { get; set; }
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            ctx.Update(user.Address);
            ctx.Entry(((IHasAddress)user).Address).State = EntityState.Detached;
            {|LC044:user.Address.City|} = ""lost after the equivalent interface path is detached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_UpdateOneInterfaceNavigationThenDetachSiblingInterfaceNavigation_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public interface IFirstAddress { Address Address { get; } }
    public interface ISecondAddress { Address Address { get; } }
    public class User : IFirstAddress, ISecondAddress
    {
        public int Id { get; set; }
        public Address Address { get; set; }
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            ctx.Update(((IFirstAddress)user).Address);
            ctx.Entry(((ISecondAddress)user).Address).State = EntityState.Detached;
            {|LC044:((IFirstAddress)user).Address.City|} = ""lost after the same implementation is detached through another interface"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_UpdateOneExplicitInterfaceNavigationThenDetachDistinctInterfaceNavigation_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public interface IFirstAddress { Address Address { get; } }
    public interface ISecondAddress { Address Address { get; } }
    public class User : IFirstAddress, ISecondAddress
    {
        public int Id { get; set; }
        public Address FirstAddress { get; set; }
        public Address SecondAddress { get; set; }
        Address IFirstAddress.Address => FirstAddress;
        Address ISecondAddress.Address => SecondAddress;
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            ctx.Update(((IFirstAddress)user).Address);
            ctx.Entry(((ISecondAddress)user).Address).State = EntityState.Detached;
            ((IFirstAddress)user).Address.City = ""persisted through the independently tracked interface path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_UpdateOverrideNavigationThenDetachBaseNavigation_StillTriggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public class BaseUser { public virtual Address Address { get; set; } }
    public class User : BaseUser
    {
        public int Id { get; set; }
        public override Address Address { get; set; }
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            ctx.Update(user.Address);
            ctx.Entry(((BaseUser)user).Address).State = EntityState.Detached;
            {|LC044:user.Address.City|} = ""lost after the equivalent base path is detached"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_UpdateReimplementedInterfaceNavigationThenDetachHiddenBaseNavigation_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class Address { public string City { get; set; } }
    public interface IHasAddress { Address Address { get; } }
    public class BaseUser : IHasAddress { public Address Address { get; set; } }
    public class User : BaseUser, IHasAddress
    {
        public int Id { get; set; }
        public new Address Address { get; set; }
    }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var user = ctx.Users.AsNoTracking().First();
            ctx.Update(((IHasAddress)user).Address);
            ctx.Entry(((BaseUser)user).Address).State = EntityState.Detached;
            ((IHasAddress)user).Address.City = ""persisted through the reimplemented interface path"";
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
