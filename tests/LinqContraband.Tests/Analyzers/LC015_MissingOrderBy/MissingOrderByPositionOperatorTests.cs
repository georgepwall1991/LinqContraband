using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC015_MissingOrderBy.MissingOrderByAnalyzer>;
using LinqContraband.Analyzers.LC015_MissingOrderBy;

namespace LinqContraband.Tests.Analyzers.LC015_MissingOrderBy;

public partial class MissingOrderByTests
{
    [Fact]
    public async Task ElementAt_WithoutOrderBy_ShouldTrigger()
    {
        // EF Core 6+ translates ElementAt to OFFSET/FETCH, which is non-deterministic without ordering.
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : TestNamespace.DbContext { public TestNamespace.DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var user = db.Users.{|#0:ElementAt|}(5);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("ElementAt"));
    }

    [Fact]
    public async Task ElementAtOrDefault_WithoutOrderBy_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : TestNamespace.DbContext { public TestNamespace.DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var user = db.Users.{|#0:ElementAtOrDefault|}(5);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("ElementAtOrDefault"));
    }

    [Fact]
    public async Task ElementAt_AfterOrderBy_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : TestNamespace.DbContext { public TestNamespace.DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var user = db.Users.OrderBy(u => u.Id).ElementAt(5);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LastAsync_WithoutOrderBy_ShouldTrigger()
    {
        // EF reverses the ordering for Last/LastAsync and throws when none is present.
        var test = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<T> LastAsync<T>(this IQueryable<T> source) => Task.FromResult(source.Last());
    }
}
namespace TestApp
{
    public class AppDbContext : TestNamespace.DbContext { public TestNamespace.DbSet<User> Users { get; set; } }

    public class Program
    {
        public async Task Main()
        {
            using var db = new AppDbContext();
            var user = await db.Users.{|#0:LastAsync|}();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("LastAsync"));
    }

    [Fact]
    public async Task ElementAtAsync_WithoutOrderBy_ShouldTrigger()
    {
        var test = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<T> ElementAtAsync<T>(this IQueryable<T> source, int index) => Task.FromResult(source.ElementAt(index));
    }
}
namespace TestApp
{
    public class AppDbContext : TestNamespace.DbContext { public TestNamespace.DbSet<User> Users { get; set; } }

    public class Program
    {
        public async Task Main()
        {
            using var db = new AppDbContext();
            var user = await db.Users.{|#0:ElementAtAsync|}(5);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test, VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("ElementAtAsync"));
    }

    [Fact]
    public async Task TakeLast_IsNotFlagged_BecauseEfCannotTranslateItAtAll()
    {
        // TakeLast/SkipLast are deliberately not LC015 operators: EF Core cannot translate them
        // even with an OrderBy, so "add OrderBy" would be wrong advice. (No diagnostic expected.)
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : TestNamespace.DbContext { public TestNamespace.DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var users = db.Users.TakeLast(5);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
