using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC044_AsNoTrackingThenModify.AsNoTrackingThenModifyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC044_AsNoTrackingThenModify;

public class AsNoTrackingThenModifyReachabilityTests
{
    private const string EfCoreMock = AsNoTrackingThenModifyAnalyzerTests.EfCoreMock;

    private const string Preamble = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;";

    [Fact]
    public async Task MutationInsideIfBlock_ThenSave_Triggers()
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
                {|LC044:u.Name|} = ""mutated inside a nested block"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideElseBlock_ThenSave_Triggers()
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
                // no mutation here
            }
            else
            {
                {|LC044:u.Name|} = ""mutated in else"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideUsingBlock_ThenSave_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            using (var stream = new System.IO.MemoryStream())
            {
                {|LC044:u.Name|} = ""mutated inside using"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideWhileBlock_ThenSave_Triggers()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            while (System.DateTime.Now.Ticks % 2 == 0)
            {
                {|LC044:u.Name|} = ""mutated inside while"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideIfBlockWithEarlyReturn_DoesNotTrigger()
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
                u.Name = ""mutated but branch returns"";
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachOverAsNoTrackingQueryable_MutateLoopVar_ThenSave_Triggers()
    {
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
                {|LC044:u.Name|} = ""x"";
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachLoopVar_MutatedInNestedIf_ThenSave_Triggers()
    {
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
                if (u.Id > 0)
                {
                    {|LC044:u.Name|} = ""nested if inside foreach"";
                }
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideNestedIf_WithIntermediateReturn_DoesNotTrigger()
    {
        // The mutation is inside an inner if; the outer if body returns before the save.
        // Every path that performs the mutation exits the method, so LC044 must stay quiet.
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx, bool flag, bool cond)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (flag)
            {
                if (cond)
                {
                    u.Name = ""mutated but outer branch returns"";
                }
                return;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideNestedBlockThenThrow_DoesNotTrigger()
    {
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            if (u.Name == null)
            {
                u.Name = ""will throw before save"";
                throw new System.InvalidOperationException();
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideIfBlockWithBreak_DoesNotTrigger()
    {
        // The mutation's path exits the enclosing loop via break, so it cannot reach
        // the SaveChanges call inside the loop body. Treating break as a terminator
        // keeps the analyzer free of false positives.
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
            while (true)
            {
                if (flag)
                {
                    u.Name = ""mutated but break exits loop"";
                    break;
                }
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideIfBlockWithContinue_DoesNotTrigger()
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
            while (true)
            {
                if (flag)
                {
                    u.Name = ""mutated but continue re-loops"";
                    continue;
                }
                ctx.SaveChanges();
            }
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideLoopWithBreak_ThenSaveAfterLoop_Triggers()
    {
        // The break exits the loop to the SaveChanges call that follows it, so the mutation
        // can reach the save and LC044 must fire.
        var test = Preamble + EfCoreMock + @"
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
                {|LC044:u.Name|} = ""mutated then break"";
                break;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationInsideLoopWithContinue_ThenSaveAfterLoop_Triggers()
    {
        // The continue jumps to the loop condition; when the loop exits the SaveChanges
        // after it is reachable, so LC044 must fire.
        var test = Preamble + EfCoreMock + @"
namespace Test
{
    public class User { public int Id { get; set; } public string Name { get; set; } }
    public class TestCtx : DbContext { public DbSet<User> Users { get; set; } }
    public class C
    {
        public void M(TestCtx ctx)
        {
            var u = ctx.Users.AsNoTracking().First();
            for (var i = 0; i < 1; i++)
            {
                {|LC044:u.Name|} = ""mutated then continue"";
                continue;
            }
            ctx.SaveChanges();
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
