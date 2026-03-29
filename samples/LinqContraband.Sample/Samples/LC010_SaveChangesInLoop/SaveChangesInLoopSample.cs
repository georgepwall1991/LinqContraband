using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC010_SaveChangesInLoop;

/// <summary>
///     Demonstrates the "SaveChanges Loop Tax" violation (LC010).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling <c>SaveChanges()</c> (or <c>SaveChangesAsync()</c>) inside a loop.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> Each call to <c>SaveChanges</c> opens a database transaction, sends commands,
///         commits the transaction, and closes the connection. Doing this 100 times is orders of magnitude slower
///         than doing it once for 100 items. It creates massive transaction overhead.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Move <c>SaveChanges()</c> <em>after</em> the loop. EF Core will batch all the updates
///         into a single (or few) efficient database roundtrips.
///     </para>
/// </remarks>
public class SaveChangesInLoopSample
{
    /// <summary>
    ///     Runs the sample demonstrating the transaction loop.
    /// </summary>
    /// <param name="users">The source collection of users to update.</param>
    public static void Run(IEnumerable<User> users)
    {
        Console.WriteLine("Testing LC010...");
        using var db = new AppDbContext();

        // VIOLATION: Calling SaveChanges() inside the loop.
        // This triggers N separate database transactions.
        foreach (var user in users)
        {
            user.Name += " Updated";
            db.SaveChanges();
        }
    }
}
