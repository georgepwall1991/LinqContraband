using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC022_ToListInSelectProjection;

/// <summary>
///     Demonstrates the "ToList Inside Select Projection" violation (LC022).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling <c>ToList()</c>, <c>ToArray()</c>, or similar collection materializers
///         inside a <c>Select()</c> projection on an IQueryable.
///     </para>
///     <para>
///         <strong>Why review it:</strong> Nested materialization can add avoidable buffering, and provider/version
///         behaviour differs. Modern EF Core can translate some correlated collection projections, so treat LC022 as
///         an advisory query-shape review rather than a blanket correctness error.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Project directly, use split queries where appropriate, or keep the materializer
///         when a DTO contract requires a concrete collection.
///     </para>
/// </remarks>
public class ToListInSelectProjectionSample
{
    /// <summary>
    ///     Runs the sample demonstrating the ToList-in-Select violation.
    /// </summary>
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC022...");

        // VIOLATION: ToList() inside Select can add avoidable nested buffering
        var result = users.Select(u => u.Orders.ToList()).ToList();

        // CORRECT: project directly when a concrete nested List is not required
        var correctResult = users.Select(u => u.Orders).ToList();
    }
}
