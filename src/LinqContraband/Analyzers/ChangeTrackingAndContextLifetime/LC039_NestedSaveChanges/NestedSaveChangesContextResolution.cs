using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC039_NestedSaveChanges;

public sealed partial class NestedSaveChangesAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool TryGetContextSymbol(IOperation? receiver, out ISymbol? symbol)
        {
            receiver = receiver?.UnwrapConversions();

            switch (receiver)
            {
                case ILocalReferenceOperation localReference:
                    symbol = localReference.Local;
                    return true;
                case IParameterReferenceOperation parameterReference:
                    symbol = parameterReference.Parameter;
                    return true;
                case IFieldReferenceOperation fieldReference:
                    symbol = fieldReference.Field;
                    return true;
                case IPropertyReferenceOperation propertyReference:
                    symbol = propertyReference.Property;
                    return true;
                default:
                    symbol = null;
                    return false;
            }
        }
    }
}
