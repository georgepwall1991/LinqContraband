using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC029_RedundantIdentitySelect.RedundantIdentitySelectAnalyzer>;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC029_RedundantIdentitySelect.RedundantIdentitySelectAnalyzer,
    LinqContraband.Analyzers.LC029_RedundantIdentitySelect.RedundantIdentitySelectFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC029_RedundantIdentitySelect;

public class RedundantIdentitySelectFixerTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    [Fact]
    public async Task SelectIdentity_InChain_ShouldBeRemoved()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var result = {|LC029:query.Select(x => x)|}.ToList();
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
            var result = query.ToList();
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task SelectIdentity_Standalone_ShouldBeRemoved()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var result = {|LC029:query.Select(x => x)|};
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
            var result = query;
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task SelectIdentity_AfterAsEnumerable_ShouldPreserveBoundary()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var result = {|LC029:query.AsEnumerable().Select(x => x)|}.ToList();
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
            var result = query.AsEnumerable().ToList();
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

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixAll_RemovesAllRedundantIdentitySelects()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var result1 = {|#0:query.Select(x => x)|}.ToList();
            var result2 = {|#1:query.Select(x => x)|};
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
            var result1 = query.ToList();
            var result2 = query;
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "RemoveRedundantSelect",
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC029", DiagnosticSeverity.Info)
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC029", DiagnosticSeverity.Info)
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
