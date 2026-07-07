using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC008_SyncBlocker;

public sealed partial class SyncBlockerAnalyzer
{
    /// <summary>
    /// Determines if the operation is within an async context.
    /// This includes being directly in an async method, or being inside a lambda/local function
    /// that is itself within an async method.
    /// </summary>
    private static bool IsInsideAsyncMethod(IOperation operation)
    {
        // Walk up the operation tree looking for async context.
        var parent = operation.Parent;
        while (parent != null)
        {
            if (parent is ILocalFunctionOperation localFunc)
            {
                if (localFunc.Symbol.IsAsync) return true;

                // A static local function cannot capture the enclosing async context - it
                // is a deliberate synchronous boundary, and a diagnostic inside it would be
                // unfixable (await is illegal there). Do not look further up.
                if (localFunc.Symbol.IsStatic) return false;
            }

            if (parent is IAnonymousFunctionOperation lambda)
            {
                if (lambda.Symbol.IsAsync) return true;
            }

            parent = parent.Parent;
        }

        // Fallback handles non-async lambdas inside async methods.
        if (operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart) is IMethodSymbol methodSymbol)
        {
            var currentMethod = methodSymbol;
            while (currentMethod != null)
            {
                if (currentMethod.IsAsync) return true;

                // Same static-local-function boundary as the operation-tree walk.
                if (currentMethod.MethodKind == MethodKind.LocalFunction && currentMethod.IsStatic)
                    return false;

                currentMethod = currentMethod.ContainingSymbol as IMethodSymbol;
            }
        }

        return false;
    }
}
