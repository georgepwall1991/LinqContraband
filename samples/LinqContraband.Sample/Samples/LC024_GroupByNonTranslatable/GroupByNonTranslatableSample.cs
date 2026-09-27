using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC024_GroupByNonTranslatable;

/// <summary>
///     Demonstrates the "GroupBy Non-Translatable Projection" violation (LC024).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Handing the group to something EF Core cannot translate in a
///         <c>GroupBy().Select()</c> projection, such as a local helper method.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> EF Core translates <c>g.Key</c>, aggregates and (from EF Core 8)
///         element operators such as <c>g.First()</c> or <c>g.ToList()</c>. A helper method that receives the
///         group cannot be translated, so the query throws at runtime.
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

        // VIOLATION: a local helper that receives the group cannot be translated to SQL
        var badResult = orders
            .GroupBy(o => o.Id)
            .Select(g => new { Key = g.Key, Summary = Summarize(g) })
            .ToList();

        // CORRECT: Only Key and aggregate functions
        var correctResult = orders
            .GroupBy(o => o.Id)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToList();
    }

    private static string Summarize(IEnumerable<Order> orders) => string.Join(",", orders.Select(o => o.Id));
}
