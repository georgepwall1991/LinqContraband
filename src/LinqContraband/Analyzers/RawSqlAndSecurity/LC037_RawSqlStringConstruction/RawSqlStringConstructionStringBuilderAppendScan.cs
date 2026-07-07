using System.Linq;
using System.Text;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool ContainsSuspiciousStringBuilderAppend(IOperation? receiver, IOperation? executableRoot, int depth)
    {
        if (receiver == null)
            return false;

        var current = receiver.UnwrapConversions();

        if (current is IInvocationOperation invocation)
        {
            if (IsStringBuilderAppend(invocation.TargetMethod))
            {
                if (invocation.Arguments.Any(arg => IsStringBuilderAppendArgumentNonConstant(arg.Value, executableRoot, depth + 1)))
                    return true;

                return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot, depth + 1);
            }

            if (IsStringBuilderClear(invocation.TargetMethod))
                return false;

            return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot, depth + 1);
        }

        if (current is ILocalReferenceOperation localReference)
        {
            return ContainsSuspiciousStringBuilderStatementAppend(localReference, executableRoot, depth + 1, localReference.Syntax.SpanStart);
        }

        if (current is IConditionalOperation conditional)
        {
            return (conditional.WhenTrue != null &&
                    ContainsSuspiciousStringBuilderAppend(conditional.WhenTrue, executableRoot, depth + 1)) ||
                   (conditional.WhenFalse != null &&
                    ContainsSuspiciousStringBuilderAppend(conditional.WhenFalse, executableRoot, depth + 1));
        }

        if (current is IObjectCreationOperation objectCreation &&
            objectCreation.Type is INamedTypeSymbol objectType &&
            objectType.Name == nameof(StringBuilder) &&
            objectType.ContainingNamespace?.ToString() == "System.Text")
        {
            return objectCreation.Arguments.Any(arg =>
                arg.Parameter?.Type.SpecialType == SpecialType.System_String &&
                IsStringBuilderAppendArgumentNonConstant(arg.Value, executableRoot, depth + 1));
        }

        return false;
    }

    private static bool ContainsSuspiciousStringBuilderStatementAppend(
        ILocalReferenceOperation builderReference,
        IOperation? executableRoot,
        int depth,
        int referenceStart)
    {
        if (executableRoot == null || depth >= MaxLocalResolutionDepth)
            return false;

        var latestGuaranteedResetEnd = GetLatestGuaranteedStringBuilderReset(
            builderReference,
            executableRoot,
            referenceStart,
            out var resetIsValueWrite);

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not IInvocationOperation invocation)
                continue;

            if (!IsStringBuilderAppend(invocation.TargetMethod))
                continue;

            if (invocation.Syntax.Span.End > referenceStart ||
                invocation.Syntax.Span.End <= latestGuaranteedResetEnd ||
                !ReferenceEquals(invocation.FindOwningExecutableRoot(), executableRoot) ||
                !CanOperationReachReference(invocation, referenceStart))
                continue;

            if (!IsInvocationOnStringBuilderLocal(invocation, builderReference, executableRoot, depth + 1))
                continue;

            if (invocation.Arguments.Any(arg => IsStringBuilderAppendArgumentNonConstant(arg.Value, executableRoot, depth + 1)))
                return true;
        }

        if (!resetIsValueWrite ||
            !TryResolveLocalValue(builderReference.Local, builderReference, executableRoot, out var resolvedValue))
            return false;

        if (resolvedValue.UnwrapConversions() is ILocalReferenceOperation resolvedLocalReference)
            return ContainsSuspiciousStringBuilderStatementAppend(resolvedLocalReference, executableRoot, depth + 1, referenceStart);

        return ContainsSuspiciousStringBuilderAppend(resolvedValue, executableRoot, depth + 1);
    }
}
