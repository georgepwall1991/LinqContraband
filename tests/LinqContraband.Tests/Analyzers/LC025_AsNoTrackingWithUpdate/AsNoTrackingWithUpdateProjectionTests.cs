using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC025_AsNoTrackingWithUpdate;

public partial class AsNoTrackingWithUpdateTests
{
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
}
