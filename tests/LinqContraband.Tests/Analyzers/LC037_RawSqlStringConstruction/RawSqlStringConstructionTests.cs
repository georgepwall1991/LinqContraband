using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC037_RawSqlStringConstruction.RawSqlStringConstructionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC037_RawSqlStringConstruction;

public class RawSqlStringConstructionTests
{
    private const string EfMock = @"
using System;
using System.Linq;
using System.Text;

namespace Microsoft.EntityFrameworkCore
{
    public class DatabaseFacade { }

    public class DbContext
    {
        public DatabaseFacade Database { get; } = new DatabaseFacade();
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(this IQueryable<TEntity> source, string sql, params object[] parameters) => source;
        public static int ExecuteSqlRaw(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => 0;
        public static int ExecuteSqlRawAsync(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => 0;
    }
}
";

    [Fact]
    public async Task FromSqlRaw_WithStringFormat_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public void Run(IQueryable<User> query, int id)
        {
            var sql = string.Format(""SELECT * FROM Users WHERE Id = {0}"", id);
            var result = query.FromSqlRaw(sql);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC037").WithSpan(33, 43, 33, 46).WithArguments("FromSqlRaw");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilder_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = new StringBuilder()
                .Append(""UPDATE Users SET Name = '"")
                .Append(id)
                .Append(""'"")
                .ToString();

            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC037").WithSpan(36, 52, 36, 55).WithArguments("ExecuteSqlRaw");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringConcatAlias_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var prefix = ""UPDATE Users SET Name = "";
            var sql = prefix + id;
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("LC037").WithSpan(31, 52, 31, 55).WithArguments("ExecuteSqlRaw");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConstantSql_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = db.Database.ExecuteSqlRaw(""UPDATE Users SET Active = 1"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantConcat_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var sql = ""SELECT "" + ""* FROM Users"";
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
