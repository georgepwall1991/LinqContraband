using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC037_RawSqlStringConstruction.RawSqlStringConstructionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC037_RawSqlStringConstruction;

/// <summary>
/// Leftover LC037 <c>TargetMethods</c> sink the shipped fixtures do not isolate.
/// <c>FromSqlRaw</c> already has <c>string.Format</c> and interpolated-alias crimes.
/// <c>SqlQueryRaw</c> already has <c>string.Format</c> and <c>string.Concat</c>.
/// <c>ExecuteSqlRaw</c> already has <c>string.Concat</c>, concat-alias and
/// <c>StringBuilder</c> crimes. Direct interpolation and direct <c>+</c> on
/// <c>ExecuteSqlRawAsync</c> stay quiet (LC034 owns those). Dropping leftover
/// <c>ExecuteSqlRawAsync</c> fails only the leftover crime pins. A lookalike
/// name outside <c>Microsoft.EntityFrameworkCore</c> stays quiet so a later
/// edit that drops the containing-namespace gate is visible.
/// </summary>
public partial class RawSqlStringConstructionTests
{
    private static string LeftoverAsyncProgram(string body, string extraNamespaces = "")
    {
        return @"using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Threading.Tasks;" + extraNamespaces + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id)
        {
" + body + @"
        }
    }
}";
    }

    [Fact]
    public async Task LeftoverExecuteSqlRawAsync_WithStringFormat_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverAsyncProgram(@"
            var sql = string.Format(""UPDATE Users SET Name = '{0}'"", id);
            var result = await db.Database.ExecuteSqlRawAsync({|LC037:sql|});
"));
    }

    [Fact]
    public async Task LeftoverExecuteSqlRawAsync_WithStringBuilder_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverAsyncProgram(@"
            var sql = new StringBuilder()
                .Append(""UPDATE Users SET Name = '"")
                .Append(id)
                .Append(""'"")
                .ToString();
            var result = await db.Database.ExecuteSqlRawAsync({|LC037:sql|});
"));
    }

    [Fact]
    public async Task LeftoverExecuteSqlRawAsync_NamedSqlArgument_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(LeftoverAsyncProgram(@"
            var sql = string.Format(""UPDATE Users SET Name = '{0}'"", id);
            var result = await db.Database.ExecuteSqlRawAsync(sql: {|LC037:sql|});
"));
    }

    [Fact]
    public async Task LookalikeExecuteSqlRawAsync_StaysQuiet()
    {
        var test = LeftoverAsyncProgram(@"
            var sql = string.Format(""UPDATE Users SET Name = '{0}'"", id);
            var result = await OtherOrm.DatabaseExtensions.ExecuteSqlRawAsync(db.Database, sql);
", extraNamespaces: @"
using OtherOrm;
") + @"
namespace OtherOrm
{
    public static class DatabaseExtensions
    {
        public static Task<int> ExecuteSqlRawAsync(Microsoft.EntityFrameworkCore.DatabaseFacade databaseFacade, string sql, params object[] parameters)
            => Task.FromResult(0);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
