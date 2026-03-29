using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC036_DbContextCapturedAcrossThreads;

public static class DbContextCapturedAcrossThreadsSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC036...");

        // ADVISORY: DbContext captured into background work.
        _ = Task.Run(() => db.Users.ToList());
        Parallel.ForEach(new[] { 1, 2, 3 }, _ => db.Users.Count());
    }
}
