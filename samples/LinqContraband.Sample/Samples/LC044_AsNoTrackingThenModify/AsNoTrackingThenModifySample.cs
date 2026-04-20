using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC044_AsNoTrackingThenModify;

public class AsNoTrackingThenModifySample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC044...");

        var userId = Guid.NewGuid();

        // VIOLATION: Silent data loss — AsNoTracking entity mutated then SaveChanges.
        // EF has no tracked entity, so SaveChanges persists nothing. No exception, no log.
        var user1 = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId);
        if (user1 != null)
        {
            user1.Name = "Will never reach the database";
            db.SaveChanges();
        }

        // CORRECT: Remove AsNoTracking so the entity is tracked from the start.
        var user2 = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user2 != null)
        {
            user2.Name = "Properly persisted";
            db.SaveChanges();
        }

        // CORRECT: AsNoTracking + explicit re-attach before SaveChanges.
        var user3 = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId);
        if (user3 != null)
        {
            user3.Name = "Re-attached before save";
            db.Users.Update(user3);
            db.SaveChanges();
        }
    }
}
