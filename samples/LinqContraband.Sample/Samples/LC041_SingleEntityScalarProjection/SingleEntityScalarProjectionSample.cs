using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC041_SingleEntityScalarProjection;

public static class SingleEntityScalarProjectionSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC041...");

        // ADVISORY: Only Name is consumed, but the full entity is materialized.
        var user = db.Users.AsNoTracking().FirstOrDefault(candidate => candidate.Age >= 18);
        Console.WriteLine(user.Name);
    }
}
