using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC032_ExecuteUpdateForBulkUpdates;

/// <summary>
///     Demonstrates the "Use ExecuteUpdate for Provable Bulk Scalar Updates" advisory (LC032).
/// </summary>
public static class ExecuteUpdateForBulkUpdatesSample
{
    public static void Run()
    {
        Console.WriteLine("Testing LC032...");

        using var db = new AppDbContext();

        // ADVISORY: This tracked bulk update can usually become a single ExecuteUpdate() call.
        foreach (var user in db.Users.Where(u => u.Age >= 18))
        {
            user.Name = "Archived";
        }

        db.SaveChanges();
    }
}
