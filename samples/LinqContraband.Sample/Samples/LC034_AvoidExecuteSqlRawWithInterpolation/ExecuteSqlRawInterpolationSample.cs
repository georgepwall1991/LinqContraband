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
        // On EF Core 8+ EF's own analyzer reports this line as EF1002, so LC034 stays quiet on it.
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM Users WHERE Name = '{name}'");

        // VIOLATION (LC034 only): a reordered named `sql:` argument slips past EF1002.
        await db.Database.ExecuteSqlRawAsync(parameters: Array.Empty<object>(), sql: $"DELETE FROM Users WHERE Name = {name}");

        // CORRECT: Use the safe interpolated API and remove SQL quotes around interpolated values.
        await db.Database.ExecuteSqlAsync($"DELETE FROM Users WHERE Name = {name}");
    }
}
