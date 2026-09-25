using LinqContraband.Analyzers.LC009_MissingAsNoTracking;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// A method that returns its entities (a repository getter) hands them to a caller the analyzer cannot see, and on
/// Kavita 7 of 8 sampled callers changed and saved them. LC009 stays quiet on those by default, and
/// <c>dotnet_code_quality.LC009.report_returned_entities = true</c> turns the reports back on. Entities read inside
/// the method, entities leaving it any other way, and entities returned from a context the method creates itself
/// (no caller can save through it) still report.
/// </summary>
public class MissingAsNoTrackingReturnedEntitiesTests
{
    private const string Mocks = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { public int SaveChanges() => 0; }

    public class DbSet<T> : System.Linq.IQueryable<T> where T : class
    {
        public System.Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => null;
        public System.Linq.IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
        public static System.Threading.Tasks.Task<System.Collections.Generic.List<T>> ToListAsync<T>(this System.Linq.IQueryable<T> source) => null;
        public static System.Threading.Tasks.Task<T> FirstOrDefaultAsync<T>(this System.Linq.IQueryable<T> source, System.Linq.Expressions.Expression<System.Func<T, bool>> predicate) => null;
    }
}

namespace Shop
{
    public class Order { public int Id { get; set; } public string Status { get; set; } }

    public class ShopContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
    }
}
";

    private static string Code(string members) => @"using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shop;

class OrderRepository
{
    private readonly ShopContext db = new ShopContext();

" + members + @"

    void Show(object value) { }
}
" + Mocks;

    [Theory]
    [InlineData("public Order Get(int id) => db.Orders.First(o => o.Id == id);")]
    [InlineData("public Task<Order> GetAsync(int id) => db.Orders.FirstOrDefaultAsync(o => o.Id == id);")]
    [InlineData("public async Task<List<Order>> GetAllAsync() { return await db.Orders.Where(o => o.Id > 0).ToListAsync(); }")]
    [InlineData("public Order Get(int id) { var order = db.Orders.First(o => o.Id == id); return order; }")]
    [InlineData("public List<Order> Open() { var orders = db.Orders.ToList(); return orders.Where(o => o.Status == \"Open\").ToList(); }")]
    [InlineData("public IEnumerable<Order> Stream() { foreach (var order in db.Orders.ToList()) yield return order; }")]
    [InlineData("public IEnumerable<Order> Stream() { var orders = db.Orders.ToList(); foreach (var order in orders) yield return order; }")]
    [InlineData("public List<Order> FromCaller(ShopContext context) => context.Orders.ToList();")]
    public Task ReturnedEntities_AreQuietByDefault(string members) =>
        VerifyCS.VerifyAnalyzerAsync(Code(members));

    [Theory]
    [InlineData("public Order Get(int id) => {|LC009:db.Orders.First(o => o.Id == id)|};")]
    [InlineData("public Task<Order> GetAsync(int id) => {|LC009:db.Orders.FirstOrDefaultAsync(o => o.Id == id)|};")]
    [InlineData("public Order Get(int id) { var order = {|LC009:db.Orders.First(o => o.Id == id)|}; return order; }")]
    [InlineData("public IEnumerable<Order> Stream() { foreach (var order in {|LC009:db.Orders.ToList()|}) yield return order; }")]
    public Task ReturnedEntities_ReportWhenOptedIn(string members) =>
        ReturnedEntitiesOptInVerifier.VerifyAnalyzerAsync(Code(members));

    [Theory]
    [InlineData("public int Count() { var orders = {|LC009:db.Orders.ToList()|}; return orders.Count; }")]
    [InlineData("public List<string> Statuses() { var orders = {|LC009:db.Orders.ToList()|}; return orders.Select(o => o.Status).ToList(); }")]
    [InlineData("public string Status(int id) => {|LC009:db.Orders.First(o => o.Id == id)|}.Status;")]
    [InlineData("public void Print() { var orders = {|LC009:db.Orders.ToList()|}; Show(orders); }")]
    [InlineData("public void Background() { _ = Task.Run(() => {|LC009:db.Orders.ToList()|}); }")]
    [InlineData("public int Local() { List<Order> Load() => {|LC009:db.Orders.ToList()|}; return Load().Count; }")]
    [InlineData("public List<Order> Owned() { var context = new ShopContext(); return {|LC009:context.Orders.ToList()|}; }")]
    [InlineData("public Order OwnedFirst(int id) { var context = new ShopContext(); var order = {|LC009:context.Orders.First(o => o.Id == id)|}; return order; }")]
    public Task EntitiesThatAreNotReturned_StillReportByDefault(string members) =>
        VerifyCS.VerifyAnalyzerAsync(Code(members));

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("")]
    public async Task OptionOtherThanTrue_KeepsReturnedEntitiesQuiet(string value)
    {
        var test = new CSharpCodeFixTest<MissingAsNoTrackingAnalyzer, MissingAsNoTrackingFixer, XUnitVerifier>
        {
            TestCode = Code("public Order Get(int id) => db.Orders.First(o => o.Id == id);")
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", $@"root = true

[*.cs]
dotnet_code_quality.LC009.report_returned_entities = {value}
"));
        await test.RunAsync();
    }

    [Fact]
    public async Task OptionIsCaseInsensitive()
    {
        var test = new CSharpCodeFixTest<MissingAsNoTrackingAnalyzer, MissingAsNoTrackingFixer, XUnitVerifier>
        {
            TestCode = Code("public Order Get(int id) => {|LC009:db.Orders.First(o => o.Id == id)|};")
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"root = true

[*.cs]
dotnet_code_quality.LC009.report_returned_entities = True
"));
        await test.RunAsync();
    }
}
