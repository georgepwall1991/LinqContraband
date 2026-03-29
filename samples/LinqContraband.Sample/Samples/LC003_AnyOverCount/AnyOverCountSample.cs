using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC003_AnyOverCount;

/// <summary>
///     Demonstrates the "Prefer Any() over Count() > 0" violation (LC003).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Using <c>Count() > 0</c> to check if any elements exist in a query.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> <c>Count()</c> forces the database to iterate through
///         <em>all</em> matching rows to calculate the total. If you have 1 million rows,
///         it counts all of them just to return "true".
///     </para>
///     <para>
///         <strong>The Fix:</strong> Use <c>Any()</c>. This generates an SQL <c>EXISTS</c> (or <c>LIMIT 1</c>) query,
///         which stops scanning as soon as the first matching row is found.
///     </para>
/// </remarks>
public class AnyOverCountSample
{
    /// <summary>
    ///     Runs the sample demonstrating the inefficient check.
    /// </summary>
    /// <param name="users">The source queryable of users.</param>
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC003...");

        // VIOLATION: This executes SELECT COUNT(*) FROM Users.
        // On large tables, this is significantly slower than checking existence.
        if (users.Count() > 0) Console.WriteLine("Users exist");
    }
}
