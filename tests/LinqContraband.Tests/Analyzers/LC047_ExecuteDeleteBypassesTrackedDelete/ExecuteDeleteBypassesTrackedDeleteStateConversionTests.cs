using LinqContraband.Tests.Architecture;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    [Fact]
    public async Task ExecuteDelete_WithDeletedDominatedModifiedOrUnchangedStateOnly_ShouldTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
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

    public sealed class RejectDeleteContext : DbContext
    {
        public DbSet<User> Users { get; set; }
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

    public sealed class Program
    {
        public void Run(AppDbContext db, RejectDeleteContext reject)
        {
            var modified = {|LC047:db.Users.ExecuteDelete()|};
            var unchanged = {|LC047:reject.Users.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithDeletedDominatedAddedStateOnly_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    entry.State = EntityState.Added;
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Users.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public void ConversionScan_TreatsDominatedModifiedAndUnchangedAsStateConversion()
    {
        var scanPath = Path.Combine(
            RepositoryLayout.GetRepositoryRoot(),
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC047_ExecuteDeleteBypassesTrackedDelete",
            "ExecuteDeleteBypassesTrackedDeleteSaveChangesScan.cs");
        var source = File.ReadAllText(scanPath);
        Assert.Contains(
            "IsEntityStateMember(assignment.Value, \"Modified\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsEntityStateMember(assignment.Value, \"Unchanged\")",
            source,
            StringComparison.Ordinal);
        var recordIndex = source.IndexOf(
            "if (operation is IAssignmentOperation assignment && deletedDominates)",
            StringComparison.Ordinal);
        var unchangedIndex = source.IndexOf(
            "IsEntityStateMember(assignment.Value, \"Unchanged\")",
            StringComparison.Ordinal);
        Assert.True(recordIndex >= 0, "State conversion must be recorded only under Deleted dominance.");
        Assert.True(
            unchangedIndex > recordIndex,
            "Unchanged conversion must be recorded through the dominated assignment path.");
    }
}
