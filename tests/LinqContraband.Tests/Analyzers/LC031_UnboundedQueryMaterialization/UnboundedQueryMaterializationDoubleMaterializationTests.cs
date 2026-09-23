using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization.UnboundedQueryMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// <c>db.Users.ToList().Where(...).ToList()</c> loads the table once, at the inner <c>ToList()</c>. The outer
/// materializer copies a list that is already in memory, so LC031 reports the inner call only.
/// </summary>
public partial class UnboundedQueryMaterializationTests
{
    private static string DoubleMaterializationProgram(string body) => Usings + EFCoreMock + @"
namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static System.Threading.Tasks.Task<List<T>> ToListAsync<T>(this IQueryable<T> source) =>
            System.Threading.Tasks.Task.FromResult(new List<T>());
    }
}
" + Entities + @"
namespace TestApp
{
    public class TestClass
    {
        public async System.Threading.Tasks.Task<object> Run(AppDbContext db)
        {
            " + body + @"
        }
    }
}";

    [Theory]
    [InlineData("return {|LC031:db.Users.ToList()|}.Where(u => u.IsActive).ToList();")]
    [InlineData("return {|LC031:db.Users.ToList()|}.OrderBy(u => u.Id).Take(5).ToList();")]
    [InlineData("return {|LC031:db.Users.Where(u => u.IsActive).ToArray()|}.ToArray();")]
    [InlineData("return {|LC031:db.Users.ToList()|}.Where(u => u.IsActive).ToDictionary(u => u.Id);")]
    [InlineData("return {|LC031:db.Users.ToList()|}.Select(u => u.Id).ToHashSet();")]
    [InlineData("return {|LC031:db.Users.AsEnumerable().ToList()|}.ToLookup(u => u.IsActive);")]
    [InlineData("var users = {|LC031:db.Users.ToList()|}; return users.Where(u => u.IsActive).ToList();")]
    [InlineData("return (await {|LC031:db.Users.ToListAsync()|}).Where(u => u.IsActive).ToList();")]
    [InlineData("var users = await {|LC031:db.Users.ToListAsync()|}; return users.Where(u => u.IsActive).ToList();")]
    public Task InMemoryMaterializerOverLoadedList_ReportsOnceOnTheEfQuery(string body) =>
        VerifyCS.VerifyAnalyzerAsync(DoubleMaterializationProgram(body));

    [Theory]
    // The EF query is bounded; the in-memory copy after it loads nothing more.
    [InlineData("return db.Users.Take(10).ToList().Where(u => u.IsActive).ToList();")]
    [InlineData("return (await db.Users.OrderBy(u => u.Id).Take(10).ToListAsync()).Where(u => u.IsActive).ToList();")]
    public Task BoundedEfQueryThenInMemoryMaterializer_StaysQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(DoubleMaterializationProgram(body));
}
