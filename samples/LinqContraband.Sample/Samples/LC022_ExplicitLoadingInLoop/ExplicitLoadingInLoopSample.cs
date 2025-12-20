using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC022_ExplicitLoadingInLoop;

public class ExplicitLoadingInLoopSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC022...");

        var users = db.Users.ToList();
        
        foreach (var user in users)
        {
            // VIOLATION: N+1 queries. Each iteration hits the database.
            db.Entry(user).Collection(u => u.Orders).Load();
        }

        // CORRECT: Eagerly load in a single query
        var usersWithOrders = db.Users.Include(u => u.Orders).ToList();
    }
}
