using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC008_SyncBlocker;

/// <summary>
///     Demonstrates the "Sync-over-Async" violation (LC008).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling synchronous database methods (like <c>ToList()</c>, <c>First()</c>,
///         <c>Count()</c>)
///         inside an <c>async</c> method.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> In a web server environment (ASP.NET Core), threads are a limited resource.
///         Blocking a thread to wait for database I/O (which can take milliseconds or seconds) prevents that thread
///         from serving other requests. This leads to "Thread Starvation" under load, causing the application to
///         become unresponsive even if CPU usage is low.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Use the asynchronous counterparts (e.g., <c>ToListAsync()</c>, <c>FirstAsync()</c>,
///         <c>CountAsync()</c>)
///         and <c>await</c> them. This frees up the thread to do other work while waiting for the database.
///     </para>
/// </remarks>
public class SyncBlockerSample
{
    /// <summary>
    ///     Runs the sample demonstrating the blocking call.
    /// </summary>
    /// <param name="users">The source queryable of users.</param>
    public static async Task RunAsync(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC008...");

        // VIOLATION: Blocking the thread with synchronous ToList() inside an async method.
        // The thread is held hostage while the query executes.
        var syncBlocker = users.ToList();

        await Task.Delay(10); // Ensure async context
    }
}
