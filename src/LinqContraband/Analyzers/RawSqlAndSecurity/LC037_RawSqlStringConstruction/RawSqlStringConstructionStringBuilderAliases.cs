using System.Linq;
using System.Text;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool IsInvocationOnStringBuilderLocal(
        IInvocationOperation invocation,
        ILocalReferenceOperation builderReference,
        IOperation executableRoot,
        int depth,
        bool allowMayAlias = true)
    {
        var receiver = GetInvocationReceiverForAliasResolution(invocation);

        while (receiver is IInvocationOperation receiverInvocation &&
               (IsStringBuilderAppend(receiverInvocation.TargetMethod) || IsStringBuilderClear(receiverInvocation.TargetMethod)))
        {
            receiver = GetInvocationReceiverForAliasResolution(receiverInvocation);
        }

        return IsSameLocalOrAlias(receiver, builderReference, executableRoot, depth, allowMayAlias);
    }

    private static IOperation? GetInvocationReceiverForAliasResolution(IInvocationOperation invocation)
    {
        var receiver = invocation.GetInvocationReceiver()?.UnwrapConversions();
        if (receiver is not IConditionalAccessInstanceOperation)
            return receiver;

        var parent = invocation.Parent;
        while (parent != null)
        {
            if (parent is IConditionalAccessOperation conditionalAccess &&
                OperationContains(conditionalAccess.WhenNotNull, invocation))
            {
                return conditionalAccess.Operation.UnwrapConversions();
            }

            parent = parent.Parent;
        }

        return receiver;
    }

    private static bool OperationContains(IOperation container, IOperation operation)
    {
        return ReferenceEquals(container, operation) ||
               container.Descendants().Any(descendant => ReferenceEquals(descendant, operation));
    }

    private static bool IsSameLocalOrAlias(
        IOperation? operation,
        ILocalReferenceOperation builderReference,
        IOperation executableRoot,
        int depth,
        bool allowMayAlias)
    {
        if (operation?.UnwrapConversions() is not ILocalReferenceOperation localReference)
            return false;

        var receiverIdentity = ResolveLocalIdentity(localReference, executableRoot, depth);
        var builderIdentity = ResolveLocalIdentity(builderReference, executableRoot, depth);
        if (receiverIdentity.Equals(builderIdentity))
        {
            if (!allowMayAlias &&
                !SymbolEqualityComparer.Default.Equals(localReference.Local, builderReference.Local) &&
                (HasNonGuaranteedWriteAfterLatestGuaranteed(localReference, executableRoot) ||
                 HasNonGuaranteedWriteAfterLatestGuaranteed(builderReference, executableRoot)))
            {
                return false;
            }

            return true;
        }

        return allowMayAlias &&
               (MayResolveToIdentity(localReference, builderIdentity, executableRoot, depth) ||
                MayResolveToIdentity(builderReference, receiverIdentity, executableRoot, depth));
    }

    private static bool IsStringBuilderClear(IMethodSymbol method)
    {
        return method.ContainingType.Name == nameof(StringBuilder) &&
               method.ContainingNamespace?.ToString() == "System.Text" &&
               method.Name == "Clear";
    }

    private static bool IsStringBuilderAppend(IMethodSymbol method)
    {
        return method.ContainingType.Name == nameof(StringBuilder) &&
               method.ContainingNamespace?.ToString() == "System.Text" &&
               method.Name.StartsWith("Append", System.StringComparison.Ordinal);
    }
}
