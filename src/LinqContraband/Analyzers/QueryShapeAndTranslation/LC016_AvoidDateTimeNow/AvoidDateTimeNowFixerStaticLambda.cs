using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

public sealed partial class AvoidDateTimeNowFixer
{
    private static bool IsInsideStaticLambda(SyntaxNode node) =>
        node.AncestorsAndSelf()
            .OfType<LambdaExpressionSyntax>()
            .Any(static lambda => lambda switch
            {
                ParenthesizedLambdaExpressionSyntax parenthesized => HasStaticModifier(parenthesized.Modifiers),
                SimpleLambdaExpressionSyntax simple => HasStaticModifier(simple.Modifiers),
                _ => false
            });

    private static bool HasStaticModifier(SyntaxTokenList modifiers) =>
        modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword));
}
