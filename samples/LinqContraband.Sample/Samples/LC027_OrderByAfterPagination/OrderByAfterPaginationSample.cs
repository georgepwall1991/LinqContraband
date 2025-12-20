using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC027_OrderByAfterPagination;

public class OrderByAfterPaginationSample
{
    public static void Run(IQueryable<User> users)
    {
        Console.WriteLine("Testing LC027...");

        // VIOLATION: OrderBy after Skip
        var result1 = users.Skip(10).OrderBy(u => u.Name).ToList();

        // VIOLATION: OrderBy after Take
        var result2 = users.Take(5).OrderBy(u => u.Age).ToList();

        // CORRECT: Sort before pagination
        var result3 = users.OrderBy(u => u.Name).Skip(10).ToList();
    }
}
