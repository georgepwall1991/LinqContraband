using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC015_MissingOrderBy.MissingOrderByAnalyzer>;
using LinqContraband.Analyzers.LC015_MissingOrderBy;

namespace LinqContraband.Tests.Analyzers.LC015_MissingOrderBy;

// Re-sorting a Skip/Take window is only a problem when the window itself was picked from an
// unordered set. When an ordering runs before the Skip/Take, the window is a deterministic
// top-N (or page) and sorting it again is intentional ("latest 10, shown oldest first").
public partial class MissingOrderByTests
{
    private static string WrapOrderedWindow(string body, string members = "") => Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext { public DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main(int scanLimit, int count, string suffix)
        {
            using var db = new AppDbContext();
" + body + @"
        }
" + members + @"
    }
}" + MockNamespace;

    [Fact]
    public async Task OrderBy_AfterOrderedTake_ShouldNotTrigger()
    {
        var test = WrapOrderedWindow(@"
            var result = db.Users.OrderByDescending(u => u.Id).Take(10).OrderBy(u => u.Id).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderByDescending_AfterOrderedTakeAndWhere_ShouldNotTrigger()
    {
        // Skoruba Duende admin AuditLogRepository: scan the newest N rows, filter, re-sort, take.
        var test = WrapOrderedWindow(@"
            var result = db.Users
                .OrderByDescending(x => x.Id)
                .Take(scanLimit)
                .Where(x => x.Name != null && !x.Name.EndsWith(suffix))
                .OrderByDescending(x => x.Id)
                .Take(count)
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderBy_AfterOrderedSkipTake_ShouldNotTrigger()
    {
        var test = WrapOrderedWindow(@"
            var result = db.Users.OrderBy(u => u.Name).ThenBy(u => u.Id).Skip(20).Take(10).OrderByDescending(u => u.Id).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderBy_AfterOrderedTake_WithWhereBetweenOrderingAndTake_ShouldNotTrigger()
    {
        var test = WrapOrderedWindow(@"
            var result = db.Users.OrderByDescending(u => u.Id).Where(u => u.Name != null).Take(10).OrderBy(u => u.Name).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderBy_OnLocalHoldingOrderedTake_ShouldNotTrigger()
    {
        var test = WrapOrderedWindow(@"
            var latest = db.Users.OrderByDescending(u => u.Id).Take(10);
            var result = latest.OrderBy(u => u.Id).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderBy_AfterTakeOnOrderedLocal_ShouldNotTrigger()
    {
        var test = WrapOrderedWindow(@"
            IQueryable<User> ordered = db.Users.OrderByDescending(u => u.Id);
            var result = ordered.Take(10).OrderBy(u => u.Name).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderBy_AfterTakeOnProjectSortHelper_ShouldNotTrigger()
    {
        var test = WrapOrderedWindow(@"
            var result = ApplySort(db.Users).Take(10).OrderBy(u => u.Name).ToList();",
            @"
        private static IQueryable<User> ApplySort(IQueryable<User> query) => query.OrderByDescending(u => u.Id);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderByDescending_AfterUnorderedSkipTake_ShouldStillTrigger()
    {
        // BTCPay shape: an unordered page re-sorted afterwards. The misplaced sort carries the report.
        var test = WrapOrderedWindow(@"
            var result = db.Users.Where(u => u.Name != null).Skip(5).Take(5).{|#0:OrderByDescending|}(u => u.Id).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.MisplacedRule).WithLocation(0).WithArguments("OrderByDescending"));
    }

    [Fact]
    public async Task OrderBy_AfterUnorderedTake_ShouldStillTrigger()
    {
        // Kavita shape: Take(maxRecords).OrderBy(...) with nothing ordering the rows before the Take.
        var test = WrapOrderedWindow(@"
            var result = db.Users.Where(u => u.Name != null).Take(scanLimit).{|#0:OrderBy|}(u => u.Name).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.MisplacedRule).WithLocation(0).WithArguments("OrderBy"));
    }

    [Fact]
    public async Task OrderBy_AfterUnorderedTakeThroughLocal_ShouldStillTrigger()
    {
        var test = WrapOrderedWindow(@"
            var window = db.Users.Take(10);
            var result = window.{|#0:OrderBy|}(u => u.Name).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.MisplacedRule).WithLocation(0).WithArguments("OrderBy"));
    }

    [Fact]
    public async Task OrderBy_AfterTakeOverMisplacedSort_ReportsOnlyTheFirstWindow()
    {
        // The first window is unordered, so Take(10) and the sort after it report. The second
        // window is picked from the (misplaced but real) ordering, so re-sorting it adds nothing.
        var test = WrapOrderedWindow(@"
            var result = db.Users.{|#0:Take|}(10).{|#1:OrderBy|}(u => u.Name).Take(5).OrderByDescending(u => u.Id).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule).WithLocation(0).WithArguments("Take"),
            VerifyCS.Diagnostic(MissingOrderByAnalyzer.MisplacedRule).WithLocation(1).WithArguments("OrderBy"));
    }
}
