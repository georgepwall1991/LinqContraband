using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

// BTCPay Server configures each entity in a static `OnModelCreating(ModelBuilder)` on the entity
// class and calls those from the context's OnModelCreating; a scan reported 14 of its entities.
public partial class EntityMissingPrimaryKeyEdgeCasesTests
{
    [Fact]
    public async Task TestInnocent_KeyConfiguredInStaticEntityHelper_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<AddressInvoiceData> AddressInvoices { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            AddressInvoiceData.OnModelCreating(builder);
        }
    }

    public class AddressInvoiceData
    {
        public string Address { get; set; }
        public string InvoiceDataId { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<AddressInvoiceData>()
                .HasKey(o => o.Address);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_KeyConfiguredInModelBuilderExtension_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<UserStore> UserStores { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMemberships();
        }
    }

    public class UserStore
    {
        public string UserId { get; set; }
        public string StoreId { get; set; }
    }

    public static class ModelBuilderExtensions
    {
        public static void ConfigureMemberships(this ModelBuilder modelBuilder)
        {
            ConfigureUserStores(modelBuilder);
        }

        private static void ConfigureUserStores(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserStore>().HasKey(""UserId"", ""StoreId"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityBuilderPassedToHelper_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<RefundData> Refunds { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureRefunds(modelBuilder.Entity<RefundData>());
        }

        private static void ConfigureRefunds(EntityTypeBuilder<RefundData> refunds)
        {
            refunds.HasKey(r => r.RefundCode);
        }
    }

    public class RefundData
    {
        public string RefundCode { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_HasNoKeyInStaticEntityHelper_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<StoreReport> Reports { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            StoreReport.OnModelCreating(builder);
        }
    }

    public class StoreReport
    {
        public string Name { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<StoreReport>().HasNoKey();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_HelperNeverCalledFromOnModelCreating_ShouldTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<OrphanData> {|LC011:Orphans|} { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
        }
    }

    public class OrphanData
    {
        public string Code { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<OrphanData>().HasKey(o => o.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_HelperConfiguresOtherEntity_ShouldTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<LabelData> Labels { get; set; }
        public DbSet<LabelLinkData> {|LC011:LabelLinks|} { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            LabelData.OnModelCreating(builder);
        }
    }

    public class LabelData
    {
        public string Name { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<LabelData>().HasKey(l => l.Name);
        }
    }

    public class LabelLinkData
    {
        public string LabelName { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_RecursiveHelpers_Terminate()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<CycleData> Cycles { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            Configure(builder, 0);
        }

        private static void Configure(ModelBuilder builder, int depth)
        {
            if (depth > 2)
                return;

            Configure(builder, depth + 1);
            builder.Entity<CycleData>().HasKey(c => c.Code);
        }
    }

    public class CycleData
    {
        public string Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_KeyConfiguredInEntityBuilderLambda_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<StoreLabelLinkData> StoreLabelLinks { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<StoreLabelLinkData>(b =>
            {
                b.ToTable(""store_label_links"");
                b.HasKey(x => new { x.StoreId, x.ObjectId });
            });
        }
    }

    public class StoreLabelLinkData
    {
        public string StoreId { get; set; }
        public string ObjectId { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_KeyConfiguredInEntityBuilderLambdaInsideHelper_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<WalletObjectLinkData> WalletObjectLinks { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            WalletObjectLinkData.OnModelCreating(builder);
        }
    }

    public class WalletObjectLinkData
    {
        public string WalletId { get; set; }
        public string ParentId { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<WalletObjectLinkData>(o => o.HasKey(x => new { x.WalletId, x.ParentId }));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_EntityBuilderLambdaWithoutKey_ShouldTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<LooseData> {|LC011:Loose|} { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<LooseData>(b => b.ToTable(""loose""));
        }
    }

    public class LooseData
    {
        public string Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    // Kavita and Bitwarden keep entities in a separate models project; the entity type in
    // `builder.Entity<ChapterPeople>().HasKey(...)` then lives in a referenced assembly.
    [Theory]
    [InlineData("builder.Entity<ChapterPeople>().HasKey(cp => new { cp.ChapterId, cp.PersonId });", false)]
    [InlineData("var chapterPeople = builder.Entity<ChapterPeople>(); chapterPeople.HasNoKey();", false)]
    [InlineData("builder.Entity<ChapterPeople>(b => b.HasKey(cp => cp.ChapterId));", false)]
    [InlineData("builder.Entity<ChapterPeople>().ToTable(\"chapter_people\");", true)]
    public async Task EntityFromReferencedProject_UsesFluentConfiguration(string configuration, bool reports)
    {
        var dbSet = reports ? "{|LC011:ChapterPeople|}" : "ChapterPeople";
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = @"
using Microsoft.EntityFrameworkCore;
using Models;

namespace Data
{
    public class DataContext : DbContext
    {
        public DbSet<ChapterPeople> " + dbSet + @" { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            " + configuration + @"
        }
    }
}"
        };

        var modelsProject = new ProjectState("Models", Microsoft.CodeAnalysis.LanguageNames.CSharp, "/models/", "cs");
        modelsProject.Sources.Add(("/models/Ef.cs", ReferencedEfCoreMock));
        modelsProject.Sources.Add(("/models/ChapterPeople.cs", @"
namespace Models
{
    public class ChapterPeople
    {
        public int ChapterId { get; set; }
        public int PersonId { get; set; }
    }
}"));
        test.TestState.AdditionalProjects.Add("Models", modelsProject);
        test.TestState.AdditionalProjectReferences.Add("Models");

        await test.RunAsync();
    }

    private const string ReferencedEfCoreMock = @"
using System;
using System.Linq.Expressions;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) {}
    }

    public class DbSet<T> where T : class {}

    public class ModelBuilder
    {
        public EntityTypeBuilder<T> Entity<T>() where T : class => new EntityTypeBuilder<T>();
        public ModelBuilder Entity<T>(Action<EntityTypeBuilder<T>> buildAction) where T : class => this;
    }

    public class EntityTypeBuilder<T> where T : class
    {
        public EntityTypeBuilder<T> HasKey(Expression<Func<T, object>> keyExpression) => this;
        public EntityTypeBuilder<T> HasNoKey() => this;
        public EntityTypeBuilder<T> ToTable(string name) => this;
    }
}";
}
