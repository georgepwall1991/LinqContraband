using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC051_ToAsyncEnumerableOnQuery;

public static class ToAsyncEnumerableOnQuerySample
{
    public static async Task RunAsync(AppDbContext db)
    {
        Console.WriteLine("Testing LC051...");

        // VIOLATION: System.Linq.AsyncEnumerable.ToAsyncEnumerable() enumerates the query synchronously.
        await foreach (var user in db.Users.Where(u => u.Age >= 18).ToAsyncEnumerable())
        {
            Console.WriteLine(user.Name);
        }

        // CORRECT: EF Core's AsAsyncEnumerable() streams the rows asynchronously.
        await foreach (var user in db.Users.Where(u => u.Age >= 18).AsAsyncEnumerable())
        {
            Console.WriteLine(user.Name);
        }
    }
}
