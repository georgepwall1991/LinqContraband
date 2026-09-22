using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

/// <summary>
/// The fix must name an API the project can call without warnings: FromSql on EF Core 7+ (and on
/// Cosmos, which never had FromSqlInterpolated), and never an obsolete FromSqlInterpolated (EF Core 11).
/// </summary>
public class AvoidFromSqlRawWithInterpolationFixerTargetTests
{
    private const string Usage = @"
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : System.Linq.IQueryable<TEntity> where TEntity : class
    {
        public System.Type ElementType => typeof(TEntity);
        public System.Linq.Expressions.Expression Expression => null;
        public System.Linq.IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EXTENSIONS
    {
        public static System.Linq.IQueryable<TEntity> FromSqlRaw<TEntity>(this DbSet<TEntity> source, string sql, params object[] parameters) where TEntity : class => source;
        SIBLINGS
    }
}

public class User { public int Id { get; set; } }

public class Repository
{
    public void Run(DbSet<User> users, int id)
    {
        var result = users.CALL(OPEN$""SELECT * FROM Users WHERE Id = {id}""CLOSE);
    }
}
";

    private const string FromSql =
        "public static System.Linq.IQueryable<TEntity> FromSql<TEntity>(this DbSet<TEntity> source, System.FormattableString sql) where TEntity : class => source;";

    private const string FromSqlInterpolated =
        "public static System.Linq.IQueryable<TEntity> FromSqlInterpolated<TEntity>(this DbSet<TEntity> source, System.FormattableString sql) where TEntity : class => source;";

    private const string ObsoleteFromSqlInterpolated =
        "[System.Obsolete(\"Use FromSql() instead.\")] " + FromSqlInterpolated;

    private static string Code(string extensions, string siblings, string call, bool reported) =>
        Usage.Replace("EXTENSIONS", extensions)
            .Replace("SIBLINGS", siblings)
            .Replace("CALL", call)
            .Replace("OPEN", reported ? "{|#0:" : string.Empty)
            .Replace("CLOSE", reported ? "|}" : string.Empty);

    private static Task VerifyAsync(string extensions, string siblings, string safe, string? fixedCall)
    {
        var expected = new DiagnosticResult("LC018", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(safe, "FromSqlRaw");

        var test = new CodeFixTest
        {
            TestCode = Code(extensions, siblings, "FromSqlRaw", reported: true),
            FixedCode = fixedCall is null
                ? Code(extensions, siblings, "FromSqlRaw", reported: true)
                : Code(extensions, siblings, fixedCall, reported: false)
        };
        test.ExpectedDiagnostics.Add(expected);
        if (fixedCall is null)
            test.FixedState.ExpectedDiagnostics.Add(expected);

        return test.RunAsync();
    }

    [Fact]
    public Task Fix_PrefersFromSql_WhenBothSiblingsExist() =>
        VerifyAsync("RelationalQueryableExtensions", FromSql + "\n        " + FromSqlInterpolated, "FromSql", "FromSql");

    [Fact]
    public Task Fix_UsesFromSql_WhenFromSqlInterpolatedIsObsolete() =>
        VerifyAsync("RelationalQueryableExtensions", FromSql + "\n        " + ObsoleteFromSqlInterpolated, "FromSql", "FromSql");

    [Fact]
    public Task Fix_UsesFromSql_OnCosmosWhichHasNoFromSqlInterpolated() =>
        VerifyAsync("CosmosQueryableExtensions", FromSql, "FromSql", "FromSql");

    [Fact]
    public Task Fix_FallsBackToFromSqlInterpolated_BeforeEfCore7() =>
        VerifyAsync("RelationalQueryableExtensions", FromSqlInterpolated, "FromSqlInterpolated", "FromSqlInterpolated");

    [Fact]
    public Task Fix_IsNotOffered_WhenOnlyAnObsoleteSiblingExists() =>
        VerifyAsync("RelationalQueryableExtensions", ObsoleteFromSqlInterpolated, "FromSql", null);

    [Fact]
    public Task Fix_IsNotOffered_WhenNoParameterizingSiblingExists() =>
        VerifyAsync("CosmosQueryableExtensions", string.Empty, "FromSql", null);
}
