using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Sample.Samples.LC007_NPlusOneLooper;

/// <summary>
///     Demonstrates the "N+1 Looper" violation (LC007).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Executing a database query inside a loop.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> If you have 100 items in your list, you will execute 100 separate database
///         queries.
///         Database queries have high latency overhead (connection pooling, network roundtrip, parsing).
///         Doing this sequentially kills performance.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Fetch all necessary data in a single (or few) batched queries <em>outside</em> the
///         loop
///         (e.g., using <c>Where(x => ids.Contains(x.Id))</c>).
///     </para>
/// </remarks>
public class NPlusOneLooperSample
{
    /// <summary>
    ///     Runs the sample demonstrating the N+1 query pattern.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="users">The source queryable of users.</param>
    public static void Run(AppDbContext db, IQueryable<User> users)
    {
        Console.WriteLine("Testing LC007...");
        var targetIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // VIOLATION: Iterating over a list and querying the DB for each item.
        foreach (var id in targetIds)
        {
            // This executes a separate SQL query for every iteration.
            var user = db.Users.Find(id);
        }
    }
}
