using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
    private static bool IsAllowedProviderSafeStringMethod(IMethodSymbol method)
    {
        if (method.ContainingType.SpecialType != SpecialType.System_String)
            return false;

        if (HasStringComparisonParameter(method))
            return false;

        return method.Name is
            "Contains" or
            "StartsWith" or
            "EndsWith" or
            "IsNullOrEmpty" or
            "IsNullOrWhiteSpace";
    }

    private static bool HasStringComparisonParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.Name == "StringComparison" &&
                parameter.Type.ContainingNamespace?.ToString() == "System")
            {
                return true;
            }
        }

        return false;
    }
}
