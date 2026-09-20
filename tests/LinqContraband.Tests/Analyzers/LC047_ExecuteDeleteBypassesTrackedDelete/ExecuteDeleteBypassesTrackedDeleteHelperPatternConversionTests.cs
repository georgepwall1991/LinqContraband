using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    [Fact]
    public async Task ExecuteDelete_WithHelperIsNotDeletedContinueThenConvert_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }

        private static bool IsLiveEntry(EntityEntry<User> entry)
            => entry.State is not EntityState.Deleted;

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (IsLiveEntry(entry))
                    continue;
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithHelperPredicateOnConvertedArgument_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }

        private static bool IsDeletedEntry(EntityEntry<User> entry)
            => entry.State == EntityState.Deleted;

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (IsDeletedEntry((EntityEntry<User>)entry))
                {
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithHelperPredicateOnConvertedParameter_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }

        private static bool IsDeletedEntry(EntityEntry<User> entry)
            => ((EntityEntry<User>)entry).State == EntityState.Deleted;

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (IsDeletedEntry(entry))
                {
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
