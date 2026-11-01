using VerifyCS = LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateVerifier;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

/// <summary>
/// Leftover <c>TargetMethods</c> and named-intent names the shipped fixtures do not isolate.
/// <c>ExecuteDelete</c>, <c>ExecuteDeleteAsync</c> and <c>ExecuteUpdate</c> already have
/// unfiltered positives. Dropping leftover <c>ExecuteUpdateAsync</c> fails only that
/// unfiltered pin; the filtered <c>ExecuteUpdateAsync</c> fixture stays green.
/// Dropping a leftover whole-set noun, <c>Truncate</c>, handler suffix or generic
/// entry point fails only that name's quiet pin. A lookalike <c>ExecuteUpdateAsync</c>
/// still stays quiet so a later edit that treats every matching name as EF is visible.
/// Leftover irregular/classical plurals stay unpinned.
/// </summary>
public partial class MissingWhereBeforeExecuteDeleteUpdateTests
{
    private static string NamedIntentSource(string members) =>
        "using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class Maintenance
    {
" + members + @"
    }
}";

    [Fact]
    public async Task ExecuteUpdateAsync_WithoutWhere_Reports()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public async System.Threading.Tasks.Task Run(DbContext db)
        {
            var result = await {|LC035:db.Set<User>().ExecuteUpdateAsync()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LookalikeExecuteUpdateAsync_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace OtherOrm
{
    public static class QueryExtensions
    {
        public static System.Threading.Tasks.Task<int> ExecuteUpdateAsync<TSource>(this IQueryable<TSource> source)
            => System.Threading.Tasks.Task.FromResult(0);
    }
}

namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public async System.Threading.Tasks.Task Run(DbContext db)
        {
            var result = await OtherOrm.QueryExtensions.ExecuteUpdateAsync(db.Set<User>());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TruncateCategories_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(NamedIntentSource(@"
        public int TruncateCategories(BlogDbContext db) => db.Categories.ExecuteDelete();
"));
    }

    [Fact]
    public async Task TruncateAllData_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(NamedIntentSource(@"
        public int TruncateAllData(BlogDbContext db) => db.Users.ExecuteDelete();
"));
    }

    [Fact]
    public async Task DeleteAllRows_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(NamedIntentSource(@"
        public int DeleteAllRows(BlogDbContext db) => db.Users.ExecuteDelete();
"));
    }

    [Fact]
    public async Task RemoveAllEntries_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(NamedIntentSource(@"
        public int RemoveAllEntries(BlogDbContext db) => db.Users.ExecuteDelete();
"));
    }

    [Fact]
    public async Task PurgeAllItems_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(NamedIntentSource(@"
        public int PurgeAllItems(BlogDbContext db) => db.Users.ExecuteDelete();
"));
    }

    [Fact]
    public async Task WipeAllEntities_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(NamedIntentSource(@"
        public int WipeAllEntities(BlogDbContext db) => db.Users.ExecuteDelete();
"));
    }

    [Fact]
    public async Task ClearAllTables_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(NamedIntentSource(@"
        public int ClearAllTables(BlogDbContext db) => db.Users.ExecuteDelete();
"));
    }

    [Fact]
    public async Task ClearUsersRequestHandler_Handle_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class ClearUsersRequestHandler
    {
        public int Handle(BlogDbContext db) => db.Users.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearUsersHandler_Handle_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class ClearUsersHandler
    {
        public int Handle(BlogDbContext db) => db.Users.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearUsersCommand_Handle_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class ClearUsersCommand
    {
        public int Handle(BlogDbContext db) => db.Users.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearUsersCommandHandler_Execute_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class ClearUsersCommandHandler
    {
        public int Execute(BlogDbContext db) => db.Users.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearUsersCommandHandler_Run_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class ClearUsersCommandHandler
    {
        public int Run(BlogDbContext db) => db.Users.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearUsersCommandHandler_Invoke_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class ClearUsersCommandHandler
    {
        public int Invoke(BlogDbContext db) => db.Users.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ClearUsersCommandHandler_Process_StaysQuiet()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class ClearUsersCommandHandler
    {
        public int Process(BlogDbContext db) => db.Users.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
