using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC004_GuidInQuery.GuidInQueryAnalyzer,
    LinqContraband.Analyzers.LC004_GuidInQuery.GuidInQueryFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC004_GuidInQuery;

public class GuidInQueryFixerTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    [Fact]
    public async Task FixCrime_ExtractsNewGuidToVariable()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var result = query.Where(x => x == 1 && {|LC004:Guid.NewGuid()|} != Guid.Empty);
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
            var guid = Guid.NewGuid();
            var result = query.Where(x => x == 1 && guid != Guid.Empty);
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task FixCrime_ExtractsNewGuidConstructorToVariable()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            // Using string constructor for example
            var result = query.Select(x => {|LC004:new Guid(""00000000-0000-0000-0000-000000000000"")|});
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
            var guid = new Guid(""00000000-0000-0000-0000-000000000000"");
            // Using string constructor for example
            var result = query.Select(x => guid);
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

