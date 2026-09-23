using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC026_MissingCancellationToken;

public sealed partial class MissingCancellationTokenAnalyzer
{
    private bool IsCandidateAsyncEfMethod(IMethodSymbol method, out IParameterSymbol? cancellationTokenParameter)
    {
        cancellationTokenParameter = null;

        if (!method.Name.EndsWith("Async", System.StringComparison.Ordinal))
            return false;

        if (!IsEfCoreMethod(method))
            return false;

        cancellationTokenParameter = method.Parameters.FirstOrDefault(IsCancellationTokenParameter);
        return cancellationTokenParameter != null;
    }

    private static bool IsCancellationTokenParameter(IParameterSymbol parameter)
    {
        return IsCancellationTokenType(parameter.Type);
    }

    private static IArgumentOperation? FindCancellationTokenArgument(
        IInvocationOperation invocation,
        IParameterSymbol cancellationTokenParameter)
    {
        return invocation.Arguments.FirstOrDefault(argument =>
            SymbolEqualityComparer.Default.Equals(argument.Parameter, cancellationTokenParameter));
    }

    private bool IsEfCoreMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        return ns != null &&
               (ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) ||
                ns.StartsWith("System.Data.Entity", System.StringComparison.Ordinal));
    }

    private static bool IsCancellationTokenType(ITypeSymbol type)
    {
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToString() == "System.Threading";
    }

    private static bool IsUsingDefault(IOperation operation)
    {
        return operation.UnwrapConversions().Kind == OperationKind.DefaultValue;
    }

    private static bool IsUsingCancellationTokenNone(IOperation operation)
    {
        return operation.UnwrapConversions() is IPropertyReferenceOperation propRef &&
               propRef.Property.Name == "None" &&
               IsCancellationTokenType(propRef.Property.ContainingType);
    }

    /// <summary>
    /// <c>CancellationToken.None</c> is the explicit way to say an operation must not be cancelled (audit writes,
    /// compensating saves in <c>finally</c>), so it is only reported when
    /// <c>dotnet_code_quality.LC026.report_explicit_none = true</c>.
    /// </summary>
    private static bool ReportsExplicitNone(AnalyzerOptions options, SyntaxTree syntaxTree)
    {
        return options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree)
                   .TryGetValue("dotnet_code_quality." + DiagnosticId + ".report_explicit_none", out var value) &&
               string.Equals(value.Trim(), "true", System.StringComparison.OrdinalIgnoreCase);
    }
}
