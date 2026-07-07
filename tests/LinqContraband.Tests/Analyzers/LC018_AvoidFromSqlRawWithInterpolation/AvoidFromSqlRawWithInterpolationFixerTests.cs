using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationFixer>;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

public partial class AvoidFromSqlRawWithInterpolationTests
{
    [Fact]
    public async Task Fixer_ShouldReplaceFromSqlRawWithFromSqlInterpolated()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlInterpolated($""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        var expected = VerifyFix.Diagnostic("LC018").WithSpan(22, 43, 22, 81).WithArguments("FromSqlInterpolated", "FromSqlRaw");
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceNamedFromSqlRawWithFromSqlInterpolated()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw(sql: {|LC018:$""SELECT * FROM Table WHERE Id = {id}""|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlInterpolated(sql: $""SELECT * FROM Table WHERE Id = {id}"");
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenRawParametersArePresent()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw(parameters: new object[0], sql: {|LC018:$""SELECT * FROM Table WHERE Id = {id}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsInsideSqlStringLiteral()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var name = ""admin"";
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Table WHERE Name = '{name}'""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsSqlIdentifier()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var tableName = ""Users"";
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM {tableName} WHERE IsActive = 1""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsSelectListIdentifier()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var columnName = ""Id"";
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT {columnName} FROM Users""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsPredicateIdentifier()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var columnName = ""IsActive"";
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Users WHERE {columnName} = 1""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_WhenInterpolationIsStoredProcedureIdentifier()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var procedureName = ""dbo.GetUsers"";
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""EXEC {procedureName} {id}""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForConcatenation()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:""SELECT * FROM Table WHERE Id = "" + id|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task FixAll_RewritesAllFromSqlRawWithInterpolatedStringInstances()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id1 = 1;
            var id2 = 2;
            var query = new int[0].AsQueryable();
            var result1 = query.FromSqlRaw({|#0:$""SELECT * FROM Table WHERE Id = {id1}""|});
            var result2 = query.FromSqlRaw({|#1:$""SELECT * FROM Table WHERE Id = {id2}""|});
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id1 = 1;
            var id2 = 2;
            var query = new int[0].AsQueryable();
            var result1 = query.FromSqlInterpolated($""SELECT * FROM Table WHERE Id = {id1}"");
            var result2 = query.FromSqlInterpolated($""SELECT * FROM Table WHERE Id = {id2}"");
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "ReplaceFromSqlRawWithInterpolated"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC018", DiagnosticSeverity.Warning)
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC018", DiagnosticSeverity.Warning)
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
