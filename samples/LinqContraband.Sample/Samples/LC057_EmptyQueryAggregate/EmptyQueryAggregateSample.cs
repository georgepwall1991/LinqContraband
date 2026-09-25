using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC057_EmptyQueryAggregate;

public static class EmptyQueryAggregateSample
{
    public static int OldestAgeOver(AppDbContext db, int minimumAge)
    {
        // VIOLATION: when no user is older than minimumAge, SQL MAX returns NULL and EF Core
        // throws "Sequence contains no elements" because int cannot hold it.
        return db.Users
            .Where(user => user.Age > minimumAge)
            .Max(user => user.Age);
    }

    public static int? OldestAgeOverOrNull(AppDbContext db, int minimumAge)
    {
        // CORRECT: the nullable selector makes an empty query return null.
        return db.Users
            .Where(user => user.Age > minimumAge)
            .Max(user => (int?)user.Age);
    }
}
