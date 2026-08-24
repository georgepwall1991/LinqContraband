using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeAnalyzerTrackedDeletePipelineTests
{
    [Fact]
    public async Task RemoveRange_WithDeletedDominatedModifiedStateOnly_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    entry.State = EntityState.Modified;
            }
            return base.SaveChanges();
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithDeletedDominatedUnchangedStateOnly_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext
    {
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    entry.State = EntityState.Unchanged;
            }
            return base.SaveChanges();
        }
    }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);
            db.Users.RemoveRange(usersToDelete);
        }
    }
}" + PipelineMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
