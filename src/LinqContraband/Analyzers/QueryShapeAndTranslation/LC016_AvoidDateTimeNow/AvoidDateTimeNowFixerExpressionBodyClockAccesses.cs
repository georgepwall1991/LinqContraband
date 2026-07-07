using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

public sealed partial class AvoidDateTimeNowFixer
{
    private static IReadOnlyList<ClockReplacement> BuildExpressionBodyReplacements(
        ExpressionSyntax expression,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var existingNames = CollectExistingNames(memberAccess);
        var replacements = new List<ClockReplacement>();

        foreach (var access in FindExpressionBodyClockAccesses(expression, semanticModel, cancellationToken))
        {
            var symbol = semanticModel.GetSymbolInfo(access, cancellationToken).Symbol;
            if (symbol is null ||
                replacements.Any(replacement => SymbolEqualityComparer.Default.Equals(replacement.Symbol, symbol)))
            {
                continue;
            }

            var variableName = GetUniqueVariableName(existingNames);
            existingNames.Add(variableName);
            replacements.Add(new ClockReplacement(symbol, access, variableName));
        }

        return replacements;
    }

    private static ClockReplacement? FindReplacementFor(
        MemberAccessExpressionSyntax access,
        IEnumerable<ClockReplacement> replacements,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(access, cancellationToken).Symbol;
        return replacements.FirstOrDefault(replacement => SymbolEqualityComparer.Default.Equals(replacement.Symbol, symbol));
    }

    private static IEnumerable<MemberAccessExpressionSyntax> FindExpressionBodyClockAccesses(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        expression.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(access => IsClockPropertyAccess(access, semanticModel, cancellationToken) &&
                             !IsInsideStaticLambda(access) &&
                             IsInsideQueryableLambda(access, semanticModel, cancellationToken));

    private static bool IsClockPropertyAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is not IPropertySymbol property)
            return false;

        if (property.Name is not ("Now" or "UtcNow"))
            return false;

        var containingType = property.ContainingType;
        return containingType.SpecialType == SpecialType.System_DateTime ||
               (containingType.Name == "DateTimeOffset" &&
                containingType.ContainingNamespace.ToDisplayString() == "System");
    }

    private sealed class ClockReplacement
    {
        public ClockReplacement(ISymbol symbol, MemberAccessExpressionSyntax initializer, string variableName)
        {
            Symbol = symbol;
            Initializer = initializer;
            VariableName = variableName;
        }

        public ISymbol Symbol { get; }
        public MemberAccessExpressionSyntax Initializer { get; }
        public string VariableName { get; }
    }
}
