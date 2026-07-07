using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC036_DbContextCapturedAcrossThreads;

public sealed partial class DbContextCapturedAcrossThreadsAnalyzer
{
    private static bool TryFindCapturedDbContextInLocalFunctionCallback(
        SyntaxNode syntax,
        SemanticModel semanticModel,
        out ISymbol capturedSymbol)
    {
        if (semanticModel.GetSymbolInfo(syntax).Symbol is not IMethodSymbol
            {
                MethodKind: MethodKind.LocalFunction
            } localFunction)
        {
            capturedSymbol = null!;
            return false;
        }

        var syntaxReference = localFunction.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxReference?.GetSyntax() is not LocalFunctionStatementSyntax localFunctionSyntax)
        {
            capturedSymbol = null!;
            return false;
        }

        SyntaxNode? body = localFunctionSyntax.Body ?? (SyntaxNode?)localFunctionSyntax.ExpressionBody?.Expression;
        if (body is null)
        {
            capturedSymbol = null!;
            return false;
        }

        foreach (var descendant in body.DescendantNodesAndSelf())
        {
            if (descendant is IdentifierNameSyntax or GenericNameSyntax or SimpleNameSyntax or MemberAccessExpressionSyntax)
            {
                var symbol = semanticModel.GetSymbolInfo(descendant).Symbol;
                if (symbol is null)
                    continue;

                if (IsCapturedDbContext(symbol, localFunctionSyntax.Span))
                {
                    capturedSymbol = symbol;
                    return true;
                }
            }
        }

        capturedSymbol = null!;
        return false;
    }
}
