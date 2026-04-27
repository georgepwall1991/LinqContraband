using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionAnalyzer
{
    private static VariableUsageAnalysis AnalyzeVariableUsage(
        IInvocationOperation invocation,
        ILocalSymbol variable,
        ITypeSymbol entityType,
        CancellationToken cancellationToken)
    {
        var result = new VariableUsageAnalysis();
        var root = FindMethodBody(invocation);
        if (root == null)
        {
            result.HasEscapingUsage = true;
            return result;
        }

        var foreachLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var manualIterationLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var descendant in root.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (descendant)
            {
                case IForEachLoopOperation forEach when
                    forEach.Collection.UnwrapConversions() is ILocalReferenceOperation collectionRef &&
                    SymbolEqualityComparer.Default.Equals(collectionRef.Local, variable):
                    foreach (var local in forEach.Locals)
                        foreachLocals.Add(local);
                    break;

                case IVariableDeclaratorOperation declarator when
                    declarator.Initializer != null &&
                    IsIndexedAccessOf(declarator.Initializer.Value, variable):
                    manualIterationLocals.Add(declarator.Symbol);
                    break;

                case ISimpleAssignmentOperation assignment when
                    assignment.Target is ILocalReferenceOperation targetLocal &&
                    IsIndexedAccessOf(assignment.Value, variable):
                    manualIterationLocals.Add(targetLocal.Local);
                    break;
                case IReturnOperation returnOperation when
                    returnOperation.ReturnedValue != null &&
                    IsDirectVariableEscape(returnOperation.ReturnedValue, variable, foreachLocals, manualIterationLocals):
                    result.HasEscapingUsage = true;
                    break;

                case IInvocationOperation call when call != invocation &&
                    call.Arguments.Any(arg => IsDirectVariableEscape(arg.Value, variable, foreachLocals, manualIterationLocals)):
                    result.HasEscapingUsage = true;
                    break;

                case IAnonymousFunctionOperation lambda when
                    LambdaDirectlyReferences(lambda, variable, foreachLocals, manualIterationLocals, cancellationToken):
                    result.HasEscapingUsage = true;
                    break;

                case IPropertyReferenceOperation propertyReference when
                    IsPropertyOfType(propertyReference.Property, entityType) &&
                    IsTrackedEntityReference(propertyReference.Instance, variable, foreachLocals, manualIterationLocals):
                    result.AccessedProperties.Add(propertyReference.Property.Name);
                    break;
            }

            if (result.HasEscapingUsage) return result;
        }

        CollectSyntaxBasedPropertyAccesses(
            invocation,
            variable,
            entityType,
            foreachLocals,
            manualIterationLocals,
            result.AccessedProperties,
            cancellationToken);
        return result;
    }

    private static bool IsTrackedEntityReference(
        IOperation? operation,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        if (operation == null) return false;

        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is ILocalReferenceOperation localReference)
        {
            return SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
                   foreachLocals.Contains(localReference.Local) ||
                   manualIterationLocals.Contains(localReference.Local);
        }

        return false;
    }

    private static bool IsDirectVariableEscape(
        IOperation operation,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is not ILocalReferenceOperation localReference) return false;

        return SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
               foreachLocals.Contains(localReference.Local) ||
               manualIterationLocals.Contains(localReference.Local);
    }

    private static bool LambdaDirectlyReferences(
        IAnonymousFunctionOperation lambda,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals,
        CancellationToken cancellationToken)
    {
        foreach (var descendant in lambda.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (descendant is not ILocalReferenceOperation localReference) continue;

            if (SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
                foreachLocals.Contains(localReference.Local) ||
                manualIterationLocals.Contains(localReference.Local))
            {
                return true;
            }
        }

        return false;
    }
}
