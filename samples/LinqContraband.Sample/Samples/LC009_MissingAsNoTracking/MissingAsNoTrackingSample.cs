using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC009_MissingAsNoTracking;

/// <summary>
///     Demonstrates the "The Tracking Tax" violation (LC009).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Fetching entities for read-only purposes without using <c>AsNoTracking()</c>.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> By default, EF Core creates a "snapshot" of every entity it retrieves
///         to detect changes later. This consumes significant CPU and memory. If you are only reading data
///         (e.g., for an API response or dashboard) and not modifying it, this overhead is wasted.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Add <c>.AsNoTracking()</c> to queries that are strictly for read-only operations.
///         If you need identity resolution (to avoid duplicate instances of the same entity in the graph) but not
///         tracking,
///         use <c>.AsNoTrackingWithIdentityResolution()</c>.
///     </para>
/// </remarks>
public class MissingAsNoTrackingSample
{
    /// <summary>
    ///     Runs the sample demonstrating missing tracking optimizations.
    /// </summary>
    public static void Run()
    {
        Console.WriteLine("Testing LC009...");

        GetUsersReadOnly();
        GetUsersWithIdentityResolution();
    }

    /// <summary>
    ///     Fetches users without disabling tracking, triggering the violation.
    /// </summary>
    private static List<User> GetUsersReadOnly()
    {
        using var db = new AppDbContext();

        // VIOLATION: Returning entities from read-only context without AsNoTracking.
        // EF Core tracks these entities, wasting resources.
        return db.Users.Where(u => u.Age > 18).Take(10).ToList();
    }

    /// <summary>
    ///     Correctly uses identity resolution without full tracking.
    /// </summary>
    private static List<User> GetUsersWithIdentityResolution()
    {
        using var db = new AppDbContext();

        // SAFE: Using AsNoTrackingWithIdentityResolution is explicitly allowed.
        return db.Users.AsNoTrackingWithIdentityResolution().Where(u => u.Age > 21).Take(10).ToList();
    }
}
