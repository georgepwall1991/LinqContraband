using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC018_AvoidFromSqlRawWithInterpolation;

public class AvoidFromSqlRawWithInterpolationSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC018...");

        var name = "admin";

        // VIOLATION: Potential SQL Injection using interpolated string.
        // On EF Core 8+ EF's own analyzer reports this line as EF1002 (and the concatenation below as
        // EF1003 on EF Core 10+), so LC018 stays quiet on it instead of warning twice.
        var users1 = db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Name = '{name}'").ToList();

        // VIOLATION: Potential SQL Injection using string concatenation
        var users2 = db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = '" + name + "'").ToList();

        // VIOLATION: SqlQueryRaw<T> is also a raw string sink for scalar/keyless queries.
        var userIds = db.Database.SqlQueryRaw<int>($"SELECT Id FROM Users WHERE Name = {name}").ToList();

        // VIOLATION (LC018 only): EF1002 reads the SQL from its positional slot, so a reordered named
        // argument slips past EF's analyzer. LC018 still reports it.
        var users5 = db.Users.FromSqlRaw(parameters: Array.Empty<object>(), sql: $"SELECT * FROM Users WHERE Name = {name}").ToList();

        // CORRECT: Safe parameterization
        var users3 = db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = {0}", name).ToList();

        // CORRECT: Use FromSql (EF Core 7+) and remove SQL quotes around interpolated values.
        var users4 = db.Users.FromSql($"SELECT * FROM Users WHERE Name = {name}").ToList();

        // CORRECT: SqlQuery<T> accepts FormattableString and parameterizes interpolation holes.
        var safeUserIds = db.Database.SqlQuery<int>($"SELECT Id FROM Users WHERE Name = {name}").ToList();
    }
}
