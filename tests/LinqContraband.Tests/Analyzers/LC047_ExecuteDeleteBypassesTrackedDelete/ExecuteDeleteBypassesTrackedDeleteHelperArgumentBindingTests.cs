using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    // Leftover #468/#488 entry-linkage walks that #503's property-holder /
    // caller-argument / double-not fixtures do not isolate:
    // * TryGetEntrySymbol's IInvocationOperation receiver walk
    // * TryGetTestedParameter's multi-hop property walk on the helper body
    // * TryGetTestedArgumentSymbol matching a non-first / named-reordered parameter

    [Fact]
    public async Task ExecuteDelete_WithAccessorInvocationDeletedThenConvert_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class EntryBox
    {
        public EntityEntry<User> Current { get; set; }
        public EntityEntry<User> GetCurrent() => Current;
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                var box = new EntryBox { Current = entry };
                if (box.GetCurrent().State == EntityState.Deleted)
                {
                    box.GetCurrent().State = EntityState.Modified;
                    box.GetCurrent().Entity.IsDeleted = true;
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
    public async Task ExecuteDelete_WithHelperPredicateOnPropertyHolderParameter_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class EntryBox
    {
        public EntityEntry<User> Current { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }

        private static bool IsDeletedBox(EntryBox box)
            => box.Current.State == EntityState.Deleted;

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                var box = new EntryBox { Current = entry };
                if (IsDeletedBox(box))
                {
                    box.Current.State = EntityState.Modified;
                    box.Current.Entity.IsDeleted = true;
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
    public async Task ExecuteDelete_WithHelperPredicateOnSecondParameter_ShouldTrigger()
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

        private static bool IsDeletedEntry(string reason, EntityEntry<User> entry)
            => entry.State == EntityState.Deleted;

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (IsDeletedEntry(""soft-delete"", entry))
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
    public async Task ExecuteDelete_WithHelperPredicateOnNamedReorderedSecondParameter_ShouldTrigger()
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

        private static bool IsDeletedEntry(string reason, EntityEntry<User> entry)
            => entry.State == EntityState.Deleted;

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (IsDeletedEntry(entry: entry, reason: ""soft-delete""))
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
