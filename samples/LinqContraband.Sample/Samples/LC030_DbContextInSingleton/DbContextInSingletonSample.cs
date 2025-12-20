using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC030_DbContextInSingleton;

public class DbContextInSingletonSample
{
    // VIOLATION: Holding a DbContext in a field of a generic class
    // If this class is registered as Singleton, it will cause threading issues.
    private readonly AppDbContext _db;

    public DbContextInSingletonSample(AppDbContext db)
    {
        _db = db;
    }

    public void Run()
    {
        Console.WriteLine("Testing LC030...");
        var count = _db.Users.Count();
    }
}
