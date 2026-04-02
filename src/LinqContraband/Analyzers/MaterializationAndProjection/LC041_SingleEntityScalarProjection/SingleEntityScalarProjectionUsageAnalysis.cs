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

        var current = invocation.Parent;
        while (current != null)
        {
            if (current is IVariableDeclaratorOperation declarator)
            {
                local = declarator.Symbol;
                return true;
            }

            if (current is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation localReference)
            {
                local = localReference.Local;
                return true;
            }

            if (current is IExpressionStatementOperation || current is IReturnOperation)
                return false;

            current = current.Parent;
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

            if (localReference.Parent is not IPropertyReferenceOperation propertyReference)
                return false;

            if (!ReferenceEquals(propertyReference.Instance?.UnwrapConversions(), localReference))
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
