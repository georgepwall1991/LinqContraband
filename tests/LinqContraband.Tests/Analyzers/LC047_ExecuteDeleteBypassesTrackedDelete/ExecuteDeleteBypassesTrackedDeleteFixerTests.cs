using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer,
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer,
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteFixer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteFixerTests
{
    private static string App(string body) =>
        @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
" + ExecuteDeleteBypassesTrackedDeleteTests.EfMock + @"
namespace TestApp
{
" + body + @"
}";

    [Fact]
    public async Task Fixer_RewritesExecuteDeleteToExecuteUpdate()
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
            var result = {|LC047:db.Users.Where(u => u.Id > 10).ExecuteDelete()|};
        }
    }
");

        var fixedCode = App(@"
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
            var result = db.Users.Where(u => u.Id > 10).ExecuteUpdate(setters => setters.SetProperty(e => e.IsDeleted, true));
        }
    }
");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_RewritesExecuteDeleteAsyncToExecuteUpdateAsync()
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
        public async Task Run(AppDbContext db)
        {
            var result = await {|LC047:db.Users.ExecuteDeleteAsync()|};
        }
    }
");

        var fixedCode = App(@"
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
        public async Task Run(AppDbContext db)
        {
            var result = await db.Users.ExecuteUpdateAsync(setters => setters.SetProperty(e => e.IsDeleted, true));
        }
    }
");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_DoesNotRegisterForClientCascade()
    {
        var test = App(@"
    public sealed class Order
    {
        public int Id { get; set; }
        public System.Collections.Generic.IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|#0:db.Orders.ExecuteDelete()|};
        }
    }
");

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = test
        };
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC047")
                .WithLocation(0)
                .WithArguments("ExecuteDelete", "Order"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task Fixer_DoesNotRegisterForAppliedConfigurationClientCascade()
    {
        var test = App(@"
    public sealed class Order
    {
        public int Id { get; set; }
        public System.Collections.Generic.IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }

    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|#0:db.Orders.ExecuteDelete()|};
        }
    }
");

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = test
        };
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC047")
                .WithLocation(0)
                .WithArguments("ExecuteDelete", "Order"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixAll_RewritesEverySoftDeleteExecuteDelete()
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
            var first = {|#0:db.Users.Where(u => u.Id > 10).ExecuteDelete()|};
            var second = {|#1:db.Users.Where(u => u.Id < 0).ExecuteDelete()|};
        }
    }
");

        var fixedCode = App(@"
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
            var first = db.Users.Where(u => u.Id > 10).ExecuteUpdate(setters => setters.SetProperty(e => e.IsDeleted, true));
            var second = db.Users.Where(u => u.Id < 0).ExecuteUpdate(setters => setters.SetProperty(e => e.IsDeleted, true));
        }
    }
");

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "UseExecuteUpdateForSoftDelete"
        };
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC047")
                .WithLocation(0)
                .WithArguments("ExecuteDelete", "User"));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC047")
                .WithLocation(1)
                .WithArguments("ExecuteDelete", "User"));

        await testObj.RunAsync();
    }
}
