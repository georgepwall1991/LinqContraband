using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC042_MissingQueryTags;

public static class MissingQueryTagsSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC042...");

        // ADVISORY: Complex query without a tag.
        var users = db.Users
            .Where(user => user.Age >= 18)
            .OrderBy(user => user.Name)
            .Take(10)
            .ToList();

        // CORRECT: Tagged complex query.
        var taggedUsers = db.Users
            .Where(user => user.Age >= 18)
            .OrderBy(user => user.Name)
            .Take(10)
            .TagWith("dashboard:adult-users")
            .ToList();

        _ = users;
        _ = taggedUsers;
    }
}
