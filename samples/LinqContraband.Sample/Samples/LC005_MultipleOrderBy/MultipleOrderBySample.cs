using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC005_MultipleOrderBy;

/// <summary>
///     Demonstrates the "Multiple OrderBy Calls" violation (LC005).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling <c>OrderBy</c> (or <c>OrderByDescending</c>) multiple times in a row.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> This is a logic bug. The second <c>OrderBy</c> call completely
///         <em>replaces</em> the first sort order, rather than refining it. The database will only sort by the last field
///         specified.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Use <c>ThenBy</c> (or <c>ThenByDescending</c>) for subsequent sort criteria.
///     </para>
/// </remarks>
public class MultipleOrderBySample
{
    /// <summary>
    ///     Runs the sample demonstrating the sorting bug.
    /// </summary>
    /// <param name="users">The source queryable of users.</param>
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC005...");

        // VIOLATION: This first sorts by Age, but then immediately discards that work
        // to sort by Name. The resulting list is NOT sorted by Age.
        var orderResult = users.AsNoTracking().OrderBy(u => u.Age).OrderBy(u => u.Name).ToList();
    }
}
