using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC061_UncachedCompiledQuery;

public static class UncachedCompiledQuerySample
{
    // CORRECT: compiled once, when the class is first used, and reused by every call.
    private static readonly Func<AppDbContext, int, Task<int>> CountAdultsQuery =
        EF.CompileAsyncQuery((AppDbContext db, int minAge) => db.Users.Count(u => u.Age >= minAge));

    public static Task<int> CountAdultsSlowAsync(AppDbContext db, int minAge)
    {
        // VIOLATION: builds and compiles the query on every call, which costs more than an ordinary
        // LINQ query that hits EF Core's query cache.
        return EF.CompileAsyncQuery((AppDbContext context, int age) => context.Users.Count(u => u.Age >= age))(db, minAge);
    }

    public static Task<int> CountAdultsAsync(AppDbContext db, int minAge)
    {
        return CountAdultsQuery(db, minAge);
    }
}
