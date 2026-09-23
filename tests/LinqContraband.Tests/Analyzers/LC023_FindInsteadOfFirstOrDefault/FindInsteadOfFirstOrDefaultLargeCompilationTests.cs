using LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer,
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer,
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultFixer>;

namespace LinqContraband.Tests.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public partial class FindInsteadOfFirstOrDefaultTests
{
    // Real applications have far more than 64 syntax trees. LC023 used to switch off its
    // convention-key fallback (and its compilation-wide HasKey/HasQueryFilter scan) above
    // that size, so it went silent on `x.Id == id` in most real projects.
    private const int LargeCompilationFillerFileCount = 70;

    private const string LargeCompilationEntities = @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } public int ExternalId { get; set; } public bool IsDeleted { get; set; } }
}
";

    private const string LargeCompilationConventionQuery = @"using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Test
{
    public class UserQueries
    {
        public User ById(DbSet<User> users, int id)
        {
            return {|LC023:users.FirstOrDefault(x => x.Id == id)|};
        }
    }
}
";

    private const string LargeCompilationQuietConventionQuery = @"using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Test
{
    public class UserQueries
    {
        public User ById(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}
";

    [Fact]
    public async Task LargeCompilation_ConventionId_ShouldTrigger()
    {
        await VerifyLargeCompilationAsync(LargeCompilationConventionQuery);
    }

    [Fact]
    public async Task LargeCompilation_ConventionTypeNameId_ShouldTrigger()
    {
        await VerifyLargeCompilationAsync(@"using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Test
{
    public class Order { public int OrderId { get; set; } }

    public class OrderQueries
    {
        public Order ById(DbSet<Order> orders, int orderId)
        {
            return {|LC023:orders.FirstOrDefault(x => x.OrderId == orderId)|};
        }
    }
}
");
    }

    [Fact]
    public async Task LargeCompilation_HasKeyInAnotherFile_ShouldNotTriggerForConventionId()
    {
        await VerifyLargeCompilationAsync(
            LargeCompilationQuietConventionQuery,
            @"using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(x => x.ExternalId);
        }
    }
}
");
    }

    [Fact]
    public async Task LargeCompilation_HasKeyInConfigurationClass_ShouldTriggerForConfiguredKey()
    {
        await VerifyLargeCompilationAsync(
            @"using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Test
{
    public class UserQueries
    {
        public User ById(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }

        public User ByExternalId(DbSet<User> users, int externalId)
        {
            return {|LC023:users.FirstOrDefault(x => x.ExternalId == externalId)|};
        }
    }
}
",
            @"using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Test
{
    public class UserConfiguration
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.HasKey(x => x.ExternalId);
        }
    }
}
");
    }

    [Fact]
    public async Task LargeCompilation_HasQueryFilterInAnotherFile_ShouldNotTrigger()
    {
        await VerifyLargeCompilationAsync(
            LargeCompilationQuietConventionQuery,
            @"using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
");
    }

    [Fact]
    public async Task LargeCompilation_HasQueryFilterOnBaseTypeInAnotherFile_ShouldNotTrigger()
    {
        await VerifyLargeCompilationAsync(
            @"using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Test
{
    public class TenantEntity { public int Id { get; set; } public int TenantId { get; set; } }
    public class Invoice : TenantEntity { }

    public class InvoiceQueries
    {
        public Invoice ById(DbSet<Invoice> invoices, int id)
        {
            return invoices.FirstOrDefault(x => x.Id == id);
        }
    }
}
",
            @"using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TenantEntity>().HasQueryFilter(x => x.TenantId == 1);
        }
    }
}
");
    }

    [Fact]
    public async Task LargeCompilation_HasNoKeyInAnotherFile_ShouldNotTrigger()
    {
        await VerifyLargeCompilationAsync(
            LargeCompilationQuietConventionQuery,
            @"using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasNoKey();
        }
    }
}
");
    }

    [Fact]
    public async Task LargeCompilation_ConventionId_FixerRewritesToFind()
    {
        const string query = @"using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Test
{
    public class UserQueries
    {
        public User ById(DbSet<User> users, int id)
        {
            return {|LC023:users.FirstOrDefault(x => x.Id == id)|};
        }
    }
}
";
        const string fixedQuery = @"using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace LinqContraband.Test
{
    public class UserQueries
    {
        public User ById(DbSet<User> users, int id)
        {
            return users.Find(id);
        }
    }
}
";

        var test = new CodeFixTest { CodeFixEquivalenceKey = "UseFind" };
        AddLargeCompilationSources(test.TestState.Sources, query);
        AddLargeCompilationSources(test.FixedState.Sources, fixedQuery);
        await test.RunAsync();
    }

    [Fact]
    public async Task FirstOrDefault_WithId_WhenEntityHasNoKey_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class UserView { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserView>().HasNoKey();
        }
    }

    public class TestClass
    {
        public UserView TestMethod(DbSet<UserView> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithId_WhenEntityIsKeylessAttribute_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    [Keyless]
    public class UserView { public int Id { get; set; } }

    public class TestClass
    {
        public UserView TestMethod(DbSet<UserView> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithPrimaryKeyAttribute_ShouldUseAttributeKeyOverConvention()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    [PrimaryKey(nameof(ExternalId))]
    public class User { public int Id { get; set; } public int ExternalId { get; set; } }

    public class TestClass
    {
        public User ById(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }

        public User ByExternalId(DbSet<User> users, int externalId)
        {
            return {|LC023:users.FirstOrDefault(x => x.ExternalId == externalId)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithPrimaryKeyAttribute_FixerRewritesToFind()
    {
        const string template = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    [PrimaryKey(nameof(ExternalId))]
    public class User { public int Id { get; set; } public int ExternalId { get; set; } }

    public class TestClass
    {
        public User ByExternalId(DbSet<User> users, int externalId)
        {
            return LOOKUP;
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(
            template.Replace("LOOKUP", "{|LC023:users.FirstOrDefault(x => x.ExternalId == externalId)|}"),
            template.Replace("LOOKUP", "users.Find(externalId)"));
    }

    [Fact]
    public async Task FirstOrDefault_WithCompositePrimaryKeyAttribute_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    [PrimaryKey(nameof(TenantId), nameof(Id))]
    public class User { public int TenantId { get; set; } public int Id { get; set; } }

    public class TestClass
    {
        public User TestMethod(DbSet<User> users, int id)
        {
            return users.FirstOrDefault(x => x.Id == id);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Theory]
    [InlineData("HasKey")]
    [InlineData("HasNoKey")]
    [InlineData("HasQueryFilter")]
    public void ModelConfigurationPrefilter_FindsNeedleAcrossChunkBoundaries(string needle)
    {
        // The prefilter reads the source in fixed-size chunks; slide the needle across every
        // offset around the first chunk boundary so a split match cannot be missed.
        for (var start = 4070; start < 4110; start++)
        {
            var text = SourceText.From(new string(' ', start) + "builder." + needle + "(x => x.Id);" + new string(' ', 5000));
            Assert.True(FindInsteadOfFirstOrDefaultKeyAnalysis.MayContainModelConfiguration(text), $"Missed {needle} at offset {start}.");
        }
    }

    [Fact]
    public void ModelConfigurationPrefilter_SkipsSourceWithoutModelConfiguration()
    {
        var text = SourceText.From(new string(' ', 9000) + "var user = users.FirstOrDefault(x => x.Id == id); Has(); Key(); HasKe;");

        Assert.False(FindInsteadOfFirstOrDefaultKeyAnalysis.MayContainModelConfiguration(text));
    }

    private static async Task VerifyLargeCompilationAsync(params string[] sources)
    {
        var test = new AnalyzerTest();
        AddLargeCompilationSources(test.TestState.Sources, sources);
        await test.RunAsync();
    }

    private static void AddLargeCompilationSources(SourceFileList target, params string[] sources)
    {
        target.Add(("/0/EfCore.cs", EFCoreMock));
        target.Add(("/0/Entities.cs", LargeCompilationEntities));

        // Fillers go before the model configuration so the configuration is never in the
        // first 64 trees and is never the tree being analyzed.
        for (var i = 0; i < LargeCompilationFillerFileCount; i++)
        {
            target.Add(($"/0/Filler{i}.cs", $@"namespace LinqContraband.Filler
{{
    public class Filler{i} {{ public int Value {{ get; set; }} }}
}}
"));
        }

        for (var i = 0; i < sources.Length; i++)
            target.Add(($"/0/Source{i}.cs", sources[i]));
    }
}
