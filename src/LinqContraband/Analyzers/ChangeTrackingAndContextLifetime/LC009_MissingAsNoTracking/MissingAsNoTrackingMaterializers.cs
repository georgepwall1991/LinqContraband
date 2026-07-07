using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
    private static bool IsEntityMaterializer(IMethodSymbol method)
    {
        return method.Name is
            "ToList" or "ToListAsync" or
            "ToArray" or "ToArrayAsync" or
            "ToDictionary" or "ToDictionaryAsync" or
            "ToHashSet" or "ToHashSetAsync" or
            "AsEnumerable" or
            "First" or "FirstOrDefault" or
            "FirstAsync" or "FirstOrDefaultAsync" or
            "Single" or "SingleOrDefault" or
            "SingleAsync" or "SingleOrDefaultAsync" or
            "Last" or "LastOrDefault" or
            "LastAsync" or "LastOrDefaultAsync";
    }
}
