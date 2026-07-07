using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationFixer>;

namespace LinqContraband.Tests.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation;

public partial class AvoidExecuteSqlRawWithInterpolationTests
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
    public async Task ExecuteSqlRaw_WithNoHoleInterpolatedString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var result = db.Database.ExecuteSqlRaw($""UPDATE Users SET Active = 1"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstantOnlyInterpolation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        private const int Active = 1;

        public void Run(DbContext db)
        {
            var result = db.Database.ExecuteSqlRaw($""UPDATE Users SET Active = {Active}"");
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
    public async Task ExecuteSqlRaw_InLookalikeNamespace_ShouldNotTrigger()
    {
        var test = @"
using System;
using Microsoft.EntityFrameworkCoreFake;

namespace Microsoft.EntityFrameworkCoreFake
{
    public sealed class DatabaseFacade { }

    public sealed class DbContext
    {
        public DatabaseFacade Database { get; } = new DatabaseFacade();
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static int ExecuteSqlRaw(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => 0;
    }
}

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
    public async Task ExecuteSqlRaw_OnNonDatabaseFacadeEfNamespaceHelper_ShouldNotTrigger()
    {
        var test = @"
using System;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class CustomRawExtensions
    {
        public static int ExecuteSqlRaw(this string target, string sql, params object[] parameters) => 0;
    }
}

namespace TestApp
{
    public sealed class Program
    {
        public void Run(int id)
        {
            var result = ""not a database"".ExecuteSqlRaw($""UPDATE Users SET Name = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_AsStaticExtensionCall_WithUnsafeInterpolation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(db.Database, {|LC034:$""UPDATE Users SET Name = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRawAsync_AsStaticExtensionCall_WithUnsafeInterpolation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id)
        {
            var result = await RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(db.Database, {|LC034:$""UPDATE Users SET Name = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSql_AsStaticExtensionCall_WithSafeInterpolation_ShouldNotTrigger()
    {
        // Safe sibling API used in static-extension form must stay quiet.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var result = RelationalDatabaseFacadeExtensions.ExecuteSql(db.Database, $""UPDATE Users SET Name = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlAsync_AsStaticExtensionCall_WithSafeInterpolation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id)
        {
            var result = await RelationalDatabaseFacadeExtensions.ExecuteSqlAsync(db.Database, $""UPDATE Users SET Name = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
