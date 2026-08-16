using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    private const string OrderGraph = @"
    public sealed class Order
    {
        public int Id { get; set; }
        public IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }
";

    [Fact]
    public async Task ExecuteDelete_WithAppliedConfigurationClientCascade_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
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
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithAppliedConfigurationClientSetNull_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientSetNull);
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
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithConfigurationAssignedToLocal_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
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
            var configuration = new OrderConfiguration();
            modelBuilder.ApplyConfiguration(configuration);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithDerivedAppliedConfiguration_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
    public class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public virtual void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class SpecialOrderConfiguration : OrderConfiguration
    {
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new SpecialOrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithExpressionBodiedConfiguration_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder) =>
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
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
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithConfigurationHelperClientCascade_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            ConfigureOrders(builder);
        }

        private void ConfigureOrders(EntityTypeBuilder<Order> builder)
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
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithHasOneInAppliedConfiguration_ShouldTriggerOnPrincipal()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : IEntityTypeConfiguration<OrderLine>
    {
        public void Configure(EntityTypeBuilder<OrderLine> builder)
        {
            builder.HasOne(l => l.Order)
                .WithMany(o => o.Lines)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLine> Lines { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var orders = {|LC047:db.Orders.ExecuteDelete()|};
            var lines = db.Lines.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithApplyConfigurationsFromAssemblyTypeOf_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
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
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderConfiguration).Assembly);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithApplyConfigurationsFromExecutingAssembly_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
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
            modelBuilder.ApplyConfigurationsFromAssembly(System.Reflection.Assembly.GetExecutingAssembly());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithApplyConfigurationsFromAssemblyLocal_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
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
            var assembly = typeof(OrderConfiguration).Assembly;
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithOneToOneAppliedConfigurationAndForeignKey_ShouldTriggerOnPrincipal()
    {
        var test = App(@"
    public sealed class Blog
    {
        public int Id { get; set; }
        public Header Header { get; set; }
    }

    public sealed class Header
    {
        public int Id { get; set; }
        public int BlogId { get; set; }
        public Blog Blog { get; set; }
    }

    public sealed class BlogConfiguration : IEntityTypeConfiguration<Blog>
    {
        public void Configure(EntityTypeBuilder<Blog> builder)
        {
            builder.HasOne(b => b.Header)
                .WithOne(h => h.Blog)
                .HasForeignKey<Header>(h => h.BlogId)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Blog> Blogs { get; set; }
        public DbSet<Header> Headers { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new BlogConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var blogs = {|LC047:db.Blogs.ExecuteDelete()|};
            var headers = db.Headers.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithUnappliedConfiguration_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
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
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithExternalApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
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
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(string).Assembly);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithAppliedConfigurationDatabaseCascade_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.Cascade);
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
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_OnDependentWithAppliedConfigurationClientCascade_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
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
        public DbSet<OrderLine> Lines { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Lines.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithAppliedOneToOneConfigurationWithoutForeignKey_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class Blog
    {
        public int Id { get; set; }
        public Header Header { get; set; }
    }

    public sealed class Header
    {
        public int Id { get; set; }
        public Blog Blog { get; set; }
    }

    public sealed class BlogConfiguration : IEntityTypeConfiguration<Blog>
    {
        public void Configure(EntityTypeBuilder<Blog> builder)
        {
            builder.HasOne(b => b.Header)
                .WithOne(h => h.Blog)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Blog> Blogs { get; set; }
        public DbSet<Header> Headers { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new BlogConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var blogs = db.Blogs.ExecuteDelete();
            var headers = db.Headers.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithAppliedConfigurationQueryFilterOnly_ShouldNotTrigger()
    {
        var test = App(@"
    public sealed class User
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.HasQueryFilter(u => !u.IsDeleted);
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new UserConfiguration());
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
    public async Task ExecuteDelete_WithConfigurationAppliedToDifferentContext_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class CatalogDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrderConfiguration());
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db, CatalogDbContext catalog)
        {
            var ignored = {|LC047:catalog.Orders.ExecuteDelete()|};
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithLookalikeEntityTypeConfiguration_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
    public interface IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        void Configure(object builder);
    }

    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(object builder)
        {
        }
    }

    public sealed class LocalModelBuilder
    {
        public LocalModelBuilder ApplyConfiguration<TEntity>(IEntityTypeConfiguration<TEntity> configuration) where TEntity : class => this;
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            new LocalModelBuilder().ApplyConfiguration(new OrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithLookalikeApplyConfiguration_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class LocalModelBuilder
    {
        public LocalModelBuilder ApplyConfiguration<TEntity>(Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<TEntity> configuration) where TEntity : class => this;
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            new LocalModelBuilder().ApplyConfiguration(new OrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithOnModelCreatingHelperApplyConfiguration_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
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
            ApplyOrderConfiguration(modelBuilder);
        }

        private void ApplyOrderConfiguration(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_WithAppliedConfigurationClientCascade_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
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
        public async Task Run(AppDbContext db)
        {
            var result = await {|LC047:db.Orders.ExecuteDeleteAsync()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithChainedApplyConfiguration_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class User
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    public sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.HasQueryFilter(u => !u.IsDeleted);
        }
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
        public DbSet<User> Users { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new UserConfiguration())
                .ApplyConfiguration(new OrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var orders = {|LC047:db.Orders.ExecuteDelete()|};
            var users = db.Users.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithAbstractConfigurationFromAssembly_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
    public abstract class OrderConfiguration : IEntityTypeConfiguration<Order>
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
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderConfiguration).Assembly);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithParameterizedConfigurationFromAssembly_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public OrderConfiguration(string name) { }
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
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderConfiguration).Assembly);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithApplyConfigurationsFromAssemblyPredicate_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
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
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(OrderConfiguration).Assembly,
                type => false);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithDerivedConfigurationOverrideDatabaseCascade_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
    public class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public virtual void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    public sealed class SpecialOrderConfiguration : OrderConfiguration
    {
        public override void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new SpecialOrderConfiguration());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithGenericConfigurationHelper_ShouldTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration<TEntity> : IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        public void Configure(EntityTypeBuilder<TEntity> builder)
        {
            ConfigureOrders(builder);
        }

        private void ConfigureOrders(EntityTypeBuilder<TEntity> builder)
        {
            if (typeof(TEntity) == typeof(Order))
            {
            }
        }
    }

    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            ConfigureRelationship(builder);
        }

        private void ConfigureRelationship(EntityTypeBuilder<Order> builder)
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
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithConstructedGenericConfigurationHelper_ShouldTrigger()
    {
        var test = App(@"
    public class Order
    {
        public int Id { get; set; }
        public IEnumerable<OrderLine> Lines { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public Order Order { get; set; }
    }

    public sealed class OrderConfiguration<TEntity> : IEntityTypeConfiguration<TEntity> where TEntity : Order
    {
        public void Configure(EntityTypeBuilder<TEntity> builder)
        {
            ConfigureRelationship(builder);
        }

        private void ConfigureRelationship(EntityTypeBuilder<TEntity> builder)
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
            modelBuilder.ApplyConfiguration(new OrderConfiguration<Order>());
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Orders.ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithNonPublicConstructorConfigurationFromAssembly_ShouldNotTrigger()
    {
        var test = App(OrderGraph + @"
    public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        protected OrderConfiguration() { }
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
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderConfiguration).Assembly);
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = db.Orders.ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
