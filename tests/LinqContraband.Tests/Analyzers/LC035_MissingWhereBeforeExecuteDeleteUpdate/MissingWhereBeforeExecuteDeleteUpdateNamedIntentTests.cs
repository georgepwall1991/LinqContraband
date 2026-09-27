using VerifyCS = LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateVerifier;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public partial class MissingWhereBeforeExecuteDeleteUpdateTests
{
    private const string NamedIntentTypes = @"
namespace TestApp
{
    public sealed class ActivityLogEntity { public int Id { get; set; } }
    public sealed class MentionEntity { public int Id { get; set; } }
    public sealed class Session { public int Id { get; set; } }
    public sealed class Category { public int Id { get; set; } }

    public class BlogDbContext : DbContext
    {
        public DbSet<ActivityLogEntity> ActivityLog { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<Category> Categories { get; set; }
    }
";

    [Fact]
    public async Task ClearAllMethod_DoesNotTrigger()
    {
        // Moonglade BlogDbContextExtension.ClearAllData wipes every table on purpose.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public static class BlogDbContextExtension
    {
        public static async Task ClearAllData(this BlogDbContext context)
        {
            Func<Task> work = async () =>
            {
                await context.ActivityLog.ExecuteDeleteAsync();
                await context.Sessions.ExecuteDeleteAsync();
            };
            await work();
        }

        public static int DeleteAllSessions(BlogDbContext context) => context.Sessions.ExecuteDelete();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HandlerNamedForClearingTheTable_DoesNotTrigger()
    {
        // Moonglade ClearActivityLogsCommandHandler and ClearMentionsCommandHandler.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class ClearActivityLogsCommandHandler
    {
        private readonly BlogDbContext db = new BlogDbContext();
        public Task<int> HandleAsync() => db.ActivityLog.ExecuteDeleteAsync();
    }

    public class ClearMentionsCommandHandler
    {
        private readonly BlogDbContext db = new BlogDbContext();
        public Task<int> HandleAsync() => db.Set<MentionEntity>().ExecuteDeleteAsync();
    }

    public class Maintenance
    {
        public int PurgeCategories(BlogDbContext db) => db.Categories.ExecuteDelete();
        public int ClearCategories(BlogDbContext db)
        {
            int Truncate() => db.Categories.ExecuteDelete();
            return Truncate();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NamesThatDoNotSayTheWholeTable_StillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + NamedIntentTypes + @"
    public class Account
    {
        public int ClearUserSession(BlogDbContext db) => {|LC035:db.Sessions.ExecuteDelete()|};
        public int ClearCache(BlogDbContext db) => {|LC035:db.Categories.ExecuteDelete()|};
        public int ResetPassword(BlogDbContext db) => {|LC035:db.Sessions.ExecuteUpdate()|};
        public int RemoveCategory(BlogDbContext db) => {|LC035:db.Categories.ExecuteDelete()|};
    }

    public class DeleteSessionCommandHandler
    {
        public int Handle(BlogDbContext db) => {|LC035:db.Sessions.ExecuteDelete()|};
    }

    public class ClearCategoriesCommandHandler
    {
        public int HandleAsync(BlogDbContext db) => 0;
        public int Other(BlogDbContext db) => {|LC035:db.Categories.ExecuteDelete()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
