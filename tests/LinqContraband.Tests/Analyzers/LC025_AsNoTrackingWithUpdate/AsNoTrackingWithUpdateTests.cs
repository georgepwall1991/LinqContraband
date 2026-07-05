using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer,
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer,
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateFixer>;

namespace LinqContraband.Tests.Analyzers.LC025_AsNoTrackingWithUpdate;

public class AsNoTrackingWithUpdateTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Collections.Generic;

namespace Microsoft.EntityFrameworkCore
{
    public enum EntityState { Detached, Unchanged, Deleted, Modified, Added }
    public class EntityEntry { public EntityState State { get; set; } }
    public class DbContext { 
        public void Update(object entity) { }
        public void UpdateRange(IEnumerable<object> entities) { }
        public void Remove(object entity) { }
        public void RemoveRange(IEnumerable<object> entities) { }
        public EntityEntry Entry(object entity) => new EntityEntry();
    }
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public void Update(TEntity entity) { }
        public void UpdateRange(IEnumerable<TEntity> entities) { }
        public void Remove(TEntity entity) { }
        public void RemoveRange(IEnumerable<TEntity> entities) { }
        public Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public IQueryProvider Provider => null;
        public System.Collections.IEnumerator GetEnumerator() => null;
        System.Collections.Generic.IEnumerator<TEntity> System.Collections.Generic.IEnumerable<TEntity>.GetEnumerator() => null;
    }
    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
        public static IQueryable<TSource> AsNoTrackingWithIdentityResolution<TSource>(this IQueryable<TSource> source) => source;
    }
}
";

    [Fact]
    public async Task AsNoTracking_ThenUpdate_ShouldTriggerLC025()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update({|LC025:user|});
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldRemoveAsNoTracking()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update({|LC025:user|});
            }
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update(user);
            }
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldRemoveAsNoTrackingFromAssignment()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            User user;
            user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            users.Update({|LC025:user|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            User user;
            user = users.FirstOrDefault(x => x.Id == 1);
            users.Update(user);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldRemoveAsNoTrackingFromForeachCollection()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            foreach (var user in users.AsNoTracking().Where(x => x.Id > 0).ToList())
            {
                users.Remove({|LC025:user|});
            }
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            foreach (var user in users.Where(x => x.Id > 0).ToList())
            {
                users.Remove(user);
            }
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task AsNoTracking_ThenRemove_ShouldTriggerLC025()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTracking().First();
            users.Remove({|LC025:user|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolution_ThenUpdate_ShouldTriggerLC025()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTrackingWithIdentityResolution().FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update({|LC025:user|});
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldRemoveAsNoTrackingWithIdentityResolution()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTrackingWithIdentityResolution().FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update({|LC025:user|});
            }
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update(user);
            }
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task AsNoTracking_ThenEntryStateModified_ShouldTriggerLC025()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbContext db, DbSet<User> users)
        {
            var user = users.AsNoTracking().First();
            db.Entry({|LC025:user|}).State = EntityState.Modified;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTracking_ThenEntryStateDeleted_ShouldTriggerLC025()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbContext db, DbSet<User> users)
        {
            var user = users.AsNoTracking().First();
            db.Entry({|LC025:user|}).State = EntityState.Deleted;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EntryStateUnchanged_ShouldNotTriggerLC025()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbContext db, DbSet<User> users)
        {
            var user = users.AsNoTracking().First();
            db.Entry(user).State = EntityState.Unchanged;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldRemoveAsNoTrackingForEntryStateModified()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbContext db, DbSet<User> users)
        {
            var user = users.AsNoTracking().First();
            db.Entry({|LC025:user|}).State = EntityState.Modified;
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbContext db, DbSet<User> users)
        {
            var user = users.First();
            db.Entry(user).State = EntityState.Modified;
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task AsNoTrackingCollection_ThenUpdateRange_ShouldTriggerLC025()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var batch = users.AsNoTracking().Where(x => x.Id > 0).ToList();
            users.UpdateRange({|LC025:batch|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoTrackingAssignmentAfterUpdate_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.FirstOrDefault(x => x.Id == 1);
            users.Update(user);
            user = users.AsNoTracking().FirstOrDefault(x => x.Id == 2);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReassignedToTrackedQueryBeforeUpdate_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            user = users.FirstOrDefault(x => x.Id == 2);
            users.Update(user);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MaterializedFromNoTrackingQueryAlias_ShouldTriggerLC025()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var query = users.AsNoTracking().Where(x => x.Id > 0);
            var user = query.FirstOrDefault();
            users.Update({|LC025:user|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ProjectedNewEntity_ThenUpdate_ShouldNotTrigger()
    {
        // A Select projecting to a newly-constructed object yields an instance EF never
        // tracks — regardless of AsNoTracking — so LC025's "untracked entity from an
        // AsNoTracking query" premise does not hold and the rule must stay quiet.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTracking().Select(x => new User { Id = x.Id }).First();
            users.Update(user);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task WrapThenUnwrapProjection_ThenUpdate_ShouldStillTrigger()
    {
        // A constructed projection that merely wraps the entity, followed by a projection
        // that re-exposes it, still materializes the original untracked entity — so the
        // anti-pattern applies. Only the outermost projection (here the member access)
        // governs, so the constructed wrapper deeper in the chain must not suppress LC025.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTracking().Select(u => new { Entity = u }).Select(x => x.Entity).First();
            users.Update({|LC025:user|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IdentitySelect_ThenUpdate_ShouldStillTrigger()
    {
        // An identity Select returns the materialized entity itself, whose tracking state
        // IS governed by AsNoTracking, so the anti-pattern still applies and LC025 fires.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.AsNoTracking().Select(x => x).First();
            users.Update({|LC025:user|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionallyReassignedToNoTracking_AmbiguousOrigin_ShouldNotTrigger()
    {
        // On the flag=false path the entity is tracked; last-write-wins would judge by the
        // if-branch assignment alone. Disagreeing origins where the latest is conditional
        // relative to the use are path-dependent — stay quiet.
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
        // An unconditional latest assignment dominates whatever came before it —
        // last-write-wins stays valid and the rule keeps firing.
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
        // Both origins are untracked, so every path passes an untracked entity — no
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
    public async Task SupersededOriginBeforeConditionalReassignment_ShouldStillTrigger()
    {
        // The initial null origin is dead — overwritten by the unconditional untracked
        // assignment before the branch — so every path reaching Update carries an untracked
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

    [Fact]
    public async Task Tracked_ThenUpdate_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user = users.FirstOrDefault(x => x.Id == 1);
            if (user != null)
            {
                users.Update(user);
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FixAll_RemovesAsNoTrackingFromAllProblematicVariables()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user1 = users.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            users.Update({|#0:user1|});

            var user2 = users.AsNoTracking().FirstOrDefault(x => x.Id == 2);
            users.Update({|#1:user2|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            var user1 = users.FirstOrDefault(x => x.Id == 1);
            users.Update(user1);

            var user2 = users.FirstOrDefault(x => x.Id == 2);
            users.Update(user2);
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "RemoveAsNoTracking"
        };

        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC025")
                .WithLocation(0)
                .WithArguments("Update"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC025")
                .WithLocation(1)
                .WithArguments("Update"));

        await testObj.RunAsync();
    }
}
