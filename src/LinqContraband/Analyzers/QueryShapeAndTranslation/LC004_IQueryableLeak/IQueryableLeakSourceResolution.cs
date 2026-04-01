using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

internal sealed partial class IQueryableLeakCompilationState
{
    private bool TryResolveParameterSource(
        IOperation? operation,
        int position,
        IOperation executableRoot,
        HashSet<ISymbol> visitedLocals,
        out IParameterSymbol parameter)
    {
        parameter = null!;
        if (operation == null)
            return false;

        var current = operation.UnwrapConversions();

        if (current is IParameterReferenceOperation parameterReference)
        {
            parameter = parameterReference.Parameter;
            return true;
        }

        if (current is ILocalReferenceOperation localReference)
        {
            if (!visitedLocals.Add(localReference.Local))
                return false;

            if (!TryResolveSingleAssignedValue(executableRoot, localReference.Local, position, out var assignedValue))
                return false;

            return TryResolveParameterSource(assignedValue, position, executableRoot, visitedLocals, out parameter);
        }

        if (current is IInvocationOperation invocation &&
            IsSequencePreservingInvocation(invocation))
        {
            return TryResolveParameterSource(
                invocation.GetInvocationReceiver(),
                position,
                executableRoot,
                visitedLocals,
                out parameter);
        }

        return false;
    }

    private bool TryResolveSingleAssignedValue(
        IOperation executableRoot,
        ILocalSymbol local,
        int position,
        out IOperation value)
    {
        value = null!;
        IOperation? latestValue = null;
        var latestPosition = -1;
        var assignmentCount = 0;

        foreach (var operation in EnumerateOperations(executableRoot))
        {
            if (operation.Syntax.SpanStart >= position)
                continue;

            switch (operation)
            {
                case IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                         declarator.Initializer != null &&
                         declarator.Syntax.SpanStart > latestPosition:
                    latestValue = declarator.Initializer.Value;
                    latestPosition = declarator.Syntax.SpanStart;
                    assignmentCount++;
                    break;

                case ISimpleAssignmentOperation assignment
                    when assignment.Target is ILocalReferenceOperation targetLocal &&
                         SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                         assignment.Syntax.SpanStart > latestPosition:
                    latestValue = assignment.Value;
                    latestPosition = assignment.Syntax.SpanStart;
                    assignmentCount++;
                    break;
            }
        }

        if (latestValue == null || assignmentCount != 1)
            return false;

        value = latestValue.UnwrapConversions();
        return true;
    }

    private IEnumerable<InvocationInput> EnumerateInvocationInputs(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        var originalTargetMethod = GetOriginalTargetMethod(targetMethod);

        if (targetMethod.ReducedFrom != null && invocation.Instance != null && originalTargetMethod.Parameters.Length > 0)
        {
            yield return new InvocationInput(invocation.Instance, originalTargetMethod.Parameters[0]);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter == null)
                continue;

            var parameter = argument.Parameter;
            if (targetMethod.ReducedFrom != null)
            {
                var originalOrdinal = parameter.Ordinal + 1;
                if (originalOrdinal >= originalTargetMethod.Parameters.Length)
                    continue;

                parameter = originalTargetMethod.Parameters[originalOrdinal];
            }

            yield return new InvocationInput(argument.Value, parameter);
        }
    }

    private bool TryGetQuerySourceType(IOperation operation, out ITypeSymbol sourceType)
    {
        sourceType = null!;
        var current = operation.UnwrapConversions();
        if (current.Type == null || !IsIQueryableType(current.Type))
            return false;

        sourceType = current.Type;
        return true;
    }

    private bool CanOfferToListFix(ITypeSymbol sourceType)
    {
        return TryGetConstructedInterface(sourceType, _queryableGenericType, out _);
    }

    private bool IsSequencePreservingInvocation(IInvocationOperation invocation)
    {
        var targetMethod = GetOriginalTargetMethod(invocation.TargetMethod);
        if (!IsEnumerableMethod(targetMethod) && !IsQueryableMethod(targetMethod))
            return false;

        if (!IsIEnumerableLike(targetMethod.ReturnType) && !IsIQueryableType(targetMethod.ReturnType))
            return false;

        return invocation.GetInvocationReceiver() != null;
    }
}
