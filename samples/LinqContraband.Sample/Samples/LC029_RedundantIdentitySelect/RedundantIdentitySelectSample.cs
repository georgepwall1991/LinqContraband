using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC029_RedundantIdentitySelect;

public class RedundantIdentitySelectSample
{
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC029...");

        // VIOLATION: Redundant identity projection.
        var result1 = users.Select(u => u).ToList();

        // CORRECT: Remove the identity projection.
        var result2 = users.ToList();
    }
}
