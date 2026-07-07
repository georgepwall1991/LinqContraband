using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesAnalyzer,
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesFixer>;

namespace LinqContraband.Tests.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public partial class ExecuteUpdateForBulkUpdatesFixerTests
{
    [Fact]
    public async Task Fixer_DbContextSetSource_Rewrites()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Set<User>().Where(u => u.IsActive))
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
        db.Set<User>().Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_QueryChainSource_PreservesChain()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive).OrderBy(u => u.Id))
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
        db.Users.Where(u => u.IsActive).OrderBy(u => u.Id).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_QueryChainWithTake_DoesNotRegister()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive).Take(100))
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_StaticQueryChainWithTake_DoesNotRegister()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in Queryable.Where(Queryable.Take(db.Users, 100), u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_InlineToList_StripsMaterializer()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive).ToList())
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
    public async Task Fixer_AwaitedInlineToListAsync_StripsMaterializer()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in await db.Users.Where(u => u.IsActive).ToListAsync())
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
