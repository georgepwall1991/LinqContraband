using System.Collections.Generic;

namespace LinqContraband.Constants;

/// <summary>
/// Provides mappings between synchronous and asynchronous EF Core/LINQ method names.
/// Used by LC008 (SyncBlocker) analyzer and fixer to identify sync methods and their async alternatives.
/// </summary>
public static class SyncAsyncMappings
{
    /// <summary>
    /// Maps synchronous EF Core and LINQ method names to their async counterparts.
    /// </summary>
    /// <remarks>
    /// <para>Includes:</para>
    /// <list type="bullet">
    ///   <item><description>LINQ Queryable extensions (ToList, First, Single, etc.)</description></item>
    ///   <item><description>DbContext methods (SaveChanges)</description></item>
    ///   <item><description>DbSet methods (Find)</description></item>
    /// </list>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> SyncToAsyncMap = new Dictionary<string, string>
    {
        // Queryable extensions
        { "ToList", "ToListAsync" },
        { "ToArray", "ToArrayAsync" },
        { "ToDictionary", "ToDictionaryAsync" },
        { "ToHashSet", "ToHashSetAsync" },
        { "First", "FirstAsync" },
        { "FirstOrDefault", "FirstOrDefaultAsync" },
        { "Single", "SingleAsync" },
        { "SingleOrDefault", "SingleOrDefaultAsync" },
        { "Last", "LastAsync" },
        { "LastOrDefault", "LastOrDefaultAsync" },
        { "Count", "CountAsync" },
        { "LongCount", "LongCountAsync" },
        { "Any", "AnyAsync" },
        { "All", "AllAsync" },
        { "Min", "MinAsync" },
        { "Max", "MaxAsync" },
        { "Sum", "SumAsync" },
        { "Average", "AverageAsync" },

        // DbContext / DbSet methods
        { "SaveChanges", "SaveChangesAsync" },
        { "Find", "FindAsync" },
        { "ExecuteUpdate", "ExecuteUpdateAsync" },
        { "ExecuteDelete", "ExecuteDeleteAsync" }
    };
}
