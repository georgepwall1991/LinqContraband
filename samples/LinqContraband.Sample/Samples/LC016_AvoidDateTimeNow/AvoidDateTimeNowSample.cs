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
        // ❌ The Crime: Using DateTime.Now directly in the query.
        // This hides the clock boundary inside the expression tree and makes tests harder to control.
        var badQuery = _db.ConfigurationEntities
            .AsNoTracking()
            .Where(c => c.CreatedAt < DateTime.Now)
            .OrderBy(c => c.Id)
            .Take(10)
            .ToList();

        // ✅ The Fix: Store the date in a variable first.
        // In production code, prefer an injected clock and use UtcNow for persisted timestamps.
        var now = DateTime.Now;
        var goodQuery = _db.ConfigurationEntities
            .AsNoTracking()
            .Where(c => c.CreatedAt < now)
            .OrderBy(c => c.Id)
            .Take(10)
            .ToList();
    }
}
