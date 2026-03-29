using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC018_AvoidFromSqlRawWithInterpolation;

public class AvoidFromSqlRawWithInterpolationSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC018...");

        var name = "admin";

        // VIOLATION: Potential SQL Injection using interpolated string
        var users1 = db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Name = '{name}'").ToList();

        // VIOLATION: Potential SQL Injection using string concatenation
        var users2 = db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = '" + name + "'").ToList();

        // CORRECT: Safe parameterization
        var users3 = db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = {0}", name).ToList();

        // CORRECT: Using FromSqlInterpolated
        var users4 = db.Users.FromSqlInterpolated($"SELECT * FROM Users WHERE Name = {name}").ToList();
    }
}
