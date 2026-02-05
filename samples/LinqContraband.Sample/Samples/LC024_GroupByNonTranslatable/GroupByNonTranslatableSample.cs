using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC024_GroupByNonTranslatable;

/// <summary>
///     Demonstrates the "GroupBy Non-Translatable Projection" violation (LC024).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Accessing group elements in a <c>GroupBy().Select()</c> projection
///         using non-aggregate methods like <c>g.ToList()</c>, <c>g.Where()</c>, or <c>g.First()</c>.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> EF Core can only translate <c>g.Key</c> and aggregate functions
///         (<c>Count</c>, <c>Sum</c>, <c>Average</c>, <c>Min</c>, <c>Max</c>) to SQL. Any other access to group
///         elements forces client-side evaluation or throws.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Use only <c>g.Key</c> and aggregate functions in GroupBy projections.
///     </para>
/// </remarks>
public class GroupByNonTranslatableSample
{
    /// <summary>
    ///     Runs the sample demonstrating the GroupBy non-translatable violation.
    /// </summary>
    public static void Run(IQueryable<Order> orders)
    {
        Console.WriteLine("Testing LC024...");

        // VIOLATION: g.ToList() cannot be translated to SQL
        var badResult = orders
            .GroupBy(o => o.Id)
            .Select(g => new { Key = g.Key, Items = g.ToList() })
            .ToList();

        // CORRECT: Only Key and aggregate functions
        var correctResult = orders
            .GroupBy(o => o.Id)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToList();
    }
}
