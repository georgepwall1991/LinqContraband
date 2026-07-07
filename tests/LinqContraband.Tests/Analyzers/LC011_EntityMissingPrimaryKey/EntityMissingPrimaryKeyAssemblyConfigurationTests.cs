using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public partial class EntityMissingPrimaryKeyEdgeCasesTests
{
    [Fact]
    public async Task TestCrime_ExternalApplyConfigurationsFromAssembly_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ExternalAssemblyConfigEntity> {|LC011:ExternalAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(string).Assembly);
        }
    }

    public class ExternalAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ExternalAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ExternalAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ExternalAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ApplyConfigurationsFromExecutingAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ExecutingAssemblyConfigEntity> ExecutingAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(System.Reflection.Assembly.GetExecutingAssembly());
        }
    }

    public class ExecutingAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ExecutingAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ExecutingAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ExecutingAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ApplyConfigurationsFromImportedExecutingAssembly_ShouldNotTrigger()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<ImportedAssemblyConfigEntity> ImportedAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class ImportedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ImportedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ImportedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ImportedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_AliasedSystemReflectionAssembly_ShouldNotTrigger()
    {
        var test = Usings.Replace("using TestNamespace;", "using Assembly = System.Reflection.Assembly;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<SystemReflectionAliasAssemblyConfigEntity> SystemReflectionAliasAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class SystemReflectionAliasAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class SystemReflectionAliasAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<SystemReflectionAliasAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<SystemReflectionAliasAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_GlobalQualifiedExecutingAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<GlobalQualifiedAssemblyConfigEntity> GlobalQualifiedAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(global::System.Reflection.Assembly.GetExecutingAssembly());
        }
    }

    public class GlobalQualifiedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class GlobalQualifiedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<GlobalQualifiedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<GlobalQualifiedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ShadowedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<ShadowedAssemblyTypeConfigEntity> {|LC011:ShadowedAssemblyTypeConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class Assembly
    {
        public static System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class ShadowedAssemblyTypeConfigEntity
    {
        public int Code { get; set; }
    }

    public class ShadowedAssemblyTypeConfigEntityConfiguration : IEntityTypeConfiguration<ShadowedAssemblyTypeConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ShadowedAssemblyTypeConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<LocalAssemblyValueConfigEntity> {|LC011:LocalAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var Assembly = new ExternalAssemblyProvider();
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class ExternalAssemblyProvider
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class LocalAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class LocalAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<LocalAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<LocalAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_MemberAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        private readonly ExternalAssemblyProvider Assembly = new ExternalAssemblyProvider();

        public DbSet<MemberAssemblyValueConfigEntity> {|LC011:MemberAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class ExternalAssemblyProvider
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class MemberAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class MemberAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<MemberAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<MemberAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ForeachAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<ForeachAssemblyValueConfigEntity> {|LC011:ForeachAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var Assembly in new[] { new ExternalAssemblyProvider() })
            {
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            }
        }
    }

    public class ExternalAssemblyProvider
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class ForeachAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class ForeachAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<ForeachAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ForeachAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_CatchAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<CatchAssemblyValueConfigEntity> {|LC011:CatchAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            try
            {
            }
            catch (ExternalAssemblyException Assembly)
            {
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            }
        }
    }

    public class ExternalAssemblyException : Exception
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class CatchAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class CatchAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<CatchAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<CatchAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_PatternAssemblyValueShadowsImportedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<PatternAssemblyValueConfigEntity> {|LC011:PatternAssemblyValueConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            object provider = new ExternalAssemblyProvider();
            if (provider is ExternalAssemblyProvider Assembly)
            {
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            }
        }
    }

    public class ExternalAssemblyProvider
    {
        public System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    public class PatternAssemblyValueConfigEntity
    {
        public int Code { get; set; }
    }

    public class PatternAssemblyValueConfigEntityConfiguration : IEntityTypeConfiguration<PatternAssemblyValueConfigEntity>
    {
        public void Configure(EntityTypeBuilder<PatternAssemblyValueConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ParentNamespaceAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace.Inner;") + SemanticMockAttributes.Replace(
            "namespace TestNamespace\n{\n    public class MyDbContext",
            @"namespace TestNamespace
{
    public class Assembly
    {
        public static System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }

    namespace Inner
    {
        public class MyDbContext") + @"
        public DbSet<ParentNamespaceAssemblyConfigEntity> {|LC011:ParentNamespaceAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class ParentNamespaceAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ParentNamespaceAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ParentNamespaceAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ParentNamespaceAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_AliasedAssemblyType_ShouldNotApplyLocalConfig()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing Assembly = ExternalAssemblyProvider.Assembly;\nusing TestNamespace;") + SemanticMockAttributes + @"
        public DbSet<AliasedAssemblyTypeConfigEntity> {|LC011:AliasedAssemblyTypeConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class AliasedAssemblyTypeConfigEntity
    {
        public int Code { get; set; }
    }

    public class AliasedAssemblyTypeConfigEntityConfiguration : IEntityTypeConfiguration<AliasedAssemblyTypeConfigEntity>
    {
        public void Configure(EntityTypeBuilder<AliasedAssemblyTypeConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}

namespace ExternalAssemblyProvider
{
    public static class Assembly
    {
        public static System.Reflection.Assembly GetExecutingAssembly() => typeof(string).Assembly;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_GlobalUsingExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = Usings + SemanticMockAttributes + @"
        public DbSet<GlobalUsingAssemblyConfigEntity> GlobalUsingAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class GlobalUsingAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class GlobalUsingAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<GlobalUsingAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<GlobalUsingAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}"
        };
        test.TestState.Sources.Add(("GlobalUsings.cs", "global using System.Reflection;"));

        await test.RunAsync();
    }

    [Fact]
    public async Task TestInnocent_NamespaceUsingExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes.Replace(
            "namespace TestNamespace\n{\n    public class MyDbContext",
            "namespace TestNamespace\n{\n    using System.Reflection;\n\n    public class MyDbContext") + @"
        public DbSet<NamespaceUsingAssemblyConfigEntity> NamespaceUsingAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }

    public class NamespaceUsingAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class NamespaceUsingAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<NamespaceUsingAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<NamespaceUsingAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LocalExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<LocalAssemblyConfigEntity> LocalAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class LocalAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class LocalAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<LocalAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<LocalAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ReassignedLocalAssembly_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ReassignedAssemblyConfigEntity> {|LC011:ReassignedAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            assembly = typeof(string).Assembly;
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class ReassignedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ReassignedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ReassignedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ReassignedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ConditionallyReassignedLocalAssembly_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<ConditionallyReassignedAssemblyConfigEntity> {|LC011:ConditionallyReassignedAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            if (Environment.TickCount > 0)
            {
                assembly = typeof(string).Assembly;
            }

            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class ConditionallyReassignedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ConditionallyReassignedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ConditionallyReassignedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ConditionallyReassignedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_UninvokedLocalFunctionAssignment_ShouldNotInvalidateCurrentAssembly()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<UninvokedLocalFunctionAssemblyConfigEntity> UninvokedLocalFunctionAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            void Later()
            {
                assembly = typeof(string).Assembly;
            }

            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class UninvokedLocalFunctionAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class UninvokedLocalFunctionAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<UninvokedLocalFunctionAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<UninvokedLocalFunctionAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalAssemblyShadowsCurrentAssemblyMember_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        private static readonly System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();

        public DbSet<ShadowedAssemblyConfigEntity> {|LC011:ShadowedAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var ConfigAssembly = typeof(string).Assembly;
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class ShadowedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ShadowedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ShadowedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ShadowedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_MutableMemberAssemblyField_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        private static System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();

        public DbSet<MutableFieldAssemblyConfigEntity> {|LC011:MutableFieldAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class MutableFieldAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class MutableFieldAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<MutableFieldAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<MutableFieldAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_MutableMemberAssemblyProperty_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes + @"
        private static System.Reflection.Assembly ConfigAssembly { get; set; } = System.Reflection.Assembly.GetExecutingAssembly();

        public DbSet<MutablePropertyAssemblyConfigEntity> {|LC011:MutablePropertyAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class MutablePropertyAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class MutablePropertyAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<MutablePropertyAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<MutablePropertyAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_InheritedReadonlyAssemblyMember_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes.Replace(
            "public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext",
            @"public class BaseDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        protected static readonly System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();
    }

    public class MyDbContext : BaseDbContext") + @"
        public DbSet<InheritedMemberAssemblyConfigEntity> InheritedMemberAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class InheritedMemberAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class InheritedMemberAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<InheritedMemberAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<InheritedMemberAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DerivedMutableAssemblyMemberShadowsInheritedReadonlyMember_ShouldNotApplyLocalConfig()
    {
        var test = Usings + SemanticMockAttributes.Replace(
            "public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext",
            @"public class BaseDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        protected static readonly System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();
    }

    public class MyDbContext : BaseDbContext") + @"
        private static new System.Reflection.Assembly ConfigAssembly = typeof(string).Assembly;

        public DbSet<ShadowedInheritedAssemblyConfigEntity> {|LC011:ShadowedInheritedAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class ShadowedInheritedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ShadowedInheritedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ShadowedInheritedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ShadowedInheritedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ExpressionBodiedMemberAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = Usings.Replace("using TestNamespace;", "using System.Reflection;\nusing TestNamespace;") + SemanticMockAttributes + @"
        private static Assembly ConfigAssembly => Assembly.GetExecutingAssembly();

        public DbSet<ExpressionBodiedAssemblyConfigEntity> ExpressionBodiedAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class ExpressionBodiedAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class ExpressionBodiedAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<ExpressionBodiedAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<ExpressionBodiedAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SelfReferentialAssemblyLocal_ShouldNotCrashAnalyzer()
    {
        var test = Usings + SemanticMockAttributes + @"
        public DbSet<SelfReferentialAssemblyConfigEntity> {|LC011:SelfReferentialAssemblyConfigs|} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var assembly = {|CS0841:assembly|};
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    public class SelfReferentialAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class SelfReferentialAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<SelfReferentialAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<SelfReferentialAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_MemberExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger()
    {
        var test = Usings + SemanticMockAttributes + @"
        private static readonly System.Reflection.Assembly ConfigAssembly = System.Reflection.Assembly.GetExecutingAssembly();

        public DbSet<MemberAssemblyConfigEntity> MemberAssemblyConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(ConfigAssembly);
        }
    }

    public class MemberAssemblyConfigEntity
    {
        public int Code { get; set; }
    }

    public class MemberAssemblyConfigEntityConfiguration : IEntityTypeConfiguration<MemberAssemblyConfigEntity>
    {
        public void Configure(EntityTypeBuilder<MemberAssemblyConfigEntity> builder)
        {
            builder.HasKey(e => e.Code);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
