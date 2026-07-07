using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

public sealed partial class AsNoTrackingWithUpdateFixer
{
    private static bool HasAsNoTrackingInChain(IInvocationOperation invocation)
    {
        IOperation current = invocation;
        while (current.UnwrapConversions() is IInvocationOperation currentInvocation)
        {
            if (IsEfCoreNoTrackingDirective(currentInvocation.TargetMethod))
                return true;

            if (IsEfCoreAsTracking(currentInvocation.TargetMethod))
                return false;

            var receiver = currentInvocation.GetInvocationReceiver();
            if (receiver == null)
                return false;

            current = receiver;
        }

        return false;
    }

    private static bool IsEfCoreNoTrackingDirective(IMethodSymbol method)
    {
        if (method.Name is not ("AsNoTracking" or "AsNoTrackingWithIdentityResolution"))
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

    private static bool IsEfCoreAsTracking(IMethodSymbol method)
    {
        if (method.Name != "AsTracking")
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

    private static InvocationExpressionSyntax? FindAsNoTrackingInvocation(ExpressionSyntax expression)
    {
        return expression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText is "AsNoTracking" or "AsNoTrackingWithIdentityResolution" &&
                invocation.ArgumentList.Arguments.Count == 0);
    }
}
