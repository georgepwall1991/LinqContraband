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
///         <strong>Why it's bad:</strong> EF Core cannot translate a materializer inside a projection to SQL.
///         This forces client-side evaluation or throws in EF Core 3+. EF Core handles collection projections
///         natively without needing explicit materialization.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Remove the <c>ToList()</c>/<c>ToArray()</c> call from the projection.
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

        // VIOLATION: ToList() inside Select forces client-side evaluation
        var result = users.Select(u => u.Orders.ToList()).ToList();

        // CORRECT: EF Core handles collection projection natively
        var correctResult = users.Select(u => u.Orders).ToList();
    }
}
