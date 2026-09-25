using LinqContraband.Analyzers.LC012_OptimizeRemoveRange;
using LinqContraband.Tests.Extensions;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeFixerTests
{
    // RemoveRange waits for SaveChanges; ExecuteDelete deletes when it runs. In the repo's own sample, which
    // never saves, the fix turned a no-op into a real delete, so the title says when the delete happens.
    [Fact]
    public async Task Fixer_TitleSaysDeleteRunsImmediately()
    {
        var source = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(DbSet<User> users)
        {
            users.RemoveRange(users.Where(x => x.Id > 0));
        }
    }
}";

        var titles = await CodeActionTitles.GetAsync(
            new OptimizeRemoveRangeAnalyzer(),
            new OptimizeRemoveRangeFixer(),
            source);

        Assert.Equal(new[] { "Use ExecuteDelete() (deletes immediately, not on SaveChanges)" }, titles);
    }
}
