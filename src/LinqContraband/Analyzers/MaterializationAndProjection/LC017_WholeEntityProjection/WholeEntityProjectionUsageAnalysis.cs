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
        var usageCandidates = new List<IOperation>();

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
            }

            if (descendant is IReturnOperation or IInvocationOperation or IAnonymousFunctionOperation or IPropertyReferenceOperation)
                usageCandidates.Add(descendant);
        }

        foreach (var descendant in usageCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (descendant)
            {
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
}
