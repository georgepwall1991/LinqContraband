using LinqContraband.Sample.Data;
using System.Linq;

namespace LinqContraband.Sample.Samples.LC001_LocalMethod;

/// <summary>
///     Demonstrates the "Local Method Smuggler" violation (LC001).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Using a local C# method inside an EF Core query expression.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> EF Core cannot translate local C# methods into SQL.
///         This forces the query to be evaluated on the client side (fetching all rows into memory)
///         or throws a runtime exception depending on the provider.
///     </para>
///     <para>
///         <strong>The Fix:</strong> Extract the logic outside the query or use only methods that
///         can be translated to SQL (like <c>string.Contains</c>, <c>Math.Abs</c>, etc.).
///     </para>
/// </remarks>
public class LocalMethodSample
{
    /// <summary>
    ///     Runs the sample demonstrating the violation.
    /// </summary>
    /// <param name="users">The source queryable of users.</param>
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC001...");

        // VIOLATION: IsAdult is a local method and cannot be translated to SQL.
        // EF Core will likely fetch ALL users from the database and filter them in memory.
        var localResult = users.Where(u => IsAdult(u.Age)).ToList();
    }

    /// <summary>
    ///     A local helper method that causes the violation.
    /// </summary>
    private static bool IsAdult(int age)
    {
        return age >= 18;
    }
}
