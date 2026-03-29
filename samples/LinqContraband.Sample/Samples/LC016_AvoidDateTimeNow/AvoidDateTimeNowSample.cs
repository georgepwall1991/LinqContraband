using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC016_AvoidDateTimeNow;

public class AvoidDateTimeNowSample
{
    private readonly AppDbContext _db;

    public AvoidDateTimeNowSample(AppDbContext db)
    {
        _db = db;
    }

    public void Run()
    {
        var now1 = DateTime.Now;
        // ❌ The Crime: Using DateTime.Now directly in the query.
        // This prevents query plan caching because the constant value changes every execution.
        // It also makes unit testing impossible without mocking the system clock.
        var badQuery = _db.ConfigurationEntities
            .AsNoTracking()
            .Where(c => c.CreatedAt < now1)
            .ToList();

        // ✅ The Fix: Store the date in a variable first.
        var now = DateTime.Now;
        var goodQuery = _db.ConfigurationEntities
            .AsNoTracking()
            .Where(c => c.CreatedAt < now)
            .ToList();
    }
}
