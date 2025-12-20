using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationFixer>;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

public class AvoidFromSqlRawWithInterpolationTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;

namespace Microsoft.EntityFrameworkCore
{
    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(this IQueryable<TEntity> source, string sql, params object[] parameters) => source;
        public static IQueryable<TEntity> FromSqlInterpolated<TEntity>(this IQueryable<TEntity> source, FormattableString sql) => source;
    }
}
";

    [Fact]
    public async Task FromSqlRaw_WithInterpolatedString_ShouldTriggerLC018()
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
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Table WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConcatenatedString_ShouldTriggerLC018()
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

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConstantString_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw(""SELECT * FROM Table"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithParameterizedString_ShouldNotTrigger()
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
            var result = query.FromSqlRaw(""SELECT * FROM Table WHERE Id = {0}"", id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

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

        var expected = VerifyFix.Diagnostic("LC018").WithSpan(22, 43, 22, 81);
        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
