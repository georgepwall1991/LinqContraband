using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

public sealed partial class AsNoTrackingWithUpdateAnalyzer
{
    private static bool TryParseEntryStateWrite(
        ISimpleAssignmentOperation assignment,
        out ILocalSymbol entityLocal,
        out IOperation entityOperation,
        out string stateName)
    {
        entityLocal = null!;
        entityOperation = null!;
        stateName = string.Empty;

        if (assignment.Target is not IPropertyReferenceOperation targetProperty ||
            targetProperty.Property.Name != "State" ||
            targetProperty.Property.ContainingType?.Name != "EntityEntry")
        {
            return false;
        }

        if (targetProperty.Instance?.UnwrapConversions() is not IInvocationOperation entryInvocation ||
            entryInvocation.TargetMethod.Name != "Entry" ||
            !entryInvocation.TargetMethod.ContainingType.IsDbContext() ||
            entryInvocation.Arguments.Length == 0)
        {
            return false;
        }

        entityOperation = entryInvocation.Arguments[0].Value.UnwrapConversions();
        if (entityOperation is not ILocalReferenceOperation localReference)
            return false;

        if (!TryGetEntityStateName(assignment.Value, out stateName))
            return false;

        entityLocal = localReference.Local;
        return true;
    }

    private static bool TryGetEntityStateName(IOperation value, out string stateName)
    {
        stateName = string.Empty;
        if (value.UnwrapConversions() is not IFieldReferenceOperation fieldReference)
            return false;

        var containingType = fieldReference.Field.ContainingType;
        if (containingType?.Name != "EntityState" ||
            containingType.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
        {
            return false;
        }

        if (fieldReference.Field.Name is not ("Modified" or "Deleted"))
            return false;

        stateName = fieldReference.Field.Name;
        return true;
    }
}
