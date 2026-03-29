using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC034_AvoidExecuteSqlRawWithInterpolation;

public static class ExecuteSqlRawInterpolationSample
{
    public static async Task RunAsync(AppDbContext db)
    {
        Console.WriteLine("Testing LC034...");

        var name = "admin";

        // VIOLATION: Interpolated user input flows into ExecuteSqlRawAsync.
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM Users WHERE Name = '{name}'");

        // CORRECT: Use the safe interpolated API instead.
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Name = {name}");
    }
}
