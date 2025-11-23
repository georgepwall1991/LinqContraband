using Microsoft.CodeAnalysis.Testing;
using CodeFixTest =
    Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
        LinqContraband.Analyzers.LC003_AnyOverCount.AnyOverCountAnalyzer,
        LinqContraband.Analyzers.LC003_AnyOverCount.AnyOverCountFixer,
        Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC003_AnyOverCount;

public class AnyOverCountFixerTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    [Fact]
    public async Task CountGreaterThanZero_ShouldBeReplacedWithAny()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count() > 0|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Any())
            {
            }
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task CountWithPredicateGreaterThanZero_ShouldBeReplacedWithAnyPredicate()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count(x => x > 10) > 0|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Any(x => x > 10))
            {
            }
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task ZeroLessThanCount_ShouldBeReplacedWithAny()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:0 < query.Count()|})
            {
            }
        }
    }
}";
        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Any())
            {
            }
        }
    }
}";
        await VerifyFix(test, fixedCode);
    }

    private static async Task VerifyFix(string test, string fixedCode)
    {
        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        // VerifyCS handles constructing the diagnostic expectation if using the helper,
        // but here we are using CodeFixTest directly.
        // The {|LC003:..|} syntax in TestCode handles the location assertion.

        await testObj.RunAsync();
    }
}