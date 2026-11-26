using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Leftover arms of the 5.19.0 save-every-N-items pass that shipped
/// <c>int</c>/<c>long</c> remainder fixtures, <c>pending &gt;= n</c>,
/// <c>size &lt;= rows</c> and <c>buffer.Count &gt;= n</c> do not isolate:
/// integer counter types besides <c>int</c>/<c>long</c>, leftover
/// <c>TryGetIntegerConstant</c> divisor types on <c>% 1L</c> and <c>% 1u</c>,
/// the swapped <c>if (size &lt; pending)</c> threshold,
/// <c>buffer.Count == n</c> with <c>Clear()</c>, and
/// <c>if (BatchSize == pending)</c> with a reset. A lookalike <c>double</c>
/// counter still reports. <c>% (byte)1</c> / <c>% (short)1</c> compile as
/// <c>int</c> after conversion and do not isolate those constant arms.
/// <c>% 1UL</c> is a leftover FN (<c>TryGetIntegerConstant</c> has no
/// <c>ulong</c> arm) and is not locked as quiet or as a positive. Does not
/// restage <c>SaveChangesPeriodicFlushTests.cs</c>.
/// </summary>
public class SaveChangesPeriodicFlushLeftoverCoverageTests
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
";

    private static string Program(string body) => Mock + @"
class Program
{
    private const int BatchSize = 500;

    async Task Run(AppDbContext db, User[] users, int size)
    {
        " + body + @"
    }
}
";

    private static Task VerifyAsync(string body) => VerifyCS.VerifyAnalyzerAsync(Program(body));

    private static string Remainder(string type) => type + @" pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            if (++pending % 100 == 0)
                db.SaveChanges();
        }";

    [Fact]
    public Task ByteCounter_Remainder_IsQuiet() => VerifyAsync(Remainder("byte"));

    [Fact]
    public Task SByteCounter_Remainder_IsQuiet() => VerifyAsync(Remainder("sbyte"));

    [Fact]
    public Task ShortCounter_Remainder_IsQuiet() => VerifyAsync(Remainder("short"));

    [Fact]
    public Task UShortCounter_Remainder_IsQuiet() => VerifyAsync(Remainder("ushort"));

    [Fact]
    public Task UIntCounter_Remainder_IsQuiet() => VerifyAsync(Remainder("uint"));

    [Fact]
    public Task ULongCounter_Remainder_IsQuiet() => VerifyAsync(Remainder("ulong"));

    [Fact]
    public Task LookalikeDoubleCounter_Remainder_StillReports() => VerifyAsync(@"double pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            if (++pending % 100 == 0)
                {|LC010:db.SaveChanges()|};
        }");

    [Fact]
    public Task Threshold_SizeLessThanPending_WithReset_IsQuiet() => VerifyAsync(@"var pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            pending++;
            if (size < pending)
            {
                db.SaveChanges();
                pending = 0;
            }
        }");

    [Fact]
    public Task Threshold_BatchSizeEqualsPending_WithReset_IsQuiet() => VerifyAsync(@"var pending = 0;
        foreach (var user in users)
        {
            db.Users.Add(user);
            pending++;
            if (BatchSize == pending)
            {
                db.SaveChanges();
                pending = 0;
            }
        }");

    [Fact]
    public Task BufferCountEqualsThreshold_WithClear_IsQuiet() => VerifyAsync(@"var buffer = new List<User>();
        foreach (var user in users)
        {
            buffer.Add(user);
            if (buffer.Count == 500)
            {
                db.Users.AddRange(buffer);
                db.SaveChanges();
                buffer.Clear();
            }
        }");

    [Fact]
    public Task Remainder_LongOne_StillReports() => VerifyAsync(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending % 1L == 0)
                {|LC010:db.SaveChanges()|};
        }");

    [Fact]
    public Task Remainder_UIntOne_StillReports() => VerifyAsync(@"var pending = 0;
        foreach (var user in users)
        {
            if (++pending % 1u == 0)
                {|LC010:db.SaveChanges()|};
        }");
}
