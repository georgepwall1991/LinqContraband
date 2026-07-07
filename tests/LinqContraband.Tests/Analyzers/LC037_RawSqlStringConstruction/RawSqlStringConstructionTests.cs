using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC037_RawSqlStringConstruction.RawSqlStringConstructionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC037_RawSqlStringConstruction;

public partial class RawSqlStringConstructionTests
{
    private const string EfMock = @"
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public static Task<int> ExecuteSqlRawAsync(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => Task.FromResult(0);
        public static IQueryable<TResult> SqlQueryRaw<TResult>(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => Enumerable.Empty<TResult>().AsQueryable();
    }
}
";

    [Fact]
    public async Task FromSqlRaw_WithInterpolatedString_ShouldNotTrigger()
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
            var result = query.FromSqlRaw($""SELECT * FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConcatenatedString_ShouldNotTrigger()
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
            var result = query.FromSqlRaw(""SELECT * FROM Users WHERE Id = "" + id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithInterpolatedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw($""UPDATE Users SET Name = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConcatenatedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw(""UPDATE Users SET Name = "" + id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRawAsync_WithInterpolatedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id)
        {
            var result = await db.Database.ExecuteSqlRawAsync($""UPDATE Users SET Name = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRawAsync_WithConcatenatedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id)
        {
            var result = await db.Database.ExecuteSqlRawAsync(""UPDATE Users SET Name = "" + id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

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
            var result = query.FromSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithStringFormat_ShouldTrigger()
    {
        // SqlQueryRaw is the DatabaseFacade scalar/keyless raw-SQL sink; String.Format construction
        // is LC037's territory (LC018 owns only interpolation and '+' concatenation).
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = string.Format(""SELECT Name FROM Users WHERE Id = {0}"", id);
            var result = db.Database.SqlQueryRaw<string>({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithStringConcatMethod_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, string name)
        {
            var result = db.Database.SqlQueryRaw<string>({|LC037:string.Concat(""SELECT Name FROM Users WHERE Name = '"", name, ""'"")|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithInterpolatedString_ShouldNotTrigger_OwnedByLC018()
    {
        // Interpolation is owned by LC018 (which now covers SqlQueryRaw); LC037 must defer so the
        // sink is not double-reported.
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.SqlQueryRaw<int>($""SELECT Id FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithConstantString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = db.Database.SqlQueryRaw<string>(""SELECT Name FROM Users"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithDirectInterpolatedString_ShouldNotTrigger()
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
            var result = query.FromSqlRaw($""SELECT * FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithDirectConcatenation_ShouldNotTrigger()
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
            var result = query.FromSqlRaw(""SELECT * FROM Users WHERE Id = "" + id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithInterpolatedAlias_ShouldTrigger()
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
            var sql = $""SELECT * FROM Users WHERE Id = {id}"";
            var result = query.FromSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithDirectInterpolatedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw($""UPDATE Users SET Name = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithDirectConcatenation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw(""UPDATE Users SET Name = "" + id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
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
            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringConcatCall_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = string.Concat(""UPDATE Users SET Name = "", id);
            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
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
