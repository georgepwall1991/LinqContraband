using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization.UnboundedQueryMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// A <c>FromSql</c>/<c>FromSqlRaw</c>/<c>FromSqlInterpolated</c> root whose constant SQL limits rows at the outer level
/// (<c>LIMIT n</c>, <c>TOP n</c>, <c>FETCH FIRST/NEXT n ROWS</c>) is already bounded. A limit only inside a
/// parenthesized subquery, in a comment or in a string literal does not count.
/// </summary>
public partial class UnboundedQueryMaterializationTests
{
    private static string RawSqlProgram(string body) => Usings + EFCoreMock + @"
namespace Microsoft.EntityFrameworkCore
{
    public static class RelationalQueryableExtensions
    {
        public static IQueryable<T> FromSqlRaw<T>(this DbSet<T> source, string sql, params object[] parameters) where T : class => source;
        public static IQueryable<T> FromSqlInterpolated<T>(this DbSet<T> source, FormattableString sql) where T : class => source;
        public static IQueryable<T> FromSql<T>(this DbSet<T> source, FormattableString sql) where T : class => source;
    }
}
" + Entities + @"
namespace TestApp
{
    public class TestClass
    {
        private const string TopTen = ""SELECT TOP 10 * FROM Users"";

        public object Run(AppDbContext db, int n, string sql)
        {
            " + body + @"
        }
    }
}";

    [Theory]
    [InlineData(@"return db.Users.FromSqlRaw(""SELECT * FROM Users LIMIT 10"").ToList();")]
    [InlineData(@"return db.Users.FromSqlRaw(""select * from Users order by Id limit @p0"", n).ToList();")]
    [InlineData(@"return db.Users.FromSqlRaw(""SELECT * FROM Users LIMIT {0}"", n).ToList();")]
    [InlineData(@"return db.Users.FromSqlRaw(""SELECT TOP 10 * FROM Users"").ToList();")]
    [InlineData(@"return db.Users.FromSqlRaw(""SELECT TOP (@n) * FROM Users"", n).ToList();")]
    [InlineData(@"return db.Users.FromSqlRaw(""SELECT * FROM Users ORDER BY Id OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY"").ToList();")]
    [InlineData(@"return db.Users.FromSqlRaw(""SELECT * FROM Users FETCH FIRST 5 ROWS ONLY"").ToList();")]
    [InlineData(@"return db.Users.FromSqlRaw(TopTen).ToList();")]
    [InlineData(@"return db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Id IN (SELECT UserId FROM Logins) LIMIT 50"").Where(u => u.IsActive).ToList();")]
    [InlineData(@"return db.Users.FromSqlInterpolated($""SELECT * FROM Users LIMIT {n}"").ToList();")]
    [InlineData(@"return db.Users.FromSql($""SELECT TOP ({n}) * FROM Users"").ToList();")]
    [InlineData(@"return db.Users.FromSql($""SELECT * FROM Users ORDER BY Id OFFSET {n} ROWS FETCH NEXT 10 ROWS ONLY"").ToList();")]
    public Task RawSqlWithOuterRowLimit_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(RawSqlProgram(body));

    [Theory]
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT * FROM Users"").ToList()|};")]
    [InlineData(@"return {|LC031:db.Users.FromSqlInterpolated($""SELECT * FROM Users WHERE Id > {n}"").ToList()|};")]
    // Limit only inside a subquery parenthesis.
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Id IN (SELECT TOP 5 UserId FROM Logins)"").ToList()|};")]
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Id IN (SELECT UserId FROM Logins LIMIT 5)"").ToList()|};")]
    // Limit only in a comment or a string literal, or used as an identifier.
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT * FROM Users -- LIMIT 10"").ToList()|};")]
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT * FROM Users /* TOP 10 */"").ToList()|};")]
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT * FROM Users WHERE Name = 'LIMIT 10'"").ToList()|};")]
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT [Limit], [Top] FROM Users"").ToList()|};")]
    // TOP n PERCENT scales with the table.
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT TOP 10 PERCENT * FROM Users"").ToList()|};")]
    // A top-level set operator: the limit may cover only one branch.
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(""SELECT TOP 5 * FROM Users UNION ALL SELECT * FROM Users"").ToList()|};")]
    // Non-constant SQL cannot be read.
    [InlineData(@"return {|LC031:db.Users.FromSqlRaw(sql).ToList()|};")]
    public Task RawSqlWithoutOuterRowLimit_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(RawSqlProgram(body));
}
