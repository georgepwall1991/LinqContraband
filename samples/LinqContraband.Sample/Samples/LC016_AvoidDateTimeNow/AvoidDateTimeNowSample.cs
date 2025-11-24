using System;
using System.Linq;
using LinqContraband.Sample.Data;

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
        // ❌ The Crime: Using DateTime.Now directly in the query.
        // This prevents query plan caching because the constant value changes every execution.
        // It also makes unit testing impossible without mocking the system clock.
        var badQuery = _db.ConfigurationEntities
            .Where(c => c.CreatedAt < DateTime.Now)
            .ToList();

        // ✅ The Fix: Store the date in a variable first.
        var now = DateTime.Now;
        var goodQuery = _db.ConfigurationEntities
            .Where(c => c.CreatedAt < now)
            .ToList();
    }
}
