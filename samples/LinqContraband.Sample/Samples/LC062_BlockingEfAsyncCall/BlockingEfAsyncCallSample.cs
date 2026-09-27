using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC062_BlockingEfAsyncCall;

// Not called from Program: under a SynchronizationContext (WinForms, WPF, classic ASP.NET) these calls deadlock.
public static class BlockingEfAsyncCallSample
{
    public static User? FindUser(AppDbContext db, string name, CancellationToken cancellationToken)
    {
        // VIOLATION: blocks a thread until the query finishes. Under a SynchronizationContext the query's
        // continuation waits for this thread, which is waiting for the query: a deadlock.
        return db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Name == name, cancellationToken).Result;
    }

    public static void Rename(AppDbContext db, User user, string name, CancellationToken cancellationToken)
    {
        user.Name = name;

        // VIOLATION: the same, for SaveChangesAsync.
        db.SaveChangesAsync(cancellationToken).GetAwaiter().GetResult();
    }

    public static async Task<User?> FindUserAsync(AppDbContext db, string name, CancellationToken cancellationToken)
    {
        // CORRECT: await the EF Core call.
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Name == name, cancellationToken);
    }

    public static User? FindUserSynchronously(AppDbContext db, string name)
    {
        // CORRECT: code that cannot be async calls the synchronous EF Core API.
        return db.Users.AsNoTracking().FirstOrDefault(u => u.Name == name);
    }
}
