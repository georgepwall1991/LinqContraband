using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionAnalyzer
{
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
