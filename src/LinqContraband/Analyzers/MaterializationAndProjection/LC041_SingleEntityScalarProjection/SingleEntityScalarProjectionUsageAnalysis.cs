using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

public sealed partial class SingleEntityScalarProjectionAnalyzer
{
    internal static bool TryGetAssignedLocal(IInvocationOperation invocation, out ILocalSymbol local)
    {
        local = null!;

        // The materializer has to be the whole value the local receives. In
        // `var c = q.FirstOrDefault() ?? q.First();` the local holds whichever side ran, so
        // projecting one side to a scalar would leave `Entity ?? int`, which does not compile.
        IOperation current = invocation;
        while (current.Parent != null)
        {
            var parent = current.Parent;
            switch (parent)
            {
                case IAwaitOperation:
                case IConversionOperation:
                case IVariableInitializerOperation:
                    current = parent;
                    continue;

                case IInvocationOperation { TargetMethod.Name: "ConfigureAwait" } configureAwait
                    when configureAwait.Instance == current:
                    current = parent;
                    continue;

                case IVariableDeclaratorOperation declarator:
                    local = declarator.Symbol;
                    return true;

                case ISimpleAssignmentOperation assignment
                    when assignment.Value == current &&
                         assignment.Target is ILocalReferenceOperation localReference:
                    local = localReference.Local;
                    return true;

                default:
                    return false;
            }
        }

        return false;
    }

    internal static bool TryAnalyzeLocalUsage(IOperation executableRoot, ILocalSymbol local, out IPropertySymbol property)
    {
        property = null!;
        var properties = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            if (!ReferenceEquals(localReference.FindOwningExecutableRoot(), executableRoot))
                return false;

            if (!TryGetConsumedProperty(localReference, out var propertyReference))
                return false;

            if (!IsScalarLikeType(propertyReference.Property.Type))
                return false;

            properties.Add(propertyReference.Property);
        }

        if (properties.Count != 1)
            return false;

        property = properties.First();
        return true;
    }

    internal static bool HasNullConditionalPropertyUsage(
        IOperation executableRoot,
        ILocalSymbol local,
        IPropertySymbol property)
    {
        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            if (!TryGetConsumedProperty(localReference, out var propertyReference))
                continue;

            if (!SymbolEqualityComparer.Default.Equals(propertyReference.Property, property))
                continue;

            if (localReference.Parent is IConditionalAccessOperation)
                return true;
        }

        return false;
    }

    private static bool IsScalarLikeType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (type.SpecialType != SpecialType.None)
            return true;

        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type.TypeKind == TypeKind.Struct)
            return true;

        return type.Name == "String";
    }
}
