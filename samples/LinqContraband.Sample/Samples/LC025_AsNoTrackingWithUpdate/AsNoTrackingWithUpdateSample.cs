using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC025_AsNoTrackingWithUpdate;

public class AsNoTrackingWithUpdateSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC025...");

        var userId = Guid.NewGuid();

        // VIOLATION: Entity is not tracked, so Update will mark all columns as modified
        var user1 = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId);
        if (user1 != null)
        {
            user1.Name = "Updated";
            db.Users.Update(user1);
        }

        // VIOLATION: Collection variant with AsNoTracking
        var users = db.Users.AsNoTracking().Where(u => u.Age > 20).ToList();
        foreach (var u in users)
        {
            db.Users.Remove(u);
        }

        // CORRECT: Entity is tracked
        var user2 = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user2 != null)
        {
            user2.Name = "Updated Correctly";
            db.SaveChanges(); // No need for Update() if tracked
        }
    }
}
