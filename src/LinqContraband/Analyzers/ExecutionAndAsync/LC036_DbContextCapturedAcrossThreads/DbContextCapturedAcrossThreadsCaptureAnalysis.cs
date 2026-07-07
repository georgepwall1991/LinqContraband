using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC036_DbContextCapturedAcrossThreads;

public sealed partial class DbContextCapturedAcrossThreadsAnalyzer
{
    private static bool TryFindCapturedDbContext(SyntaxNode syntax, SemanticModel semanticModel, out ISymbol capturedSymbol)
    {
        var lambdaSyntax = syntax.AncestorsAndSelf().FirstOrDefault(node =>
            node is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax);

        if (lambdaSyntax is not null)
        {
            return TryFindCapturedDbContextInLambda(lambdaSyntax, semanticModel, out capturedSymbol);
        }

        foreach (var descendant in syntax.DescendantNodesAndSelf())
        {
            if (descendant is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                if (TryFindCapturedDbContextInLambda(descendant, semanticModel, out capturedSymbol))
                    return true;
            }
        }

        foreach (var descendant in syntax.DescendantNodesAndSelf())
        {
            if (TryFindCapturedDbContextInLocalFunctionCallback(descendant, semanticModel, out capturedSymbol))
                return true;
        }

        capturedSymbol = null!;
        return false;
    }

    private static bool TryFindCapturedDbContextInLambda(
        SyntaxNode lambdaSyntax,
        SemanticModel semanticModel,
        out ISymbol capturedSymbol)
    {
        var body = lambdaSyntax switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Body,
            SimpleLambdaExpressionSyntax simple => simple.Body,
            AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
            _ => null
        };

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
                {
                    continue;
                }

                if (IsCapturedDbContext(symbol, lambdaSyntax.Span))
                {
                    capturedSymbol = symbol;
                    return true;
                }
            }
        }

        capturedSymbol = null!;
        return false;
    }

    private static bool IsCapturedDbContext(ISymbol symbol, TextSpan lambdaSpan)
    {
        if (symbol is not ILocalSymbol and not IParameterSymbol and not IFieldSymbol and not IPropertySymbol)
            return false;

        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        if (type == null || !type.IsDbContext())
            return false;

        if (symbol.DeclaringSyntaxReferences.Length == 0)
            return true;

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (lambdaSpan.Contains(syntax.Span))
                return false;
        }

        return true;
    }
}
