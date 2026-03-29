using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC040_MixedTrackingAndNoTracking;

public static class MixedTrackingAndNoTrackingSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC040...");

        // ADVISORY: Mixed tracking modes on the same context in one method.
        var trackedUsers = db.Users.ToList();
        var noTrackingUsers = db.Users.AsNoTracking().ToList();

        _ = trackedUsers;
        _ = noTrackingUsers;
    }
}
