using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesAnalyzer
{
    private static bool IsScalarLikeType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (type is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
        {
            type = namedType.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_Char or
            SpecialType.System_Decimal or
            SpecialType.System_Double or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_SByte or
            SpecialType.System_Single or
            SpecialType.System_String or
            SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64)
        {
            return true;
        }

        var displayName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return displayName is
            "global::System.DateOnly" or
            "global::System.DateTime" or
            "global::System.DateTimeOffset" or
            "global::System.Guid" or
            "global::System.TimeOnly" or
            "global::System.TimeSpan";
    }
}
