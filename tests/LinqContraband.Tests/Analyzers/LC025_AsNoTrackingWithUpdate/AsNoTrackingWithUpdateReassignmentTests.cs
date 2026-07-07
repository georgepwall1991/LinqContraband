using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer,
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateFixer>;

namespace LinqContraband.Tests.Analyzers.LC025_AsNoTrackingWithUpdate;

public partial class AsNoTrackingWithUpdateTests
{
    [Fact]
    public async Task ConditionallyReassignedToNoTracking_AmbiguousOrigin_ShouldNotTrigger()
    {
        // On the flag=false path the entity is tracked; last-write-wins would judge by the
        // if-branch assignment alone. Disagreeing origins where the latest is conditional
        // relative to the use are path-dependent, so stay quiet.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users, bool flag)
        {
            var user = users.FirstOrDefault(x => x.Id == 1);
            if (flag)
            {
                user = users.AsNoTracking().FirstOrDefault(x => x.Id == 2);
            }
            users.Update(user);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionallyReassignedToTracked_AmbiguousOrigin_ShouldNotTrigger()
    {
        // The mirror shape: untracked base, tracked conditional reassignment. Also
        // path-dependent, also quiet.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users, bool flag)
        {
            var user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (flag)
            {
                user = users.FirstOrDefault(x => x.Id == 2);
            }
            users.Update(user);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SequentialReassignmentToNoTracking_ShouldStillTrigger()
    {
        // An unconditional latest assignment dominates whatever came before it,
        // so last-write-wins stays valid and the rule keeps firing.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            User user = null;
            user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            users.Update({|LC025:user|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionalReassignmentBothNoTracking_ShouldStillTrigger()
    {
        // Both origins are untracked, so every path passes an untracked entity: no
        // ambiguity, the rule keeps firing.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users, bool flag)
        {
            var user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (flag)
            {
                user = users.AsNoTracking().FirstOrDefault(x => x.Id == 2);
            }
            users.Update({|LC025:user|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionalReassignmentBothNoTracking_HasNoFix()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users, bool flag)
        {
            var user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (flag)
            {
                user = users.AsNoTracking().FirstOrDefault(x => x.Id == 2);
            }
            users.Update({|LC025:user|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task ConditionalReassignmentWithNoTrackingQueryAliasFallback_HasNoFix()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users, bool flag)
        {
            var query = users.AsNoTracking();
            var user = query.FirstOrDefault(x => x.Id == 1);
            if (flag)
            {
                user = users.AsNoTracking().FirstOrDefault(x => x.Id == 2);
            }
            users.Update({|LC025:user|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task SupersededOriginBeforeConditionalReassignment_ShouldStillTrigger()
    {
        // The initial null origin is dead, overwritten by the unconditional untracked
        // assignment before the branch, so every path reaching Update carries an untracked
        // entity. Ambiguity must be judged against the latest unconditional fallback, not
        // against superseded history.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users, bool flag)
        {
            User user = null;
            user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (flag)
            {
                user = users.AsNoTracking().FirstOrDefault(x => x.Id == 2);
            }
            users.Update({|LC025:user|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReassignedInsideSameBranchAsUpdate_ShouldStillTrigger()
    {
        // The conditional reassignment and the Update share the branch, so on every path
        // that reaches the Update the entity is untracked.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users, bool flag)
        {
            var user = users.FirstOrDefault(x => x.Id == 1);
            if (flag)
            {
                user = users.AsNoTracking().FirstOrDefault(x => x.Id == 2);
                users.Update({|LC025:user|});
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
