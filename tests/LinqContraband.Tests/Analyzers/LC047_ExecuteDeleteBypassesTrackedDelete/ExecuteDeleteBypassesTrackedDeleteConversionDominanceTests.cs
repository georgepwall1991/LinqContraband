using LinqContraband.Tests.Architecture;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    [Fact]
    public async Task ExecuteDelete_WithDeletedReadOnSiblingArmFromAddedWrite_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
                if (entry.State == EntityState.Added)
                    ((User)entry.Entity).CreatedAt = DateTime.UtcNow;
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
    public async Task ExecuteDelete_WithNegatedDeletedContinueThenConvert_ShouldTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Deleted)
                    continue;
                entry.State = EntityState.Modified;
                ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithInterceptorDominatingDeletedCheck_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            foreach (var entry in eventData.Context.ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Deleted)
                    continue;
                entry.State = EntityState.Modified;
                ((ISoftDelete)entry.Entity).IsDeleted = true;
            }
            return result;
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.AddInterceptors(new SoftDeleteInterceptor());
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
    public async Task ExecuteDelete_WithTypedEntriesDominatingDeletedCheck_ShouldTriggerOnUser()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class Order
    {
        public int Id { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Order> Orders { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (entry.State == EntityState.Deleted)
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
            var users = {|LC047:db.Users.ExecuteDelete()|};
            var orders = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithDeletedReadInMethodAndConversionInUncalledLocalFunction_ShouldNotTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
            }
            return base.SaveChanges();

            void Convert()
            {
                foreach (var entry in ChangeTracker.Entries())
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
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
    public async Task ExecuteDelete_WithDetachedAssignmentUnderDeletedCheck_ShouldNotTrigger()
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
                    entry.State = EntityState.Detached;
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
    public async Task ExecuteDelete_WithSwitchArmsConvertingAddedAndModifiedOnly_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public DateTime TouchedAt { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                    case EntityState.Modified:
                        ((User)entry.Entity).TouchedAt = DateTime.UtcNow;
                        break;
                    case EntityState.Deleted:
                        System.Console.WriteLine(entry.Entity);
                        break;
                }
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
    public async Task ExecuteDelete_WithSwitchDeletedArmConversion_ShouldTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                switch (entry.State)
                {
                    case EntityState.Deleted:
                        entry.State = EntityState.Modified;
                        ((ISoftDelete)entry.Entity).IsDeleted = true;
                        break;
                    case EntityState.Added:
                        break;
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
    public async Task ExecuteDelete_WithCalledHelperUnderDeletedCheck_ShouldTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    Convert(entry);
            }
            return base.SaveChanges();
        }

        private void Convert(EntityEntry entry)
        {
            entry.State = EntityState.Modified;
            ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithInterceptorSiblingDeletedReadAndAddedWrite_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            foreach (var entry in eventData.Context.ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
                if (entry.State == EntityState.Added)
                    ((User)entry.Entity).CreatedAt = DateTime.UtcNow;
            }
            return result;
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.AddInterceptors(new SoftDeleteInterceptor());
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
    public async Task ExecuteDelete_WithClassicDeletedIfBodyConversion_ShouldTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithDeletedOnLeftOfEquality_ShouldTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (EntityState.Deleted == entry.State)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithPropertyWriteOnlyUnderDeletedCheck_ShouldTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    ((User)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithNegatedDeletedReturnThenConvert_ShouldTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
                ConvertIfDeleted(entry);
            return base.SaveChanges();
        }

        private void ConvertIfDeleted(EntityEntry entry)
        {
            if (entry.State != EntityState.Deleted)
                return;
            entry.State = EntityState.Modified;
            ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithCalledLocalFunctionUnderDeletedCheck_ShouldTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    Convert(entry);
            }

            void Convert(EntityEntry current)
            {
                current.State = EntityState.Modified;
                ((ISoftDelete)current.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithIsPatternDeletedThenConvert_ShouldTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State is EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithConversionInElseOfDeletedCheck_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public DateTime TouchedAt { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
                else
                    ((User)entry.Entity).TouchedAt = DateTime.UtcNow;
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
    public async Task ExecuteDelete_WithDeletedContinueThenConvert_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    continue;
                entry.State = EntityState.Modified;
                ((User)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithHelperConversionCalledWithoutDeletedCheck_ShouldNotTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
                Convert(entry);
            }
            return base.SaveChanges();
        }

        private void Convert(EntityEntry entry)
        {
            entry.State = EntityState.Modified;
            ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithConversionAfterDeletedLogWithoutContinue_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
                entry.State = EntityState.Modified;
                ((User)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithModifiedStateGuardThenConvert_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public DateTime TouchedAt { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
                if (entry.State == EntityState.Modified)
                    ((User)entry.Entity).TouchedAt = DateTime.UtcNow;
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
    public async Task ExecuteDeleteAsync_WithSiblingDeletedReadAndAddedWrite_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    System.Console.WriteLine(entry.Entity);
                if (entry.State == EntityState.Added)
                    ((User)entry.Entity).CreatedAt = DateTime.UtcNow;
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var result = await db.Users.ExecuteDeleteAsync();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithUndominatedUserCastAndContextWideStateConversion_ShouldTriggerOnOrder()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class Order
    {
        public int Id { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Order> Orders { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                    entry.State = EntityState.Modified;
                if (entry.State == EntityState.Added)
                    ((User)entry.Entity).CreatedAt = DateTime.UtcNow;
            }
            return base.SaveChanges();
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var users = {|LC047:db.Users.ExecuteDelete()|};
            var orders = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithGotoSkippingNegatedDeletedContinue_ShouldNotTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                goto Convert;
                if (entry.State != EntityState.Deleted)
                    continue;
            Convert:
                entry.State = EntityState.Modified;
                ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithContextStatePropertyDeletedGuard_ShouldNotTrigger()
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
        public EntityState State { get; set; }
        public override int SaveChanges()
        {
            if (State == EntityState.Deleted)
            {
                foreach (var entry in ChangeTracker.Entries())
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
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
    public async Task ExecuteDelete_WithSwitchOnUnrelatedEntityState_ShouldNotTrigger()
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
        public override int SaveChanges()
        {
            var mode = EntityState.Added;
            foreach (var entry in ChangeTracker.Entries())
            {
                switch (mode)
                {
                    case EntityState.Deleted:
                        entry.State = EntityState.Modified;
                        ((ISoftDelete)entry.Entity).IsDeleted = true;
                        break;
                    case EntityState.Added:
                        break;
                }
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
    public async Task ExecuteDelete_WithNegatedDeletedInnerLocalFunctionThenContinue_ShouldNotTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Deleted)
                {
                    void Log()
                    {
                        System.Console.WriteLine(entry.Entity);
                    }
                    continue;
                }
                entry.State = EntityState.Modified;
                ((ISoftDelete)entry.Entity).IsDeleted = true;
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
    public async Task ExecuteDelete_WithPropertyPatternDeletedThenConvert_ShouldNotTrigger()
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
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry is { State: EntityState.Deleted })
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
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
    public void ConversionScan_RequiresDeletedDominanceBeforeRecordingAssignment()
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
        Assert.Contains("deletedDominates", source, StringComparison.Ordinal);
        Assert.Contains("RecordAssignment(assignment, aggregate)", source, StringComparison.Ordinal);
        var recordIndex = source.IndexOf("RecordAssignment(assignment, aggregate)", StringComparison.Ordinal);
        var guardIndex = source.LastIndexOf("deletedDominates", recordIndex, StringComparison.Ordinal);
        Assert.True(guardIndex >= 0, "RecordAssignment must be dominated by a Deleted-state test.");
    }
}
