using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC039_NestedSaveChanges;

public static class NestedSaveChangesSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC039...");

        // ADVISORY: Multiple saves on the same context in one scope.
        db.SaveChanges();
        db.SaveChanges();
    }
}
