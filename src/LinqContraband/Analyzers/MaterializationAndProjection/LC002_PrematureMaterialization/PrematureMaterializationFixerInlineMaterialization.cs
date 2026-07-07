using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationFixer
{
    private static bool IsInlineMaterializerReceiver(InvocationExpressionSyntax invocation)
    {
        return TryGetInlineMaterializerParts(invocation, out _, out _, out _);
    }

    private static bool TryGetInlineMaterializerParts(
        InvocationExpressionSyntax invocation,
        out MemberAccessExpressionSyntax currentMemberAccess,
        out InvocationExpressionSyntax previousInvocation,
        out MemberAccessExpressionSyntax previousMemberAccess)
    {
        currentMemberAccess = null!;
        previousInvocation = null!;
        previousMemberAccess = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax currentAccess) return false;
        if (currentAccess.Expression is not InvocationExpressionSyntax previousCall) return false;
        if (previousCall.Expression is not MemberAccessExpressionSyntax previousAccess) return false;

        currentMemberAccess = currentAccess;
        previousInvocation = previousCall;
        previousMemberAccess = previousAccess;
        return true;
    }

    private static bool IsInsideOuterMaterialization(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Parent is MemberAccessExpressionSyntax parentMemberAccess &&
            parentMemberAccess.Parent is InvocationExpressionSyntax parentInvocation)
        {
            var parentSymbol = semanticModel.GetSymbolInfo(parentInvocation, cancellationToken).Symbol as IMethodSymbol;
            return parentSymbol != null && IsMaterializingMethod(parentSymbol.Name);
        }

        if (invocation.Parent is ArgumentSyntax argument &&
            argument.Parent?.Parent is ObjectCreationExpressionSyntax objectCreation)
        {
            var constructor = semanticModel.GetSymbolInfo(objectCreation, cancellationToken).Symbol as IMethodSymbol;
            return constructor != null && IsMaterializingConstructor(constructor.ContainingType.Name);
        }

        return false;
    }

    private static bool IsMaterializingMethod(string methodName)
    {
        return methodName == "AsEnumerable" ||
               methodName == "ToList" ||
               methodName == "ToArray" ||
               methodName == "ToDictionary" ||
               methodName == "ToHashSet" ||
               methodName == "ToLookup" ||
               methodName.StartsWith("ToImmutable");
    }

    private static bool IsMaterializingConstructor(string typeName)
    {
        return typeName is
            "List" or
            "HashSet" or
            "Dictionary" or
            "SortedDictionary" or
            "SortedList" or
            "LinkedList" or
            "Queue" or
            "Stack";
    }
}
