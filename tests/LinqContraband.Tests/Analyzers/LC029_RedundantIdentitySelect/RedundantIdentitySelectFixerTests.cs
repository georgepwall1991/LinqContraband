using Microsoft.CodeAnalysis.Testing;
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
}
