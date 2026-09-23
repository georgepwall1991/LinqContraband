using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC026_MissingCancellationToken;

public sealed partial class MissingCancellationTokenAnalyzer
{
    internal static string? FindCancellationTokenInScope(SemanticModel semanticModel, int position)
    {
        ISymbol? fallback = null;
        ISymbol? shortName = null;
        var inStaticContext = IsInStaticContext(semanticModel.GetEnclosingSymbol(position));

        foreach (var symbol in semanticModel.LookupSymbols(position))
        {
            // A local declared after the call is in scope but cannot be used there yet (CS0841).
            if (symbol is ILocalSymbol && IsDeclaredAfter(symbol, position))
                continue;

            // An instance field or property is not reachable from a static method (CS0120).
            if (inStaticContext && symbol is IFieldSymbol or IPropertySymbol && !symbol.IsStatic)
                continue;

            // A CancellationToken stored in a field or surfaced through a readable property (e.g. an
            // injected token or IHostApplicationLifetime.ApplicationStopping) is just as passable as a
            // local/parameter one; the fixer references it by bare name, which binds to this.<member>.
            if (symbol is not ILocalSymbol and not IParameterSymbol and not IFieldSymbol and not IPropertySymbol)
                continue;

            if (symbol is IPropertySymbol { GetMethod: null })
                continue;

            var type = symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

            if (type == null || !IsCancellationTokenType(type))
                continue;

            if (symbol.Name == "cancellationToken")
                return symbol.Name;

            if (symbol.Name == "ct" && shortName == null)
                shortName = symbol;

            fallback ??= symbol;
        }

        return shortName?.Name ?? fallback?.Name;
    }

    private static bool IsDeclaredAfter(ISymbol symbol, int position)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.Span.Start < position)
                return false;
        }

        return symbol.DeclaringSyntaxReferences.Length > 0;
    }

    private static bool IsInStaticContext(ISymbol? enclosing)
    {
        // Lambdas and local functions inherit the static-ness of the member they are declared in.
        for (var current = enclosing; current != null; current = current.ContainingSymbol)
        {
            if (current is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
                continue;

            return current.IsStatic;
        }

        return false;
    }

    private static bool HasUsableCancellationTokenInScope(SemanticModel? semanticModel, int position)
    {
        if (semanticModel == null)
            return false;

        return FindCancellationTokenInScope(semanticModel, position) != null;
    }
}
