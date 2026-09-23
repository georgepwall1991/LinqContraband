using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC027_MissingExplicitForeignKey.MissingExplicitForeignKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC027_MissingExplicitForeignKey;

/// <summary>
/// BTCPay Server configures each entity in a static <c>OnModelCreating(ModelBuilder builder, ...)</c> on the entity
/// class, called from the context, often through a <c>var b = builder.Entity&lt;T&gt;()</c> local. LC027 follows
/// those helpers and reads the entity from the builder's type. It also skips navigations EF Core does not map:
/// <c>[NotMapped]</c> and computed properties such as <c>PlanData NextPlan =&gt; NewPlan ?? Plan</c>.
/// </summary>
public partial class MissingExplicitForeignKeyEdgeCasesTests
{
    private const string NotMappedAttribute = @"
namespace System.ComponentModel.DataAnnotations.Schema
{
    public sealed class NotMappedAttribute : System.Attribute { }
}
";

    private static string HelperProgram(string entityMembers, string contextModelCreating, string helpers) => EFCoreMock + NotMappedAttribute + @"
namespace TestApp
{
    public class Store { public int Id { get; set; } public System.Collections.Generic.List<ApiKey> Keys { get; set; } }

    public class ApiKey
    {
        public int Id { get; set; }
        public int ParentStoreId { get; set; }
        " + entityMembers + @"
    }

    public class AppDbContext : DbContext
    {
        public DbSet<ApiKey> ApiKeys { get; set; }
        public DbSet<Store> Stores { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            " + contextModelCreating + @"
        }
    }

    public static class ModelHelpers
    {
        " + helpers + @"
    }
}";

    [Theory]
    // A static helper on another class, chained from Entity<T>().
    [InlineData("ModelHelpers.Configure(modelBuilder);",
        "public static void Configure(ModelBuilder builder) { builder.Entity<ApiKey>().HasOne(k => k.Store).WithMany(s => s.Keys).HasForeignKey<ApiKey>(k => k.ParentStoreId); }")]
    // The helper keeps the entity builder in a local, as BTCPay does.
    [InlineData("ModelHelpers.Configure(modelBuilder);",
        "public static void Configure(ModelBuilder builder) { var b = builder.Entity<ApiKey>(); b.HasOne(k => k.Store).WithMany(s => s.Keys).HasForeignKey<ApiKey>(k => k.ParentStoreId); }")]
    // An extension method on ModelBuilder, one helper deeper.
    [InlineData("modelBuilder.ConfigureKeys();",
        "public static void ConfigureKeys(this ModelBuilder builder) => ConfigureKey(builder.Entity<ApiKey>()); static void ConfigureKey(EntityTypeBuilder<ApiKey> b) { b.HasOne(k => k.Store).WithMany(s => s.Keys).HasForeignKey<ApiKey>(k => k.ParentStoreId); }")]
    // Entity<T>(b => ...) directly in OnModelCreating.
    [InlineData("modelBuilder.Entity<ApiKey>(b => b.HasOne(k => k.Store).WithMany(s => s.Keys).HasForeignKey<ApiKey>(k => k.ParentStoreId));", "")]
    public Task ForeignKeyConfiguredThroughHelperOrBuilderLocal_NoDiagnostic(string contextModelCreating, string helpers) =>
        VerifyCS.VerifyAnalyzerAsync(HelperProgram("public Store Store { get; set; }", contextModelCreating, helpers));

    [Theory]
    [InlineData("[System.ComponentModel.DataAnnotations.Schema.NotMapped] public Store Store { get; set; }")]
    [InlineData("public Store Owner { get; set; } public int OwnerId { get; set; } public Store Store => Owner;")]
    [InlineData("public Store Owner { get; set; } public int OwnerId { get; set; } public Store Store { get { return Owner; } }")]
    public Task UnmappedNavigation_NoDiagnostic(string entityMembers) =>
        VerifyCS.VerifyAnalyzerAsync(HelperProgram(entityMembers, "", ""));

    [Fact]
    public Task PrincipalSideOfOneToOne_NoDiagnostic() =>
        VerifyCS.VerifyAnalyzerAsync(EFCoreMock + @"
namespace TestApp
{
    public class ImageInfo { public int Id { get; set; } public int? UserId { get; set; } }

    public class User
    {
        public int Id { get; set; }
        public ImageInfo ProfileImage { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<ImageInfo> Images { get; set; }
    }
}");

    [Theory]
    // A get-only auto-property is mapped through its backing field.
    [InlineData("public Store {|LC027:Store|} { get; }")]
    // A computed getter over a conventionally named backing field is mapped too.
    [InlineData("private Store _store; public Store {|LC027:Store|} => _store;")]
    public Task NavigationWithBackingField_StillReports(string entityMembers) =>
        VerifyCS.VerifyAnalyzerAsync(HelperProgram(entityMembers, "", ""));

    [Theory]
    // The helper configures a different navigation.
    [InlineData("ModelHelpers.Configure(modelBuilder);",
        "public static void Configure(ModelBuilder builder) { var b = builder.Entity<Store>(); }")]
    // A helper that is never called from OnModelCreating does not configure the model.
    [InlineData("",
        "public static void Configure(ModelBuilder builder) { builder.Entity<ApiKey>().HasOne(k => k.Store).WithMany(s => s.Keys).HasForeignKey<ApiKey>(k => k.ParentStoreId); }")]
    public Task HelperThatDoesNotConfigureTheForeignKey_StillReports(string contextModelCreating, string helpers) =>
        VerifyCS.VerifyAnalyzerAsync(HelperProgram("public Store {|LC027:Store|} { get; set; }", contextModelCreating, helpers));
}
