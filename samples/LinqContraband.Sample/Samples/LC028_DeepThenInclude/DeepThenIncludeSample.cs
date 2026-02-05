using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC028_DeepThenInclude;

/// <summary>
///     Demonstrates the "Deep ThenInclude Chain" violation (LC028).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Chaining more than 3 <c>ThenInclude()</c> calls after an <c>Include()</c>,
///         creating a deeply nested eager-loading chain.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> Deep ThenInclude chains generate complex SQL with many LEFT JOINs.
///         This degrades query performance and is usually a sign of over-fetching — loading deeply nested data
///         that should be projected with <c>Select</c> instead.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Use <c>Select</c> projection to load only the needed nested properties.
///     </para>
/// </remarks>
public class DeepThenIncludeSample
{
    /// <summary>
    ///     Runs the sample demonstrating the deep ThenInclude chain violation.
    /// </summary>
    public static void Run(IQueryable<Customer> customers)
    {
        Console.WriteLine("Testing LC028...");

        // VIOLATION: 4 levels of ThenInclude (exceeds threshold of 3) — too deep
        var deepResult = customers
            .Include(c => c.ShippingAddress)
            .ThenInclude(a => a.ShippingCountry)
            .ThenInclude(c => c.ShippingRegion)
            .ThenInclude(r => r.Continent)
            .ThenInclude(c => c.Planet)
            .ToList();

        // CORRECT: Use Select projection for deeply nested data
        var correctResult = customers.Select(c => new
        {
            c.Id,
            c.Name,
            Country = c.ShippingAddress.ShippingCountry.Name
        }).ToList();
    }
}
