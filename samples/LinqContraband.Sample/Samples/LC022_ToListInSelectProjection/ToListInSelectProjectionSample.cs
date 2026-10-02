using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC022_ToListInSelectProjection;

/// <summary>
///     Demonstrates the "Nested Collection Materialization Inside Projection" violation (LC022).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling <c>ToDictionary()</c> on a nested collection inside a <c>Select()</c>
///         projection on an IQueryable. EF Core cannot translate it and throws at run time.
///     </para>
///     <para>
///         <strong>Why review it:</strong> On EF Core 8 and later with a relational provider, a nested
///         <c>ToList()</c>, <c>ToArray()</c> or <c>ToHashSet()</c> produces the same SQL as the bare collection, so
///         LC022 reports only <c>ToDictionary()</c> there. On older EF Core it also reports the other materializers as
///         an advisory query-shape review.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Project the nested rows as a list and build the dictionary after the query.
///     </para>
/// </remarks>
public class ToListInSelectProjectionSample
{
    /// <summary>
    ///     Runs the sample demonstrating the nested-materialization violation.
    /// </summary>
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC022...");

        // VIOLATION: EF Core cannot translate a nested ToDictionary and throws at run time
        var result = users.Select(u => new { u.Id, OrdersById = u.Orders.ToDictionary(o => o.Id) }).ToList();

        // CORRECT: project a list, then build the dictionary in memory
        var correctResult = users
            .Select(u => new { u.Id, Orders = u.Orders.ToList() })
            .AsEnumerable()
            .Select(u => new { u.Id, OrdersById = u.Orders.ToDictionary(o => o.Id) })
            .ToList();
    }
}
