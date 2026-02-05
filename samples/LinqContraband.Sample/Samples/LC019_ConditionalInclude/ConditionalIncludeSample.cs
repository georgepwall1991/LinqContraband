using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC019_ConditionalInclude;

/// <summary>
///     Demonstrates the "Conditional Include Expression" violation (LC019).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Using a ternary or null-coalescing expression inside
///         <c>Include()</c> or <c>ThenInclude()</c>.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> EF Core cannot translate conditional expressions inside Include.
///         This <em>always</em> throws <c>InvalidOperationException</c> at runtime — it's not a performance
///         issue, it's a guaranteed crash.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Split into separate conditional Include calls using an if statement.
///     </para>
/// </remarks>
public class ConditionalIncludeSample
{
    /// <summary>
    ///     Runs the sample demonstrating the conditional include violation.
    /// </summary>
    public static void Run(IQueryable<User> users, bool includeOrders)
    {
        Console.WriteLine("Testing LC019...");

        // VIOLATION: Conditional expression inside Include always throws at runtime
        var result = users.Include(u => includeOrders ? (object)u.Orders : u.Roles).ToList();

        // CORRECT: Split into separate conditional Include calls
        var query = users.AsQueryable();
        if (includeOrders)
            query = query.Include(u => u.Orders);
        else
            query = query.Include(u => u.Roles);
        var correctResult = query.ToList();
    }
}
