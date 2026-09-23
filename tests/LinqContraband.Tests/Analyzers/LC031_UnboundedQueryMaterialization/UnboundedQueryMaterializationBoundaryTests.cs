using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization.UnboundedQueryMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// LC031 only walks through LINQ and EF Core operators. A project's own query helper (paging, specifications)
/// may apply the bound itself, so the rule stops there. A lookup by primary key, or by a list of keys, is bounded
/// by the keys. A <c>Take</c> after <c>AsEnumerable()</c> runs in memory and does not bound the database query.
/// </summary>
public partial class UnboundedQueryMaterializationTests
{
    private static string BoundaryProgram(string body) => Usings + EFCoreMock + @"
namespace TestApp
{
    public class User { public int Id { get; set; } public bool IsActive { get; set; } public int TeamId { get; set; } }

    public class Country { [System.ComponentModel.DataAnnotations.Key] public string Code { get; set; } public string Name { get; set; } }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
        public Microsoft.EntityFrameworkCore.DbSet<Country> Countries { get; set; }
    }

    public interface ISpecification<T> { }

    public static class QueryHelpers
    {
        public static IQueryable<T> Paginate<T>(this IQueryable<T> source, int page, int size) => source.Skip(page * size).Take(size);
        public static IQueryable<T> WithSpecification<T>(this IQueryable<T> source, ISpecification<T> spec) => source;
        public static IEnumerable<T> InMemoryFilter<T>(this IEnumerable<T> source) => source;
    }

    public class TestClass
    {
        public void Run(AppDbContext db, int id, List<int> ids, string code, ISpecification<User> spec)
        {
            " + body + @"
        }
    }
}";

    [Theory]
    // A project's own IQueryable helper may bound the query.
    [InlineData("var result = db.Users.Paginate(2, 50).ToList();")]
    [InlineData("var result = db.Users.WithSpecification(spec).ToList();")]
    [InlineData("var result = db.Users.Where(u => u.IsActive).Paginate(0, 20).OrderBy(u => u.Id).ToList();")]
    // Primary-key lookups are bounded by their keys.
    [InlineData("var result = db.Users.Where(u => u.Id == id).ToList();")]
    [InlineData("var result = db.Users.Where(u => id == u.Id).ToList();")]
    [InlineData("var result = db.Users.Where(u => u.Id == id && u.IsActive).ToList();")]
    [InlineData("var result = db.Users.Where(u => ids.Contains(u.Id)).ToList();")]
    [InlineData("var result = db.Countries.Where(c => c.Code == code).ToList();")]
    [InlineData("var result = (from u in db.Users where u.Id == id select u).ToList();")]
    public Task BoundedOrHelperApplied_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(BoundaryProgram(body));

    [Theory]
    // Take after AsEnumerable() trims in memory; the whole table is still loaded.
    [InlineData("var result = {|LC031:db.Users.AsEnumerable().Take(10).ToList()|};")]
    // An in-memory helper on IEnumerable runs after the full load.
    [InlineData("var result = {|LC031:db.Users.InMemoryFilter().ToList()|};")]
    // Foreign keys and non-key members can match many rows.
    [InlineData("var result = {|LC031:db.Users.Where(u => u.TeamId == id).ToList()|};")]
    [InlineData("var result = {|LC031:db.Users.Where(u => u.Id == id || u.IsActive).ToList()|};")]
    [InlineData("var result = {|LC031:db.Users.Where(u => u.Id > id).ToList()|};")]
    [InlineData("var result = {|LC031:db.Users.Where(u => u.Id == u.TeamId).ToList()|};")]
    [InlineData("var result = {|LC031:db.Users.Where(u => ids.Contains(u.TeamId)).ToList()|};")]
    public Task UnboundedShapes_StillReport(string body) =>
        VerifyCS.VerifyAnalyzerAsync(BoundaryProgram(body));
}
