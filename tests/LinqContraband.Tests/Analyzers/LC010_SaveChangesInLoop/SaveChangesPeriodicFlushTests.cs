using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// A long job that saves every N items keeps the change tracker small without a round trip per item:
/// <c>if (++pending % 1000 == 0) db.SaveChanges();</c>, a counter that is reset after the save, or a buffer that
/// is cleared after it. LC010 stays quiet when the counter or buffer lives across the loop's passes and the guard
/// cannot hold on every pass, and still reports a guard that reads the item, a guard without a reset, and a save
/// that an outer per-item loop repeats.
/// </summary>
public class SaveChangesPeriodicFlushTests
{
    private const string Mock = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }

    public class DbSet<T> where T : class
    {
        public void Add(T entity) { }
        public void AddRange(IEnumerable<T> entities) { }
    }
}

public class User { public int Id { get; set; } public string Name { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class UnitOfWork
{
    private readonly AppDbContext _db;
    public UnitOfWork(AppDbContext db) => _db = db;
    public void Commit() => _db.SaveChanges();
}
";

    private static string Program(string body) => Mock + @"
class Program
{
    private const int BatchSize = 500;

    async Task Run(AppDbContext db, UnitOfWork unitOfWork, User[] users, int[] tenants, List<List<User>> groups, int size)
    {
        " + body + @"
    }

    static bool Next(out int value) { value = 0; return false; }
}
";

    private static Task VerifyAsync(string body) => VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Theory]
    // The benchmark's S23: flush every thousand rows, then once more for the rest.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            if (++pending % 1000 == 0)
                db.SaveChanges();
        }

        db.SaveChanges();")]
    // The for loop's own counter, with a configured batch size.
    [InlineData(@"for (var i = 0; i < users.Length; i++)
        {
            db.Users.Add(users[i]);
            if ((i + 1) % size == 0)
                await db.SaveChangesAsync();
        }")]
    // Stepped in its own statement, against a constant.
    [InlineData(@"var count = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            count++;
            if (count % BatchSize == 0)
            {
                await db.SaveChangesAsync();
            }
        }")]
    [InlineData(@"var count = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            count += 1;
            if (0 == count % BatchSize) db.SaveChanges();
        }")]
    [InlineData(@"long count = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            count = count + 1;
            if (count % 250 == 0) db.SaveChanges();
        }")]
    // A conjunction only narrows the guard.
    [InlineData(@"for (var i = 0; i < users.Length; i++)
        {
            db.Users.Add(users[i]);
            if (i > 0 && i % 500 == 0)
                db.SaveChanges();
        }")]
    // A while loop with an index it advances.
    [InlineData(@"var index = 0;
        while (index < users.Length)
        {
            db.Users.Add(users[index]);
            index++;
            if (index % 100 == 0) db.SaveChanges();
        }")]
    // One counter across both loops: a save every hundred rows whichever tenant they belong to.
    [InlineData(@"var written = 0;
        foreach (var tenant in tenants)
        {
            foreach (var user in users)
            {
                db.Users.Add(user);
                if (++written % 100 == 0)
                    db.SaveChanges();
            }
        }")]
    // The modulo guard that also resets its counter.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            if (++pending % 100 == 0)
            {
                db.SaveChanges();
                pending = 0;
            }
        }")]
    public Task SaveEveryNthPass_IsQuiet(string body) => VerifyAsync(body);

    [Theory]
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            if (++pending >= size)
            {
                db.SaveChanges();
                pending = 0;
            }
        }")]
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            pending++;
            if (pending == BatchSize)
            {
                await db.SaveChangesAsync();
                pending = 0;
            }
        }")]
    // Counted in rows, not passes: the reset is what makes the next save wait.
    [InlineData(@"var rows = 0;
        foreach (var group in groups)
        {
            db.Users.AddRange(group);
            rows += group.Count;
            if (size <= rows)
            {
                db.SaveChanges();
                rows = 0;
            }
        }")]
    [InlineData(@"var buffer = new List<User>();
        foreach (var user in users)
        {
            buffer.Add(user);
            if (buffer.Count >= 500)
            {
                db.Users.AddRange(buffer);
                await db.SaveChangesAsync();
                buffer.Clear();
            }
        }")]
    [InlineData(@"var buffer = new List<User>();
        foreach (var user in users)
        {
            buffer.Add(user);
            if (buffer.Count > size)
            {
                db.Users.AddRange(buffer);
                db.SaveChanges();
                buffer = new List<User>();
            }
        }")]
    // One counter across both loops, reset after every save.
    [InlineData(@"var pending = 0;
        foreach (var tenant in tenants)
        {
            foreach (var user in users)
            {
                db.Users.Add(user);
                if (++pending >= 100)
                {
                    db.SaveChanges();
                    pending = 0;
                }
            }
        }")]
    public Task SaveAtAThresholdThatIsReset_IsQuiet(string body) => VerifyAsync(body);

    [Fact]
    public Task HelperThatSavesEveryNthPass_IsQuiet() => VerifyAsync(@"var pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            if (++pending % 100 == 0)
                unitOfWork.Commit();
        }");

    [Fact]
    public Task HelperThatSavesEveryPass_StillReports() => VerifyAsync(@"foreach (var user in users)
        {
            db.Users.Add(user);
            {|LC010:unitOfWork.Commit()|};
        }");

    [Theory]
    // The guard reads the item, not a count of passes.
    [InlineData(@"foreach (var user in users)
        {
            if (user.Id % 2 == 0)
                {|LC010:db.SaveChanges()|};
        }")]
    [InlineData(@"foreach (var tenant in tenants)
        {
            if (tenant % 100 == 0)
                {|LC010:db.SaveChanges()|};
        }")]
    [InlineData(@"while (Next(out var value))
        {
            if (value % 100 == 0)
                {|LC010:db.SaveChanges()|};
        }")]
    // Holds on every pass.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending % 1 == 0)
                {|LC010:db.SaveChanges()|};
        }")]
    // Saves on every pass but the hundredth.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending % 100 != 0)
                {|LC010:db.SaveChanges()|};
        }")]
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending % 100 == 0)
            {
            }
            else
            {
                {|LC010:db.SaveChanges()|};
            }
        }")]
    // Either side of an `or` can hold on every pass.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending % 100 == 0 || user.Name != null)
                {|LC010:db.SaveChanges()|};
        }")]
    // The counter does not count passes of the loop that saves.
    [InlineData(@"var seen = 0;
        foreach (var tenant in tenants)
        {
            seen++;
            foreach (var user in users)
            {
                if (seen % 2 == 0)
                    {|LC010:db.SaveChanges()|};
            }
        }")]
    [InlineData(@"var rows = 0;
        foreach (var group in groups)
        {
            rows += group.Count;
            if (rows % 100 == 0)
                {|LC010:db.SaveChanges()|};
        }")]
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            pending--;
            if (pending % 100 == 0)
                {|LC010:db.SaveChanges()|};
        }")]
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            Next(out pending);
            pending++;
            if (pending % 100 == 0)
                {|LC010:db.SaveChanges()|};
        }")]
    // Nothing steps the counter at all.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            if (pending % 100 == 0)
                {|LC010:db.SaveChanges()|};
        }")]
    public Task GuardThatIsNotACountOfPasses_StillReports(string body) => VerifyAsync(body);

    [Theory]
    // Without the reset the guard holds on every pass after the hundredth.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            if (++pending >= 100)
                {|LC010:db.SaveChanges()|};
        }")]
    [InlineData(@"var buffer = new List<User>();
        foreach (var user in users)
        {
            buffer.Add(user);
            if (buffer.Count >= 100)
            {
                db.Users.AddRange(buffer);
                {|LC010:db.SaveChanges()|};
            }
        }")]
    // A threshold of one is every pass.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending >= 1)
            {
                {|LC010:db.SaveChanges()|};
                pending = 0;
            }
        }")]
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending > 0)
            {
                {|LC010:db.SaveChanges()|};
                pending = 0;
            }
        }")]
    // The buffer is the item, or is built from it: one save per item that is large enough.
    [InlineData(@"foreach (var group in groups)
        {
            if (group.Count >= 10)
            {
                db.Users.AddRange(group);
                {|LC010:db.SaveChanges()|};
                group.Clear();
            }
        }")]
    [InlineData(@"foreach (var tenant in tenants)
        {
            var buffer = users.Where(u => u.Id == tenant).ToList();
            if (buffer.Count >= 10)
            {
                db.Users.AddRange(buffer);
                {|LC010:db.SaveChanges()|};
                buffer.Clear();
            }
        }")]
    // The reset is in a lambda that may never run.
    [InlineData(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending >= 100)
            {
                {|LC010:db.SaveChanges()|};
                Action reset = () => pending = 0;
            }
        }")]
    public Task ThresholdWithoutAReset_StillReports(string body) => VerifyAsync(body);

    [Theory]
    // The inner loop's counter starts again for every tenant, and `i % 100 == 0` holds on its first pass.
    [InlineData(@"foreach (var tenant in tenants)
        {
            for (var i = 0; i < users.Length; i++)
            {
                db.Users.Add(users[i]);
                if (i % 100 == 0)
                    {|LC010:db.SaveChanges()|};
            }
        }")]
    [InlineData(@"foreach (var tenant in tenants)
        {
            var written = 0;
            foreach (var user in users)
            {
                db.Users.Add(user);
                if (written++ % 100 == 0)
                    {|LC010:db.SaveChanges()|};
            }
        }")]
    // Declared per tenant: not followed, although the reset would keep it to one save per hundred rows.
    [InlineData(@"foreach (var tenant in tenants)
        {
            var pending = 0;
            foreach (var user in users)
            {
                db.Users.Add(user);
                if (++pending >= 100)
                {
                    {|LC010:db.SaveChanges()|};
                    pending = 0;
                }
            }
        }")]
    public Task CounterThatRestartsForEveryOuterItem_StillReports(string body) => VerifyAsync(body);
}
