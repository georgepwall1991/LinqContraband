using LinqContraband.Analyzers.LC009_MissingAsNoTracking;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// The LC009 verifier with <c>dotnet_code_quality.LC009.report_returned_entities = true</c>. LC009 is quiet by
/// default on queries whose entities the method returns; test classes whose cases return entities for other reasons
/// (the query shape, the message, the fix position) opt back in through this verifier.
/// </summary>
internal static class ReturnedEntitiesOptInVerifier
{
    public const string EditorConfig = """
        root = true

        [*.cs]
        dotnet_code_quality.LC009.report_returned_entities = true
        """;

    public static DiagnosticResult Diagnostic(string diagnosticId) =>
        CSharpCodeFixVerifier<MissingAsNoTrackingAnalyzer, MissingAsNoTrackingFixer, XUnitVerifier>.Diagnostic(diagnosticId);

    public static Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = CreateTest(source);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    public static Task VerifyCodeFixAsync(string source, string fixedSource)
    {
        var test = CreateTest(source);
        test.FixedCode = fixedSource;
        return test.RunAsync();
    }

    private static CSharpCodeFixTest<MissingAsNoTrackingAnalyzer, MissingAsNoTrackingFixer, XUnitVerifier> CreateTest(string source)
    {
        var test = new CSharpCodeFixTest<MissingAsNoTrackingAnalyzer, MissingAsNoTrackingFixer, XUnitVerifier>
        {
            TestCode = source
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", EditorConfig));
        return test;
    }
}
