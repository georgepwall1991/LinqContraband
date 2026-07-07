using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC028_DeepThenInclude;

public sealed partial class DeepThenIncludeAnalyzer
{
    private static int CountThenIncludeDepth(IInvocationOperation invocation)
    {
        var depth = 1;
        var current = invocation.GetInvocationReceiver();

        while (current is IInvocationOperation previousInvocation)
        {
            var previousMethod = previousInvocation.TargetMethod;
            if (previousMethod.Name != "ThenInclude" || !IsEfCoreMethod(previousMethod))
                break;

            depth++;
            current = previousInvocation.GetInvocationReceiver();
        }

        return depth;
    }

    private static Location GetDiagnosticLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax memberAccess
            })
        {
            return invocation.Syntax.GetLocation();
        }

        var textSpan = TextSpan.FromBounds(
            memberAccess.OperatorToken.SpanStart,
            invocation.Syntax.Span.End);

        return Location.Create(invocation.Syntax.SyntaxTree, textSpan);
    }
}
