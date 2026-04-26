using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationFixer>;

namespace LinqContraband.Tests.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation;

public class AvoidExecuteSqlRawWithInterpolationTests
{
    private const string EfMock = @"
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public sealed class DatabaseFacade { }

    public sealed class DbContext
    {
        public DatabaseFacade Database { get; } = new DatabaseFacade();
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static int ExecuteSqlRaw(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => 0;
        public static Task<int> ExecuteSqlRawAsync(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => Task.FromResult(0);
        public static int ExecuteSql(this DatabaseFacade databaseFacade, FormattableString sql) => 0;
        public static Task<int> ExecuteSqlAsync(this DatabaseFacade databaseFacade, FormattableString sql, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
";

    [Fact]
    public async Task ExecuteSqlRaw_WithInterpolatedString_ShouldTrigger()
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

        var expected = VerifyCS.Diagnostic("LC034").WithSpan(31, 52, 31, 83).WithArguments("ExecuteSql", "ExecuteSqlRaw");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExecuteSqlRawAsync_WithInterpolatedString_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
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

        var expected = VerifyCS.Diagnostic("LC034").WithSpan(31, 63, 31, 94).WithArguments("ExecuteSqlAsync", "ExecuteSqlRawAsync");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstantString_ShouldNotTrigger()
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
    public async Task ExecuteSqlRaw_WithConcatenation_ShouldTrigger()
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

        var expected = VerifyCS.Diagnostic("LC034").WithSpan(31, 52, 31, 83).WithArguments("ExecuteSql", "ExecuteSqlRaw");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithNestedConcatenation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, string name)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:""UPDATE Users SET Name = "" + name + "" WHERE Id = "" + id|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithNamedSqlArgument_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw(parameters: new object[0], sql: {|LC034:$""UPDATE Users SET Name = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithInterpolatedAlias_ShouldNotTriggerLC034()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = $""UPDATE Users SET Name = {id}"";
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithParameterizedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw(""UPDATE Users SET Active = 1 WHERE Id = {0}"", id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConcatenatedAlias_ShouldNotTriggerLC034()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = ""UPDATE Users SET Name = "" + id;
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringFormat_ShouldNotTriggerLC034()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw(string.Format(""UPDATE Users SET Name = {0}"", id));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilder_ShouldNotTriggerLC034()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder(""UPDATE Users SET Name = "");
            builder.Append(id);
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSql_WithInterpolatedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSql($""UPDATE Users SET Name = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlAsync_WithInterpolatedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id)
        {
            var result = await db.Database.ExecuteSqlAsync($""UPDATE Users SET Name = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceExecuteSqlRawWithExecuteSql()
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

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSql($""UPDATE Users SET Name = {id}"");
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(31, 52, 31, 83).WithArguments("ExecuteSql", "ExecuteSqlRaw");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceExecuteSqlRawAsyncWithExecuteSqlAsync()
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

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id)
        {
            var result = await db.Database.ExecuteSqlAsync($""UPDATE Users SET Name = {id}"");
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(32, 63, 32, 94).WithArguments("ExecuteSqlAsync", "ExecuteSqlRawAsync");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceNamedExecuteSqlRawWithExecuteSql()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw(sql: $""UPDATE Users SET Name = {id}"");
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSql(sql: $""UPDATE Users SET Name = {id}"");
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(31, 57, 31, 88).WithArguments("ExecuteSql", "ExecuteSqlRaw");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenRawParametersArePresent()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = db.Database.ExecuteSqlRaw(parameters: new object[0], sql: $""UPDATE Users SET Name = {id}"");
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(31, 84, 31, 115).WithArguments("ExecuteSql", "ExecuteSqlRaw");
        await VerifyFix.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsInsideSqlStringLiteral()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, string name)
        {
            var result = db.Database.ExecuteSqlRaw($""DELETE FROM Users WHERE Name = '{name}'"");
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(31, 52, 31, 94).WithArguments("ExecuteSql", "ExecuteSqlRaw");
        await VerifyFix.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForConcatenation()
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

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(31, 52, 31, 83).WithArguments("ExecuteSql", "ExecuteSqlRaw");
        await VerifyFix.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForInterpolatedAlias()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = $""UPDATE Users SET Name = {id}"";
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }
}
