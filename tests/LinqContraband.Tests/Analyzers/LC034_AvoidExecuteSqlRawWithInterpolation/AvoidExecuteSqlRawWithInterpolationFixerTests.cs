using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation.AvoidExecuteSqlRawWithInterpolationFixer>;

namespace LinqContraband.Tests.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation;

public partial class AvoidExecuteSqlRawWithInterpolationTests
{
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
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsSqlIdentifier()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, string tableName)
        {
            var result = db.Database.ExecuteSqlRaw($""DELETE FROM {tableName}"");
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC034").WithSpan(31, 52, 31, 78).WithArguments("ExecuteSql", "ExecuteSqlRaw");
        await VerifyFix.VerifyCodeFixAsync(test, expected, test);
    }

    [Theory]
    [InlineData(@"$""UPDATE {tableId} SET IsActive = 0""")]
    [InlineData(@"$""DELETE FROM Users WHERE {columnId} = 1""")]
    [InlineData(@"$""EXEC {procedureId} {id}""")]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsSqlStructure(string sqlExpression)
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(
            DbContext db,
            int tableId,
            int columnId,
            int procedureId,
            int id)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:" + sqlExpression + @"|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenStringInterpolationCouldBeSqlStructure()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, string otherColumn)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""DELETE FROM Users WHERE Id = {otherColumn}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Theory]
    [InlineData("int value")]
    [InlineData("int? value")]
    [InlineData("decimal value")]
    [InlineData("System.DateTime value")]
    [InlineData("System.DateTimeOffset value")]
    [InlineData("System.Guid value")]
    [InlineData("System.TimeSpan value")]
    public async Task Fixer_ShouldRegister_ForSupportedFrameworkScalar(string parameterDeclaration)
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, " + parameterDeclaration + @")
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Value = {value}""|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, " + parameterDeclaration + @")
        {
            var result = db.Database.ExecuteSql($""UPDATE Users SET Value = {value}"");
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldRegister_WhenValueInterpolationsHaveDistinctPositions()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int major, int minor)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Major = {major}, Minor = {minor}""|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int major, int minor)
        {
            var result = db.Database.ExecuteSql($""UPDATE Users SET Major = {major}, Minor = {minor}"");
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenValueTypeIsUserDefined()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public readonly struct CustomValue { }

    public sealed class Program
    {
        public void Run(DbContext db, CustomValue value)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Value = {value}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenValueTypeIsEnum()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public enum CustomValue { None }

    public sealed class Program
    {
        public void Run(DbContext db, CustomValue value)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Value = {value}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenValueTypeIsGenericParameter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run<T>(DbContext db, T value) where T : struct
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Value = {value}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenValueInterpolationsAreAdjacent()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int major, int minor)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Code = {major}{minor}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForProviderDirectiveValue()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int pages)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""PRAGMA cache_size = {pages}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenAnotherStatementFollowsInterpolation()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int value)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Value = {value}; DELETE FROM Audit""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Theory]
    [InlineData("Guid")]
    [InlineData("DateTimeOffset")]
    [InlineData("TimeSpan")]
    public async Task Fixer_ShouldNotRegister_ForFrameworkScalarLookalike(string typeName)
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
#pragma warning disable CS0436
namespace System
{
    public readonly struct " + typeName + @" { }
}

namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, global::System." + typeName + @" value)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Value = {value}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationFollowsBackslashEscapedQuote()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int value)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE Users SET Name = E'can\\'t = {value}'""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenQuotedIdentifierApostrophePrecedesSqlLiteral()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int value)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE [User's] SET Name = 'prefix={value}'""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenQuotedIdentifierApostrophePrecedesStructuralHole()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int columnId)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:$""UPDATE [User's] SET {columnId} = 1""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Theory]
    [InlineData("$\"UPDATE [Tenant={tenantId}] SET IsActive = 0\"")]
    [InlineData("$\"UPDATE \\\"Tenant={tenantId}\\\" SET IsActive = 0\"")]
    [InlineData("$\"UPDATE `Tenant={tenantId}` SET IsActive = 0\"")]
    [InlineData("$\"UPDATE [Tenant]]Name={tenantId}] SET IsActive = 0\"")]
    [InlineData("$\"UPDATE \\\"Tenant\\\"\\\"Name={tenantId}\\\" SET IsActive = 0\"")]
    [InlineData("$\"UPDATE `Tenant``Name={tenantId}` SET IsActive = 0\"")]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsInsideDelimitedIdentifier(string sqlExpression)
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int tenantId)
        {
            var result = db.Database.ExecuteSqlRaw({|LC034:" + sqlExpression + @"|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
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

    [Fact]
    public async Task FixAll_RewritesAllExecuteSqlRawCalls()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id, int deletedId)
        {
            var result1 = db.Database.ExecuteSqlRaw({|#0:$""UPDATE Users SET Name = {id}""|});
            var result2 = db.Database.ExecuteSqlRaw({|#1:$""UPDATE Users SET IsActive = 1 WHERE Id = {id}""|});
            var result3 = db.Database.ExecuteSqlRaw({|#2:$""DELETE FROM Users WHERE Id = {deletedId}""|});
            var result4 = await db.Database.ExecuteSqlRawAsync({|#3:$""UPDATE Users SET IsActive = 0 WHERE Id = {id}""|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public async Task Run(DbContext db, int id, int deletedId)
        {
            var result1 = db.Database.ExecuteSql($""UPDATE Users SET Name = {id}"");
            var result2 = db.Database.ExecuteSql($""UPDATE Users SET IsActive = 1 WHERE Id = {id}"");
            var result3 = db.Database.ExecuteSql($""DELETE FROM Users WHERE Id = {deletedId}"");
            var result4 = await db.Database.ExecuteSqlAsync($""UPDATE Users SET IsActive = 0 WHERE Id = {id}"");
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 4,
            CodeFixEquivalenceKey = "UseSafeExecuteSql"
        };

        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC034")
                .WithLocation(0)
                .WithArguments("ExecuteSql", "ExecuteSqlRaw"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC034")
                .WithLocation(1)
                .WithArguments("ExecuteSql", "ExecuteSqlRaw"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC034")
                .WithLocation(2)
                .WithArguments("ExecuteSql", "ExecuteSqlRaw"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC034")
                .WithLocation(3)
                .WithArguments("ExecuteSqlAsync", "ExecuteSqlRawAsync"));

        await testObj.RunAsync();
    }
}
