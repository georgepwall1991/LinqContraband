using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC037_RawSqlStringConstruction;

public static class RawSqlStringConstructionSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC037...");

        var name = "admin";
        var sql = "SELECT * FROM Users WHERE Name = '" + name + "'";

        // VIOLATION: Constructed SQL flows into FromSqlRaw.
        var users = db.Users.FromSqlRaw(sql).ToList();

        // CORRECT: Keep user input out of the SQL string.
        var safeUsers = db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = {0}", name).ToList();

        _ = users;
        _ = safeUsers;
    }
}
