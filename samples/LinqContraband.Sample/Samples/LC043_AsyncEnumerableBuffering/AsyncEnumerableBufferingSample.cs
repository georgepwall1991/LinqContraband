using System.Collections.Generic;
using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC043_AsyncEnumerableBuffering;

public static class AsyncEnumerableBufferingSample
{
    public static async Task RunAsync(AppDbContext db)
    {
        Console.WriteLine("Testing LC043...");

        // ADVISORY: Buffering the async stream before a single loop.
        var users = await db.Users.AsAsyncEnumerable().ToListAsync();
        foreach (var user in users)
        {
            Console.WriteLine(user.Name);
        }

        // CORRECT: Stream directly.
        await foreach (var user in db.Users.AsAsyncEnumerable())
        {
            Console.WriteLine(user.Name);
        }
    }
}

internal static class AsyncEnumerableBufferingDemoExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
