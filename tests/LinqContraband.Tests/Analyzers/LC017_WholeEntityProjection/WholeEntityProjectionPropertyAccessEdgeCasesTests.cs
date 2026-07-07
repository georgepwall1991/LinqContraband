using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC017_WholeEntityProjection.WholeEntityProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC017_WholeEntityProjection;

public partial class WholeEntityProjectionEdgeCasesTests
{
    /// <summary>
    /// Innocent: Null-conditional operator changes the operation type.
    /// The analyzer doesn't track through conditional access.
    /// </summary>
    [Fact]
    public async Task TestCrime_NullConditionalPropertyAccess_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e?.Name);
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Crime: Property accessed via method chain.
    /// </summary>
    [Fact]
    public async Task TestCrime_PropertyMethodChain_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine(e.Name.ToUpper());
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Crime: Property accessed in string interpolation.
    /// </summary>
    [Fact]
    public async Task TestCrime_StringInterpolation_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            Console.WriteLine($""Name: {e.Name}"");
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    /// <summary>
    /// Crime: Property accessed in binary expression.
    /// </summary>
    [Fact]
    public async Task TestCrime_BinaryExpression_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public void Process()
    {
        var db = new MyDbContext();
        var entities = db.LargeEntities.ToList();
        foreach (var e in entities)
        {
            var x = e.Id + 1;
        }
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC017")
            .WithSpan(14, 24, 14, 49)
            .WithArguments("LargeEntity", 1, 13);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
