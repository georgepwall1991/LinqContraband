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

        var expected = VerifyCS.Diagnostic("LC034").WithSpan(31, 52, 31, 83).WithArguments("ExecuteSqlRaw");
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

        var expected = VerifyCS.Diagnostic("LC034").WithSpan(31, 63, 31, 94).WithArguments("ExecuteSqlRawAsync");
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

        var expected = VerifyCS.Diagnostic("LC034").WithSpan(31, 52, 31, 83).WithArguments("ExecuteSqlRaw");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
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

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(31, 52, 31, 83).WithArguments("ExecuteSqlRaw");
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

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(32, 63, 32, 94).WithArguments("ExecuteSqlRawAsync");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
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

        var expected = VerifyCS.Diagnostic("LC034").WithSpan(31, 52, 31, 83).WithArguments("ExecuteSqlRaw");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
