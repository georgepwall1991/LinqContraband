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

            if (descendant is IReturnOperation or IInvocationOperation or IAnonymousFunctionOperation or IPropertyReferenceOperation
                or IAssignmentOperation or IIncrementOrDecrementOperation or IArrayInitializerOperation or ITupleOperation
                or IVariableDeclaratorOperation)
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

                // Loading tracked entities to change them is an update, not a read a projection could serve.
                // That includes writes through a navigation (`user.Preferences.Theme = ...`).
                case IAssignmentOperation { Target: IPropertyReferenceOperation or IFieldReferenceOperation } write when
                    IsRootedInTrackedEntity(((IMemberReferenceOperation)write.Target).Instance, variable, foreachLocals, manualIterationLocals):
                    result.HasEscapingUsage = true;
                    break;

                case IIncrementOrDecrementOperation { Target: IPropertyReferenceOperation or IFieldReferenceOperation } increment when
                    IsRootedInTrackedEntity(((IMemberReferenceOperation)increment.Target).Instance, variable, foreachLocals, manualIterationLocals):
                    result.HasEscapingUsage = true;
                    break;

                // `library.FileTypes.Add(...)` or `order.MarkPaid()` changes the tracked graph, and
                // SaveChanges would persist nothing once the query returns a projection instead.
                case IInvocationOperation call when call != invocation &&
                    IsEntityMutatingCall(call, variable, foreachLocals, manualIterationLocals):
                    result.HasEscapingUsage = true;
                    break;

                // Stored in a field, property, another local or an initializer, the entities are used where this
                // method cannot see.
                case IAssignmentOperation assignment when
                    IsDirectVariableEscape(assignment.Value, variable, foreachLocals, manualIterationLocals) &&
                    !IsIterationLocalAssignment(assignment, manualIterationLocals):
                    result.HasEscapingUsage = true;
                    break;

                case IVariableDeclaratorOperation { Initializer: { } initializer } when
                    IsDirectVariableEscape(initializer.Value, variable, foreachLocals, manualIterationLocals):
                    result.HasEscapingUsage = true;
                    break;

                case IArrayInitializerOperation arrayInitializer when
                    arrayInitializer.ElementValues.Any(element => IsDirectVariableEscape(element, variable, foreachLocals, manualIterationLocals)):
                    result.HasEscapingUsage = true;
                    break;

                case ITupleOperation tuple when
                    tuple.Elements.Any(element => IsDirectVariableEscape(element, variable, foreachLocals, manualIterationLocals)):
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

    private static bool IsIterationLocalAssignment(IAssignmentOperation assignment, HashSet<ILocalSymbol> manualIterationLocals)
    {
        // `current = entities[i];` is how a manual loop reads an item, not an escape.
        return assignment.Target is ILocalReferenceOperation target && manualIterationLocals.Contains(target.Local);
    }
}
