using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateAnalyzer,
    LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate.AsNoTrackingWithUpdateFixer>;

namespace LinqContraband.Tests.Analyzers.LC025_AsNoTrackingWithUpdate;

/// <summary>
/// Leftover LC025 <c>TrackingMethods</c> name the original 33 fixtures do not isolate.
/// <c>Update</c>, <c>UpdateRange</c> and <c>Remove</c> already have crime cases.
/// <c>RemoveRange</c> is listed beside them and documented as a write-path crime,
/// but no fixture marks <c>{|LC025|}</c> on it. Dropping that name keeps the
/// shipped <c>UpdateRange</c>/<c>Remove</c> fixtures green and fails only these
/// pins. A tracked collection passed to <c>RemoveRange</c> stays quiet so a
/// later edit that reports every <c>RemoveRange</c> is visible.
/// </summary>
public partial class AsNoTrackingWithUpdateTests
{
    [Fact]
    public async Task AsNoTrackingCollection_ThenRemoveRange_Reports()
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
            users.RemoveRange({|LC025:batch|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTrackingCollection_ThenDbContextRemoveRange_Reports()
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
            var batch = users.AsNoTracking().Where(x => x.Id > 0).ToList();
            db.RemoveRange({|LC025:batch|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsNoTrackingWithIdentityResolutionCollection_ThenRemoveRange_Reports()
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
            var batch = users.AsNoTrackingWithIdentityResolution().Where(x => x.Id > 0).ToList();
            users.RemoveRange({|LC025:batch|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TrackedCollection_ThenRemoveRange_StaysQuiet()
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
            var batch = users.Where(x => x.Id > 0).ToList();
            users.RemoveRange(batch);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixes_RemoveRangeByRemovingAsNoTracking()
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
            users.RemoveRange({|LC025:batch|});
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
            var batch = users.Where(x => x.Id > 0).ToList();
            users.RemoveRange(batch);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
