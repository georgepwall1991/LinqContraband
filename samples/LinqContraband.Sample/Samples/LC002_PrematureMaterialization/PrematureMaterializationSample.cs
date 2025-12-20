using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC002_PrematureMaterialization;

/// <summary>
///     Demonstrates the "Premature Materialization" violation (LC002).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling a materializing method (like <c>ToList()</c>, <c>AsEnumerable()</c>, or
///         <c>ToArray()</c>)
///         <em>before</em> applying filtering logic (like <c>Where()</c>).
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> This fetches the entire table (or a larger result set than needed)
///         from the database into application memory before filtering. This wastes network bandwidth,
///         memory, and CPU, effectively acting as a "SELECT *" on the table.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Always chain <c>Where()</c> clauses <em>before</em> calling <c>ToList()</c>.
///     </para>
/// </remarks>
public class PrematureMaterializationSample
{
    /// <summary>
    ///     Runs the sample demonstrating various forms of premature materialization.
    /// </summary>
    /// <param name="users">The source queryable of users.</param>
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC002...");

        // VIOLATION 1: ToList() executes the query (SELECT * FROM Users).
        // The Where() clause then runs in memory on the entire dataset.
        var prematureResult = users.ToList().Where(u => u.Age > 20).ToList();

        // VIOLATION 2: AsEnumerable() switches to LINQ-to-Objects context.
        // This forces client-side evaluation for all subsequent operators.
        var prematureAsEnumerable = users.AsEnumerable().Where(u => u.Age > 30).ToList();

        // VIOLATION 3: Materializing to a Dictionary first.
        // This fetches everything before filtering.
        var prematureDictionary = users.ToDictionary(u => u.Id).Where(kvp => kvp.Value.Age > 25).ToList();

        // VIOLATION 4: Redundant materialization (Merged from LC028).
        // Calling AsEnumerable() immediately followed by ToList() is redundant.
        var redundant = users.AsEnumerable().ToList();
    }
}
