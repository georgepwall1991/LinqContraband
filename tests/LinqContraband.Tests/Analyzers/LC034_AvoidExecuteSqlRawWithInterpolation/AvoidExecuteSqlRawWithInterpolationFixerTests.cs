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
        public void Run(DbContext db, int id, string name)
        {
            var result1 = db.Database.ExecuteSqlRaw({|#0:$""UPDATE Users SET Name = {id}""|});
            var result2 = db.Database.ExecuteSqlRaw({|#1:$""UPDATE Users SET IsActive = 1 WHERE Id = {id}""|});
            var result3 = db.Database.ExecuteSqlRaw({|#2:$""DELETE FROM Users WHERE Name = {name}""|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, string name)
        {
            var result1 = db.Database.ExecuteSql($""UPDATE Users SET Name = {id}"");
            var result2 = db.Database.ExecuteSql($""UPDATE Users SET IsActive = 1 WHERE Id = {id}"");
            var result3 = db.Database.ExecuteSql($""DELETE FROM Users WHERE Name = {name}"");
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 3,
            CodeFixEquivalenceKey = "ExecuteSql"
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

        await testObj.RunAsync();
    }
}
