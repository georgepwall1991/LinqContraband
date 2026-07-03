using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC037_RawSqlStringConstruction.RawSqlStringConstructionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC037_RawSqlStringConstruction;

public class RawSqlStringConstructionTests
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

            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderStatementAppends_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder.Append(""'"");

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstantStringBuilderStatementAppends_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users "");
            builder.Append(""SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderBranchSelectedLiteralAppendValue_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, bool active)
        {
            var builder = new StringBuilder();
            string predicate;
            if (active)
            {
                predicate = ""Active = 1"";
            }
            else
            {
                predicate = ""Active = 0"";
            }

            builder.Append(""UPDATE Users SET "");
            builder.Append(predicate);
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendLocalNonConstantOnlyInReturningBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool skip)
        {
            var builder = new StringBuilder();
            var predicate = ""Active = 1"";
            if (skip)
            {
                predicate = id.ToString();
                return;
            }

            builder.Append(""UPDATE Users SET "");
            builder.Append(predicate);
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderStatementAppendsClearedBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder.Clear();
            builder.Append(""UPDATE Users SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderChainedClearBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"")
                .Append(id)
                .Clear();

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAliasAndLaterAppend_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder.Append(""'"");

            var result = db.Database.ExecuteSqlRaw({|LC037:alias.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendThroughAlias_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            alias.Append(""UPDATE Users SET Name = '"");
            alias.Append(id);
            alias.Append(""'"");

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearThroughAliasBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            alias.Clear();

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearThroughOriginalBeforeAliasCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            alias.Append(""UPDATE Users SET Name = '"");
            alias.Append(id);
            builder.Clear();

            var result = db.Database.ExecuteSqlRaw(alias.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderOriginalReassignedBeforeClear_ShouldStillTriggerAlias()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            alias.Append(""UPDATE Users SET Name = '"");
            alias.Append(id);
            builder = new StringBuilder();
            builder.Clear();

            var result = db.Database.ExecuteSqlRaw({|LC037:alias.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderOriginalReassignedBeforeAppend_ShouldNotTaintAlias()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            alias.Append(""UPDATE Users SET Active = 1"");
            builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);

            var result = db.Database.ExecuteSqlRaw(alias.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderSelfAssignmentAfterAppend_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder = builder;

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalSelfPreservingAssignment_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool keep)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder = keep ? builder : new StringBuilder();

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalAliasAppendIntoTarget_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useBuilder)
        {
            var other = new StringBuilder();
            var builder = new StringBuilder();
            var alias = other;
            if (useBuilder)
            {
                alias = builder;
            }

            alias.Append(""UPDATE Users SET Name = '"");
            alias.Append(id);

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalAliasClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useBuilder)
        {
            var other = new StringBuilder();
            var builder = new StringBuilder();
            var alias = other;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            if (useBuilder)
            {
                alias = builder;
            }

            alias.Clear();

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAliasConditionallyReassignedBeforeClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useOther)
        {
            var builder = new StringBuilder();
            var other = new StringBuilder();
            var alias = builder;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            if (useOther)
            {
                alias = other;
            }

            alias.Clear();

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalAssignmentFromTaintedBuilder_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useFresh)
        {
            var other = new StringBuilder();
            other.Append(""UPDATE Users SET Name = '"");
            other.Append(id);
            var builder = new StringBuilder();
            builder = useFresh ? new StringBuilder() : other;

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCopyAssignmentFromTaintedBuilder_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder = new StringBuilder(builder.ToString());

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCopyAssignmentFromTaintedBuilderExpression_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder = new StringBuilder(builder.ToString() + """");

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCopyAssignmentFromOtherTaintedBuilder_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var other = new StringBuilder();
            other.Append(""UPDATE Users SET Name = '"");
            other.Append(id);

            var builder = new StringBuilder();
            builder = new StringBuilder(other.ToString());

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderVariableCapacityAndConstantAppends_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int capacity)
        {
            var builder = new StringBuilder(capacity);
            builder.Append(""UPDATE Users SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLoopCarriedAppendLocal_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = ""Active = 1"";

            for (var i = 0; i < 2; i++)
            {
                builder.Append(""UPDATE Users SET "");
                builder.Append(value);
                value = id.ToString();
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLoopValueResetBeforeAppend_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = id.ToString();

            for (var i = 0; i < 2; i++)
            {
                value = ""Active = 1"";
                builder.Append(""UPDATE Users SET "");
                builder.Append(value);
                value = id.ToString();
            }

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCompoundAssignedAppendLocal_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = ""Active = "";
            value += id.ToString();
            builder.Append(""UPDATE Users SET "");
            builder.Append(value);

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLoopCarriedCompoundAppendLocal_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = ""Active = 1"";

            for (var i = 0; i < 2; i++)
            {
                builder.Append(""UPDATE Users SET "");
                builder.Append(value);
                value += id.ToString();
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConstantCompoundAssignedAppendLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var builder = new StringBuilder();
            var value = ""Active "";
            value += ""= 1"";
            builder.Append(""UPDATE Users SET "");
            builder.Append(value);

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithNullConditionalStringBuilderAppend_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            StringBuilder builder = new StringBuilder();
            builder?.Append(""UPDATE Users SET Name = '"");
            builder?.Append(id);

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderShortCircuitClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool shouldClear)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            var ignored = shouldClear && builder.Clear() != null;

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderShortCircuitAssignmentReset_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool shouldReset)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            var ignored = shouldReset && (builder = new StringBuilder()) != null;

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderFinallyClearBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            try
            {
                MaybeThrow();
            }
            finally
            {
                builder.Clear();
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }

        private static void MaybeThrow() { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalFinallyClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool safe)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            try
            {
                MaybeThrow();
            }
            finally
            {
                if (safe)
                {
                    builder.Clear();
                }
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }

        private static void MaybeThrow() { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLoopGuardedClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
			using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool shouldClear, bool safe)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            while (shouldClear)
            {
                if (safe)
                {
                    builder.Clear();
                }
                else
                {
                    return;
                }
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearInOnlyContinuingLoopBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool safe)
        {
            while (true)
            {
                var builder = new StringBuilder();
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                if (safe)
                {
                    builder.Clear();
                }
                else
                {
                    continue;
                }

                builder.Append(""UPDATE Users SET Active = 1"");
                var result = db.Database.ExecuteSqlRaw(builder.ToString());
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLocalAppendValue_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = id;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(value);

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderMethodCallAppendValue_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(GetName());

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }

        private static string GetName() => ""Alice"";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearThenFluentStatementAppends_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Clear()
                .Append(""UPDATE Users SET Name = '"")
                .Append(id)
                .Append(""'"");

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderInitializerAppendsClearedBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder()
                .Append(""UPDATE Users SET Name = '"")
                .Append(id);

            builder.Clear();
            builder.Append(""UPDATE Users SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
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

    [Fact]
    public async Task ExecuteSqlRaw_WithConstructedInitialValueOverwrittenByConstantBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = ""UPDATE Users SET Name = "" + id;
            sql = ""UPDATE Users SET Active = 1"";
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstantInitialValueOverwrittenByConstructedSqlBeforeCall_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = ""UPDATE Users SET Active = 1"";
            sql = ""UPDATE Users SET Name = "" + id;
            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithSelfReferentialConcatAssignment_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = ""UPDATE Users SET Name = "";
            sql = sql + id;
            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstructedInitialValueConditionallyOverwrittenByConstant_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useSafeSql)
        {
            var sql = ""UPDATE Users SET Name = "" + id;
            if (useSafeSql)
            {
                sql = ""UPDATE Users SET Active = 1"";
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstantInitialValueConditionallyOverwrittenByConstructedSql_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useNameSql)
        {
            var sql = ""UPDATE Users SET Active = 1"";
            if (useNameSql)
            {
                sql = ""UPDATE Users SET Name = "" + id;
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConditionalConstructedSqlOverwrittenByConstantBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useNameSql)
        {
            var sql = ""UPDATE Users SET Active = 1"";
            if (useNameSql)
            {
                sql = ""UPDATE Users SET Name = "" + id;
            }

            sql = ""UPDATE Users SET Active = 1"";
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendInReturningGuard_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool skip)
        {
            var builder = new StringBuilder();
            if (skip)
            {
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                return;
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeNestedTerminatingGuard_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool skip, bool retry)
        {
            var builder = new StringBuilder();
            if (skip)
            {
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                if (retry)
                {
                    return;
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearInOnlyReachingBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool safe)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            if (safe)
            {
                builder.Clear();
            }
            else
            {
                return;
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCatchOnlyBranchClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool safe)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);

            try
            {
                MaybeThrow();
            }
            catch (InvalidOperationException)
            {
                if (safe)
                {
                    builder.Clear();
                }
                else
                {
                    return;
                }
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }

        private static void MaybeThrow() { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendInReturningLoop_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool retry)
        {
            var builder = new StringBuilder();
            while (retry)
            {
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                return;
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendInReturningSwitchSection_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, int mode)
        {
            var builder = new StringBuilder();
            switch (mode)
            {
                case 1:
                    builder.Append(""UPDATE Users SET Name = '"");
                    builder.Append(id);
                    return;
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeCaughtThrow_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            try
            {
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowCaughtByBaseType_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
			using System;
			using System.Text;" + EfMock + @"
	namespace TestApp
	{
	    public sealed class Program
	    {
	        public void Run(DbContext db, int id)
	        {
	            var builder = new StringBuilder();
	            try
	            {
	                builder.Append(""UPDATE Users SET Name = '"");
	                builder.Append(id);
	                throw new InvalidOperationException();
	            }
	            catch (SystemException)
	            {
	            }

	            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
	        }
	    }
	}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowCaughtByOrdinaryBaseType_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
				using System;
				using System.Text;" + EfMock + @"
		namespace TestApp
		{
		    public sealed class Program
		    {
		        public void Run(DbContext db, int id)
		        {
		            var builder = new StringBuilder();
		            try
		            {
		                builder.Append(""UPDATE Users SET Name = '"");
		                builder.Append(id);
		                throw new ArgumentNullException(nameof(id));
		            }
		            catch (ArgumentException)
		            {
		            }

		            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
		        }
		    }
		}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowCaughtByAliasType_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
				using System;
				using System.Text;
                using Alias = System.InvalidOperationException;" + EfMock + @"
			namespace TestApp
			{
			    public sealed class Program
			    {
			        public void Run(DbContext db, int id)
			        {
			            var builder = new StringBuilder();
			            try
			            {
			                builder.Append(""UPDATE Users SET Name = '"");
			                builder.Append(id);
			                throw new InvalidOperationException();
			            }
			            catch (Alias)
			            {
			            }

			            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
			        }
			    }
			}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeCustomThrowCaughtByBaseType_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
					using System;
					using System.Text;" + EfMock + @"
			namespace TestApp
			{
			    public sealed class Program
			    {
			        public void Run(DbContext db, int id)
			        {
			            var builder = new StringBuilder();
			            try
			            {
			                builder.Append(""UPDATE Users SET Name = '"");
			                builder.Append(id);
			                throw new MyException();
			            }
			            catch (InvalidOperationException)
			            {
			            }

			            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
			        }

                    private sealed class MyException : InvalidOperationException
                    {
                    }
			    }
			}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeCustomThrowNameSuffixNotCaughtByBaseType_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
						using System;
						using System.Text;" + EfMock + @"
				namespace TestApp
				{
				    public sealed class Program
				    {
				        public void Run(DbContext db, int id)
				        {
				            var builder = new StringBuilder();
				            try
				            {
				                builder.Append(""UPDATE Users SET Name = '"");
				                builder.Append(id);
				                throw new MyInvalidOperationException();
				            }
				            catch (InvalidOperationException)
				            {
				            }

				            var result = db.Database.ExecuteSqlRaw(builder.ToString());
				        }

	                    private sealed class MyInvalidOperationException : Exception
	                    {
	                    }
				    }
				}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowHandledByReturningFirstCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
						using System;
						using System.Text;" + EfMock + @"
					namespace TestApp
					{
					    public sealed class Program
					    {
					        public void Run(DbContext db, int id)
					        {
					            var builder = new StringBuilder();
					            try
					            {
					                builder.Append(""UPDATE Users SET Name = '"");
					                builder.Append(id);
					                throw new InvalidOperationException();
					            }
					            catch (InvalidOperationException)
					            {
					                return;
					            }
					            catch (Exception)
					            {
					            }

					            var result = db.Database.ExecuteSqlRaw(builder.ToString());
					        }
					    }
					}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowNotCaughtByApplicationException_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
						using System;
						using System.Text;" + EfMock + @"
					namespace TestApp
					{
					    public sealed class Program
					    {
					        public void Run(DbContext db, int id)
					        {
					            var builder = new StringBuilder();
					            try
					            {
					                builder.Append(""UPDATE Users SET Name = '"");
					                builder.Append(id);
					                throw new InvalidOperationException();
					            }
					            catch (ApplicationException)
					            {
					            }

					            var result = db.Database.ExecuteSqlRaw(builder.ToString());
					        }
					    }
					}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowCaughtByFalseFilter_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
						using System;
						using System.Text;" + EfMock + @"
					namespace TestApp
					{
					    public sealed class Program
					    {
					        public void Run(DbContext db, int id)
					        {
					            var builder = new StringBuilder();
					            try
					            {
					                builder.Append(""UPDATE Users SET Name = '"");
					                builder.Append(id);
					                throw new InvalidOperationException();
					            }
					            catch (InvalidOperationException) when (false)
					            {
					            }

					            var result = db.Database.ExecuteSqlRaw(builder.ToString());
					        }
					    }
					}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeCaughtThrowAndReturningCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
					using System;
	using System.Text;" + EfMock + @"
	namespace TestApp
	{
	    public sealed class Program
	    {
	        public void Run(DbContext db, int id)
	        {
	            var builder = new StringBuilder();
	            try
	            {
	                builder.Append(""UPDATE Users SET Name = '"");
	                builder.Append(id);
	                throw new InvalidOperationException();
	            }
	            catch (InvalidOperationException)
	            {
	                return;
	            }

	            var result = db.Database.ExecuteSqlRaw(builder.ToString());
	        }
	    }
	}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendLocalOverwrittenByOnlyReachingConstant_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
	namespace TestApp
	{
	    public sealed class Program
	    {
	        public void Run(DbContext db, int id, bool safe)
	        {
	            var builder = new StringBuilder();
	            var predicate = id.ToString();
	            if (safe)
	            {
	                predicate = ""Active = 1"";
	            }
	            else
	            {
	                return;
	            }

	            builder.Append(""UPDATE Users SET "");
	            builder.Append(predicate);
	            var result = db.Database.ExecuteSqlRaw(builder.ToString());
	        }
	    }
	}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConstructorTaintClearedInFluentChain_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder(id.ToString())
                .Clear()
                .Append(""UPDATE Users SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
