using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    private const string SoftDeleteInterceptorGraph = @"
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
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return result;
        }
    }
";

    [Fact]
    public async Task ExecuteDelete_WithAddDbContextAddInterceptors_ShouldTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
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
    public async Task ExecuteDelete_WithAddDbContextPoolAddInterceptors_ShouldTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContextPool<AppDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
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
    public async Task ExecuteDelete_WithAddDbContextFactoryAddInterceptors_ShouldTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContextFactory<AppDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
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
    public async Task ExecuteDelete_WithDiInterceptorAssignedToBaseTypedLocal_ShouldTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o =>
            {
                ISaveChangesInterceptor interceptor = new SoftDeleteInterceptor();
                o.AddInterceptors(interceptor);
            });
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
    public async Task ExecuteDelete_WithDiDerivedInterceptor_ShouldTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class SoftDeleteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            foreach (var entry in eventData.Context.ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return result;
        }
    }

    public sealed class SpecialInterceptor : SoftDeleteInterceptor
    {
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => o.AddInterceptors(new SpecialInterceptor()));
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
    public async Task ExecuteDelete_WithDiInterceptorOnGenericContext_ShouldTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext<TTenant> : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext<int>>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext<int> db)
        {
            var result = {|LC047:db.Users.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_WithAddDbContextAddInterceptors_ShouldTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
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

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithDiInterceptorOnOtherContext_ShouldNotTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class OtherDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<OtherDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
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
    public async Task ExecuteDelete_WithConversionInterceptorNeverRegistered_ShouldNotTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => { });
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
    public async Task ExecuteDelete_WithNonGenericAddDbContext_ShouldNotTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext(typeof(AppDbContext), o => o.AddInterceptors(new SoftDeleteInterceptor()));
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
    public async Task ExecuteDelete_WithLookalikeAddDbContext_ShouldNotTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public static class LocalServices
    {
        public static void AddDbContext<TContext>(Action<DbContextOptionsBuilder> optionsAction) where TContext : DbContext
        {
        }
    }

    public sealed class Startup
    {
        public void ConfigureServices()
        {
            LocalServices.AddDbContext<AppDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
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
    public async Task ExecuteDelete_WithLookalikeDiAddInterceptors_ShouldNotTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class LocalOptionsBuilder
    {
        public LocalOptionsBuilder AddInterceptors(params object[] interceptors) => this;
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => new LocalOptionsBuilder().AddInterceptors(new SoftDeleteInterceptor()));
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
    public async Task ExecuteDelete_WithOpaqueDiOptionsHelper_ShouldNotTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(Configure);
        }

        private static void Configure(DbContextOptionsBuilder options)
        {
            options.AddInterceptors(new SoftDeleteInterceptor());
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
    public async Task ExecuteDelete_WithDiOptionsLambdaCallingHelper_ShouldNotTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => Configure(o));
        }

        private static void Configure(DbContextOptionsBuilder options)
        {
            options.AddInterceptors(new SoftDeleteInterceptor());
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
    public async Task ExecuteDelete_WithAddDbContextOnFrameworkDbContext_ShouldNotTrigger()
    {
        var test = App(SoftDeleteInterceptorGraph + @"
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<DbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
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
    public async Task ExecuteDelete_WithDiInterceptorOnUnrelatedEntity_ShouldNotTrigger()
    {
        var test = App(@"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User
    {
        public int Id { get; set; }
    }

    public sealed class Order : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            foreach (var entry in eventData.Context.ChangeTracker.Entries<Order>())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                }
            }
            return result;
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(o => o.AddInterceptors(new SoftDeleteInterceptor()));
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var users = db.Users.ExecuteDelete();
            var orders = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
