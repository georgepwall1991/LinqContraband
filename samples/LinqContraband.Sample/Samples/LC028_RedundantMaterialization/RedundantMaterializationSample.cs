using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC028_RedundantMaterialization;

public class RedundantMaterializationSample
{
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC028...");

        // VIOLATION: AsEnumerable followed by ToList
        var result1 = users.AsEnumerable().ToList();

        // VIOLATION: Double ToList
        var result2 = users.ToList().ToList();

        // CORRECT
        var result3 = users.ToList();
    }
}
