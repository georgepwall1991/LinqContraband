using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC060_AsyncOnInMemoryQuery;

public static class AsyncOnInMemoryQuerySample
{
    public static async Task<List<User>> AdultsAsync(List<User> loaded, CancellationToken cancellationToken)
    {
        // VIOLATION: AsQueryable() over a list is an EnumerableQuery, which does not implement
        // IAsyncEnumerable<T>. ToListAsync throws InvalidOperationException every time.
        return await loaded.AsQueryable()
            .Where(user => user.Age >= 18)
            .ToListAsync(cancellationToken);
    }

    public static List<User> Adults(List<User> loaded)
    {
        // CORRECT: the users are already in memory, so filter them synchronously.
        return loaded.AsQueryable()
            .Where(user => user.Age >= 18)
            .ToList();
    }
}
