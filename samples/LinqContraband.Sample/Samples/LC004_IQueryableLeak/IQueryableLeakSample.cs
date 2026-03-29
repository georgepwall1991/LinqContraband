using LinqContraband.Sample.Data;
using System.Linq;

namespace LinqContraband.Sample.Samples.LC004_IQueryableLeak;

/// <summary>
///     Demonstrates the "Deferred Execution Leak" violation (LC004).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Passing an <c>IQueryable&lt;T&gt;</c> to a method that accepts
///         <c>IEnumerable&lt;T&gt;</c>,
///         where that method then iterates the collection.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> Accepting <c>IEnumerable&lt;T&gt;</c> signals "I am working with an in-memory
///         collection".
///         When you pass a database query to it, you lose the ability to add more SQL filters (like <c>Where</c> or
///         <c>Take</c>)
///         <em>inside</em> the called method. The query is executed "as is" when iteration begins, potentially fetching
///         too much data.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Change the parameter type to <c>IQueryable&lt;T&gt;</c> if the method intends to
///         extend the query,
///         or explicitly materialize (e.g., <c>ToList()</c>) at the call site if you truly mean to pass data.
///     </para>
/// </remarks>
public class IQueryableLeakSample
{
    /// <summary>
    ///     Runs the sample demonstrating the leak.
    /// </summary>
    public static void Run()
    {
        Console.WriteLine("Testing LC004...");
        using var db = new AppDbContext();

        // VIOLATION: Passing a raw IQueryable (db.Users) to a method expecting IEnumerable.
        // This implicitly opts out of further SQL composition.
        ProcessUsers(db.Users);
    }

    /// <summary>
    ///     A method that unintentionally triggers database execution by iterating.
    /// </summary>
    /// <param name="users">Defined as IEnumerable, limiting SQL translation options.</param>
    private static void ProcessUsers(IEnumerable<User> users)
    {
        // Execution happens here. We cannot say "users.Where(...)" and have it run on DB
        // because 'users' is treated as IEnumerable.
        foreach (var user in users) Console.WriteLine(user.Id);
    }
}
