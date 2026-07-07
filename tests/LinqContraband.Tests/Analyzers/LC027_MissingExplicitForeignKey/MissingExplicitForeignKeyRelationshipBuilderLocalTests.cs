using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC027_MissingExplicitForeignKey.MissingExplicitForeignKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC027_MissingExplicitForeignKey;

public partial class MissingExplicitForeignKeyEdgeCasesTests
{
    [Fact]
    public async Task Navigation_WithRelationshipBuilderLocalShadowForeignKey_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer Customer { get; set; }
    }

    public class Customer { public int Id { get; set; } public System.Collections.Generic.List<Order> Orders { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var relationship = modelBuilder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany(c => c.Orders);

            relationship.HasForeignKey(""CustomerShadowId"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithSameRelationshipBuilderLocalNameInSeparateScopes_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer BillingCustomer { get; set; }
        public Customer ShippingCustomer { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            {
                var relationship = modelBuilder.Entity<Order>()
                    .HasOne(o => o.BillingCustomer)
                    .WithMany();

                relationship.HasForeignKey(""BillingCustomerShadowId"");
            }

            {
                var relationship = modelBuilder.Entity<Order>()
                    .HasOne(o => o.ShippingCustomer)
                    .WithMany();

                relationship.HasForeignKey(""ShippingCustomerShadowId"");
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithNestedLambdaShadowingRelationshipBuilderLocal_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer BillingCustomer { get; set; }
        public Customer ShippingCustomer { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var relationship = modelBuilder.Entity<Order>()
                .HasOne(o => o.BillingCustomer)
                .WithMany();

            System.Action configureShipping = () =>
            {
                var relationship = modelBuilder.Entity<Order>()
                    .HasOne(o => o.ShippingCustomer)
                    .WithMany();

                relationship.HasForeignKey(""ShippingCustomerShadowId"");
            };

            relationship.HasForeignKey(""BillingCustomerShadowId"");
            configureShipping();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithNestedNonRelationshipLocalShadowingRelationshipBuilderLocal_ShouldTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer {|LC027:Customer|} { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class RelationshipHelper
    {
        public void HasForeignKey(string name) { }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var relationship = modelBuilder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany();

            System.Action configureHelper = () =>
            {
                var relationship = new RelationshipHelper();
                relationship.HasForeignKey(""CustomerShadowId"");
            };

            configureHelper();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithNestedParameterShadowingRelationshipBuilderLocal_ShouldTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer {|LC027:Customer|} { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class RelationshipHelper
    {
        public void HasForeignKey(string name) { }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var relationship = modelBuilder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany();

            System.Action<RelationshipHelper> configureHelper =
                relationship => relationship.HasForeignKey(""CustomerShadowId"");

            configureHelper(new RelationshipHelper());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithOutVarShadowingRelationshipBuilderLocal_ShouldTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer {|LC027:Customer|} { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class RelationshipHelper
    {
        public void HasForeignKey(string name) { }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var relationship = modelBuilder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany();

            System.Action configureHelper = () =>
            {
                if (TryGet(out var relationship))
                {
                    relationship.HasForeignKey(""CustomerShadowId"");
                }
            };

            configureHelper();
        }

        private static bool TryGet(out RelationshipHelper relationship)
        {
            relationship = new RelationshipHelper();
            return true;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithForeachVariableShadowingRelationshipBuilderLocal_ShouldTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer {|LC027:Customer|} { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class RelationshipHelper
    {
        public void HasForeignKey(string name) { }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var relationship = modelBuilder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany();

            System.Action configureHelper = () =>
            {
                foreach (var relationship in new[] { new RelationshipHelper() })
                {
                    relationship.HasForeignKey(""CustomerShadowId"");
                }
            };

            configureHelper();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Navigation_WithMemberAssignmentSharingRelationshipBuilderLocalName_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order
    {
        public int Id { get; set; }
        public Customer BillingCustomer { get; set; }
        public int ShippingCustomerId { get; set; }
        public Customer ShippingCustomer { get; set; }
    }

    public class Customer { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        private ReferenceCollectionBuilder<Order, Customer> relationship;

        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var relationship = modelBuilder.Entity<Order>()
                .HasOne(o => o.BillingCustomer)
                .WithMany();

            this.relationship = modelBuilder.Entity<Order>()
                .HasOne(o => o.ShippingCustomer)
                .WithMany();

            relationship.HasForeignKey(""BillingCustomerShadowId"");
            this.relationship.HasForeignKey(""ShippingCustomerShadowId"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
