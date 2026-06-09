using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC005_MultipleOrderBy.MultipleOrderByAnalyzer,
    LinqContraband.Analyzers.LC005_MultipleOrderBy.MultipleOrderByFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC005_MultipleOrderBy.MultipleOrderByAnalyzer,
    LinqContraband.Analyzers.LC005_MultipleOrderBy.MultipleOrderByFixer>;

namespace LinqContraband.Tests.Analyzers.LC005_MultipleOrderBy;

public class MultipleOrderByFixerTests
{
    [Fact]
    public async Task ExplicitGenericOrderBy_PreservesTypeArguments()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy<int, int>(x => x).{|LC005:OrderBy<int, int>|}(x => x);
    }
}";
        var fix = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list.OrderBy<int, int>(x => x).ThenBy<int, int>(x => x);
    }
}";

        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task ThenByDescendingOrderByDescending_RewritesToThenByDescending()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list
            .OrderBy(x => x)
            .ThenByDescending(x => x)
            .{|LC005:OrderByDescending|}(x => x);
    }
}";
        var fix = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list
            .OrderBy(x => x)
            .ThenByDescending(x => x)
            .ThenByDescending(x => x);
    }
}";

        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixAll_ReplacesAllConsecutiveOrderBysWithThenBy()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list
            .OrderBy(x => x)
            .{|#0:OrderBy|}(x => x)
            .{|#1:OrderBy|}(x => x);
    }
}";
        var fix = @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(List<int> list)
    {
        var q = list
            .OrderBy(x => x)
            .ThenBy(x => x)
            .ThenBy(x => x);
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fix,
            BatchFixedCode = fix,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "MultipleOrderByFixer"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC005", DiagnosticSeverity.Warning)
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC005", DiagnosticSeverity.Warning)
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
