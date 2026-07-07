using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public partial class EntityMissingPrimaryKeyEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_UnappliedEntityTypeConfiguration_ShouldTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ConfiguredElsewhereEntity> {|LC011:ConfiguredElsewhere|} { get; set; }
    }

    public class ConfiguredElsewhereEntity
    {
        public int Code { get; set; }
    }

    public class ConfiguredElsewhereEntityConfiguration : IEntityTypeConfiguration<ConfiguredElsewhereEntity>
    {
        public void Configure(EntityTypeBuilder<ConfiguredElsewhereEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_AppliedEntityTypeConfiguration_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<AppliedConfigEntity> AppliedConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new AppliedConfigEntityConfiguration());
        }
    }

    public class AppliedConfigEntity
    {
        public int Code { get; set; }
    }

    public class AppliedConfigEntityConfiguration : IEntityTypeConfiguration<AppliedConfigEntity>
    {
        public void Configure(EntityTypeBuilder<AppliedConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_BuilderVariableHasKey_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<VariableConfiguredEntity> VariableConfiguredEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<VariableConfiguredEntity>();
            entity.HasKey(e => e.Code);
        }
    }

    public class VariableConfiguredEntity
    {
        public int Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ChainedBuilderVariableHasKey_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ChainedConfiguredEntity> ChainedConfiguredEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<ChainedConfiguredEntity>();
            entity.ToTable(""ChainedConfiguredEntities"").HasKey(e => e.Code);
        }
    }

    public class ChainedConfiguredEntity
    {
        public int Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SelfReferentialBuilderLocal_ShouldNotCrashAnalyzer()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<SelfReferentialBuilderEntity> {|LC011:SelfReferentialBuilderEntities|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = {|CS0841:entity|}.HasKey(""Id"");
        }
    }

    public class SelfReferentialBuilderEntity
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_AppliedConfigurationChainedBuilderHasKey_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ChainedConfigEntity> ChainedConfigEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ChainedConfigEntityConfiguration());
        }
    }

    public class ChainedConfigEntity
    {
        public int Code { get; set; }
    }

    public class ChainedConfigEntityConfiguration : IEntityTypeConfiguration<ChainedConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ChainedConfigEntity> builder)
        {
            builder.ToTable(""ChainedConfigEntities"").HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LocalVariableAppliedConfiguration_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<VariableAppliedConfigEntity> VariableAppliedConfigEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var config = new VariableAppliedConfigEntityConfiguration();
            modelBuilder.ApplyConfiguration(config);
        }
    }

    public class VariableAppliedConfigEntity
    {
        public int Code { get; set; }
    }

    public class VariableAppliedConfigEntityConfiguration : IEntityTypeConfiguration<VariableAppliedConfigEntity>
    {
        public void Configure(EntityTypeBuilder<VariableAppliedConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ScopedBuilderVariableReuse_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ScopedOrder> ScopedOrders { get; set; }
        public DbSet<ScopedCustomer> ScopedCustomers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            {
                var entity = modelBuilder.Entity<ScopedOrder>();
                entity.HasKey(e => e.Code);
            }

            {
                var entity = modelBuilder.Entity<ScopedCustomer>();
                entity.HasKey(e => e.Code);
            }
        }
    }

    public class ScopedOrder
    {
        public int Code { get; set; }
    }

    public class ScopedCustomer
    {
        public int Code { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
