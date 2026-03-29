using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC006_CartesianExplosion;

/// <summary>
///     Demonstrates the "Cartesian Explosion" violation (LC006).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Using multiple <c>Include()</c> calls on different collection navigations
///         in a single query without splitting it.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> Relational databases join tables to produce the result.
///         If you fetch Users + 10 Orders + 10 Roles, the database produces 100 rows (10 * 10) for <em>each</em> user
///         to represent all combinations. This "Cartesian Product" explodes the amount of data transferred over the
///         network,
///         causing massive memory spikes and slow performance.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Use <c>.AsSplitQuery()</c>. This instructs EF Core to issue separate SQL queries
///         (one for Users, one for Orders, one for Roles) and stitch them together in memory, avoiding the explosion.
///     </para>
/// </remarks>
public class CartesianExplosionSample
{
    /// <summary>
    ///     Runs the sample demonstrating the Cartesian explosion risk.
    /// </summary>
    /// <param name="users">The source queryable of users.</param>
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC006...");

        // VIOLATION: Fetching multiple collection navigations (Orders, Roles) in a single query.
        // This creates a Cartesian product (Users * Orders * Roles).
        var cartesianResult = users.OrderBy(u => u.Id).Take(10).Include(u => u.Orders).Include(u => u.Roles).ToList();
    }
}
