using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateAnalyzer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

/// <summary>
/// LC035 has a live descriptor and a compilation-end one for reports that depend on a helper's callers;
/// markup only needs the id and location.
/// </summary>
internal static class MissingWhereBeforeExecuteDeleteUpdateVerifier
{
    public static Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest
        {
            TestCode = source,
            MarkupOptions = MarkupOptions.UseFirstDescriptor
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }
}
