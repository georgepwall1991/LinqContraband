using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesAnalyzer,
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesFixer>;

namespace LinqContraband.Tests.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

/// <summary>
/// Leftover <c>UnsupportedExecuteUpdateReceiverSteps</c> and materializer-strip
/// names the shipped fixer fixtures do not isolate. <c>Take</c> already declines
/// and <c>ToList</c>/<c>ToListAsync</c> already strip. Dropping <c>Skip</c> or
/// <c>Distinct</c> from the unsupported-receiver set offers a rewrite EF Core
/// rejects at run time. Dropping <c>ToArray</c> or <c>ToArrayAsync</c> from
/// the strip list leaves the materializer on the ExecuteUpdate receiver.
/// </summary>
public partial class ExecuteUpdateForBulkUpdatesFixerTests
{
    [Fact]
    public async Task Fixer_QueryChainWithSkip_DoesNotRegister()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive).Skip(1))
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_QueryChainWithDistinct_DoesNotRegister()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive).Distinct())
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_StaticQueryChainWithSkip_DoesNotRegister()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in Queryable.Where(Queryable.Skip(db.Users, 1), u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_StaticQueryChainWithDistinct_DoesNotRegister()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in Queryable.Where(Queryable.Distinct(db.Users), u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_InlineToArray_StripsMaterializer()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive).ToArray())
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_AwaitedInlineToArrayAsync_StripsMaterializer()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in await db.Users.Where(u => u.IsActive).ToArrayAsync())
        {
            user.Name = ""Archived"";
        }|}
        await db.SaveChangesAsync();
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        await db.Users.Where(u => u.IsActive).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
        await db.SaveChangesAsync();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
