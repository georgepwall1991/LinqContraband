using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC012_OptimizeRemoveRange;

/// <summary>
///     Demonstrates the "Optimize Bulk Delete" suggestion (LC012).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Opportunity:</strong> Using <c>RemoveRange()</c> to delete a collection of entities fetched from
///         the database.
///     </para>
///     <para>
///         <strong>Why it's suboptimal:</strong> <c>RemoveRange</c> (when used with a query) typically requires fetching
///         the entities into memory first, tracking them, and then issuing individual DELETE statements (or batched ones).
///         This is slow for large datasets.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Use <c>ExecuteDelete()</c> (EF Core 7+). This executes a direct SQL <c>DELETE</c>
///         command
///         on the database without loading any data into memory.
///     </para>
///     <para>
///         <strong>Warning:</strong> <c>ExecuteDelete()</c> bypasses Change Tracking, so local events and client-side
///         logic won't run.
///     </para>
/// </remarks>
public class OptimizeRemoveRangeSample
{
    /// <summary>
    ///     Runs the sample demonstrating the inefficient delete pattern.
    /// </summary>
    public static void Run()
    {
        Console.WriteLine("Testing LC012...");
        using var db = new AppDbContext();

        var usersToDelete = db.Users.Where(u => u.Id < Guid.Empty);

        // VIOLATION: Loading entities into memory to delete them.
        // This fetches all matching rows, tracks them, and marks them as Deleted.
        db.Users.RemoveRange(usersToDelete);

        // Alternative inefficient approach on the context directly.
        db.RemoveRange(usersToDelete);
    }
}
