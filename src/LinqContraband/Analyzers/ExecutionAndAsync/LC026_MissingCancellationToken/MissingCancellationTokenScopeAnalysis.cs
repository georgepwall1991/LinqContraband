using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC026_MissingCancellationToken;

public sealed partial class MissingCancellationTokenAnalyzer
{
    internal static string? FindCancellationTokenInScope(SemanticModel semanticModel, int position)
    {
        ISymbol? fallback = null;
        ISymbol? shortName = null;

        foreach (var symbol in semanticModel.LookupSymbols(position))
        {
            if (symbol is not ILocalSymbol and not IParameterSymbol)
                continue;

            var type = symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
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

    private static bool HasUsableCancellationTokenInScope(SemanticModel? semanticModel, int position)
    {
        if (semanticModel == null)
            return false;

        return FindCancellationTokenInScope(semanticModel, position) != null;
    }
}
