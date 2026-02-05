using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC031_UnboundedQueryMaterialization;

/// <summary>
///     Demonstrates the "Unbounded Query Materialization" violation (LC031).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Calling <c>ToList()</c>, <c>ToArray()</c>, or similar collection materializers
///         on an IQueryable chain from a <c>DbSet</c> without any bounding method like <c>Take()</c>, <c>First()</c>,
///         or <c>Single()</c>.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> Without a bound, the query loads every matching row from the database into
///         memory. This is the most common cause of out-of-memory errors in production EF Core applications.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Add <c>Take(n)</c> to bound the result set, or use a single-element materializer
///         like <c>FirstOrDefault()</c>.
///     </para>
/// </remarks>
public class UnboundedQueryMaterializationSample
{
    /// <summary>
    ///     Runs the sample demonstrating the unbounded query materialization violation.
    /// </summary>
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC031...");

        // VIOLATION: No bounding method — could load millions of rows
        var result = db.Users.Where(u => u.Age > 18).ToList();

        // CORRECT: Bounded with Take()
        var correctResult = db.Users.Where(u => u.Age > 18).Take(1000).ToList();
    }
}
