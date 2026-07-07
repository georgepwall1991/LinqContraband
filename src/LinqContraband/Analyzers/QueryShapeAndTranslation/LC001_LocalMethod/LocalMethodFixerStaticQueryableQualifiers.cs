using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodFixer
{
    private static ExpressionSyntax CreateEnumerableQualifier(ExpressionSyntax qualifier)
    {
        return TryGetEnumerableQualifier(qualifier, out var rewrittenQualifier)
            ? rewrittenQualifier
            : SyntaxFactory.ParseExpression("System.Linq.Enumerable").WithTriviaFrom(qualifier);
    }

    private static bool IsSystemLinqQueryableType(
        SemanticModel? semanticModel,
        ExpressionSyntax qualifier,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null) return false;

        var symbol = semanticModel.GetSymbolInfo(qualifier, cancellationToken).Symbol;
        if (IsSystemLinqQueryableTypeSymbol(symbol)) return true;

        return qualifier is NameSyntax nameSyntax &&
               semanticModel.GetAliasInfo(nameSyntax, cancellationToken)?.Target is INamedTypeSymbol aliasTarget &&
               IsSystemLinqQueryableTypeSymbol(aliasTarget);
    }

    private static bool CanSwitchStaticQueryableMethodToEnumerable(
        SemanticModel? semanticModel,
        MemberAccessExpressionSyntax memberAccess)
    {
        if (semanticModel == null) return false;

        var methodName = memberAccess.Name.Identifier.ValueText;
        var enumerableType = semanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");

        return enumerableType?.GetMembers(methodName).OfType<IMethodSymbol>().Any() == true;
    }

    private static bool IsSystemLinqQueryableTypeSymbol(ISymbol? symbol)
    {
        return symbol is INamedTypeSymbol typeSymbol &&
               typeSymbol.Name == "Queryable" &&
               typeSymbol.ContainingNamespace.ToDisplayString() == "System.Linq";
    }

    private static bool TryGetEnumerableQualifier(ExpressionSyntax qualifier, out ExpressionSyntax enumerableQualifier)
    {
        enumerableQualifier = qualifier;

        if (qualifier is IdentifierNameSyntax identifierName &&
            identifierName.Identifier.ValueText == "Queryable")
        {
            enumerableQualifier = SyntaxFactory.ParseExpression("System.Linq.Enumerable")
                .WithTriviaFrom(identifierName);

            return true;
        }

        if (qualifier is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "Queryable")
        {
            var enumerableName = SyntaxFactory.IdentifierName(
                    SyntaxFactory.Identifier(
                        memberAccess.Name.Identifier.LeadingTrivia,
                        "Enumerable",
                        memberAccess.Name.Identifier.TrailingTrivia))
                .WithTriviaFrom(memberAccess.Name);

            enumerableQualifier = memberAccess.WithName(enumerableName);
            return true;
        }

        return false;
    }

    private static bool IsThenBy(string methodName)
    {
        return methodName is "ThenBy" or "ThenByDescending";
    }

    private static bool IsOrderingMethod(string methodName)
    {
        return methodName is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending";
    }
}
