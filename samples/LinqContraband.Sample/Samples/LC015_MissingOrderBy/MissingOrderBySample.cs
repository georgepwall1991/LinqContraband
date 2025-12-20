using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC015_MissingOrderBy;

/// <summary>
///     Demonstrates the "Missing OrderBy Before Skip/Last" violation (LC015).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling <c>Skip</c>, <c>Last</c>, or <c>Chunk</c> on an <c>IQueryable</c> without
///         explicitly sorting it first.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> Without an explicit <c>OrderBy</c>, the database does not guarantee the order of
///         results.
///         This makes pagination non-deterministic (items might appear on multiple pages or be skipped entirely) and
///         <c>Last()</c> calls unpredictable.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Call <c>OrderBy</c> or <c>OrderByDescending</c> before applying pagination or
///         retrieving the last element.
///     </para>
/// </remarks>
public class MissingOrderBySample
{
    /// <summary>
    ///     Runs the sample demonstrating the pagination bug.
    /// </summary>
    /// <param name="users">The source queryable of users.</param>
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC015...");

        // VIOLATION: Skipping 10 users without sorting. Which 10 are skipped? It's undefined.
        var page2 = users.Skip(10).Take(10).ToList();

        // VIOLATION: Getting the last user without sorting. Which user is "Last"? Undefined.
        // Note: Last() isn't supported directly in some EF Core providers without ordering, but LINQ allows compilation.
        try
        {
            var last = users.Last();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Expected error or undefined behavior: {ex.Message}");
        }

        // VIOLATION: Chunking data without order. The chunks might contain random items across executions.
        var chunks = users.Chunk(5).ToList();

        // VIOLATION: Misplaced sorting (Merged from LC027).
        // Sorting happens AFTER Skip/Take, so it only sorts the page, not the source.
        var misplacedSort = users.Skip(10).Take(10).OrderBy(u => u.Name).ToList();
    }
}
