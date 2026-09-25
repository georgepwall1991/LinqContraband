using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

// Shapes from BaGet (keys configured in methods passed as method groups to Entity<T>) and
// Duende IdentityServer (keys configured in a ModelBuilder extension called from a generic context).
public class EntityMissingPrimaryKeyRealAppConfigurationTests
{
    private const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    using System;
    using System.Linq.Expressions;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class DbContext : IDisposable
    {
        public void Dispose() {}
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) {}
    }
    public class DbSet<T> where T : class {}

    public class ModelBuilder
    {
        public EntityTypeBuilder<T> Entity<T>() where T : class => new EntityTypeBuilder<T>();
        public ModelBuilder Entity<T>(Action<EntityTypeBuilder<T>> buildAction) where T : class => this;
        public ModelBuilder HasDefaultSchema(string schema) => this;
    }
}

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    using System;
    using System.Linq.Expressions;

    public class EntityTypeBuilder<T> where T : class
    {
        public EntityTypeBuilder<T> HasKey(params string[] propertyNames) => this;
        public EntityTypeBuilder<T> HasKey(Expression<Func<T, object>> keyExpression) => this;
        public EntityTypeBuilder<T> ToTable(string name) => this;
        public PropertyBuilder Property<TProperty>(Expression<Func<T, TProperty>> propertyExpression) => new PropertyBuilder();
        public IndexBuilder HasIndex(Expression<Func<T, object>> indexExpression) => new IndexBuilder();
    }

    public class PropertyBuilder
    {
        public PropertyBuilder HasMaxLength(int maxLength) => this;
        public PropertyBuilder IsRequired() => this;
    }

    public class IndexBuilder
    {
        public IndexBuilder IsUnique() => this;
    }
}
";

    private const string BaGetContext = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BaGet.Core
{
    public interface IContext {}

    public abstract class AbstractContext<TContext> : DbContext, IContext where TContext : DbContext
    {
        public DbSet<Package> Packages { get; set; }
        public DbSet<PackageType> PackageTypes { get; set; }
        public DbSet<TargetFramework> TargetFrameworks { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Package>(BuildPackageEntity);
            builder.Entity<PackageType>(BuildPackageTypeEntity);
            builder.Entity<TargetFramework>(this.BuildTargetFrameworkEntity);
        }

        private void BuildPackageEntity(EntityTypeBuilder<Package> package)
        {
            package.HasKey(p => p.Key);
        }

        private void BuildPackageTypeEntity(EntityTypeBuilder<PackageType> type)
        {
            type.HasKey(d => d.Key);
            type.Property(d => d.Name).HasMaxLength(512);
        }

        private void BuildTargetFrameworkEntity(EntityTypeBuilder<TargetFramework> targetFramework)
        {
            targetFramework.HasKey(f => f.Key);
        }
    }

    public class SqliteContext : AbstractContext<SqliteContext> {}

    public class Package
    {
        public int Key { get; set; }
        public string Id { get; set; }
    }

    public class PackageType
    {
        public int Key { get; set; }
        public string Name { get; set; }
    }

    public class TargetFramework
    {
        public int Key { get; set; }
        public string Moniker { get; set; }
    }
}
" + EfMock;

    // Package has a string Id (the NuGet package id), which also satisfies the convention check, so
    // BaGet only saw PackageType and TargetFramework reported.
    [Fact]
    public async Task BaGet_KeysConfiguredInMethodGroupBuilders_ShouldNotTrigger()
    {
        await VerifyCS.VerifyAnalyzerAsync(BaGetContext);
    }

    [Fact]
    public async Task MethodGroupBuilderWithoutKey_ShouldTrigger()
    {
        var test = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App
{
    public class AppContext : DbContext
    {
        public DbSet<Tag> {|LC011:Tags|} { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Tag>(BuildTag);
        }

        private static void BuildTag(EntityTypeBuilder<Tag> tag)
        {
            tag.ToTable(""tags"");
        }
    }

    public class Tag
    {
        public string Name { get; set; }
    }
}
" + EfMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MethodGroupBuilderForOtherEntity_ShouldTrigger()
    {
        var test = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App
{
    public class AppContext : DbContext
    {
        public DbSet<Tag> Tags { get; set; }
        public DbSet<Label> {|LC011:Labels|} { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Tag>(BuildTag);
        }

        private static void BuildTag(EntityTypeBuilder<Tag> tag)
        {
            tag.HasKey(t => t.Name);
        }

        private static void BuildLabel(EntityTypeBuilder<Label> label)
        {
            label.HasKey(l => l.Text);
        }
    }

    public class Tag
    {
        public string Name { get; set; }
    }

    public class Label
    {
        public string Text { get; set; }
    }
}
" + EfMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticMethodGroupBuilderOnOtherClass_ShouldNotTrigger()
    {
        var test = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App
{
    public class AppContext : DbContext
    {
        public DbSet<Tag> Tags { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Tag>(TagConfiguration.Build);
        }
    }

    public static class TagConfiguration
    {
        public static void Build(EntityTypeBuilder<Tag> tag)
        {
            tag.HasKey(t => t.Name);
        }
    }

    public class Tag
    {
        public string Name { get; set; }
    }
}
" + EfMock;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    // Duende IdentityServer declares its ModelBuilder extensions in C# 14 extension blocks, where the
    // receiver belongs to the block rather than to the method.
    private const string DuendeExtensionBlocks = @"
    public static class ModelBuilderExtensions
    {
        extension<TEntity>(EntityTypeBuilder<TEntity> entityTypeBuilder)
            where TEntity : class
        {
            private EntityTypeBuilder<TEntity> ToTable(TableConfiguration configuration) =>
                entityTypeBuilder.ToTable(configuration.Name);
        }

        extension(ModelBuilder modelBuilder)
        {
            public void ConfigurePersistedGrantContext(OperationalStoreOptions storeOptions)
            {
                if (!string.IsNullOrWhiteSpace(storeOptions.DefaultSchema))
                {
                    modelBuilder.HasDefaultSchema(storeOptions.DefaultSchema);
                }

                modelBuilder.Entity<PersistedGrant>(grant =>
                {
                    grant.ToTable(storeOptions.PersistedGrants);
                    grant.Property(x => x.Key).HasMaxLength(200);
                    grant.HasKey(x => x.Id);
                });

                modelBuilder.Entity<DeviceFlowCodes>(codes =>
                {
                    codes.ToTable(storeOptions.DeviceFlowCodes);
                    codes.Property(x => x.DeviceCode).HasMaxLength(200).IsRequired();
                    codes.Property(x => x.UserCode).HasMaxLength(200).IsRequired();
                    codes.HasKey(x => new { x.UserCode });
                    codes.HasIndex(x => x.DeviceCode).IsUnique();
                    codes.HasIndex(x => x.Expiration);
                });
            }
        }
    }";

    // The same helper written as a classic `this ModelBuilder` extension method.
    private const string DuendeClassicExtensions = @"
    public static class ModelBuilderExtensions
    {
        public static void ConfigurePersistedGrantContext(this ModelBuilder modelBuilder, OperationalStoreOptions storeOptions)
        {
            modelBuilder.Entity<PersistedGrant>(grant => grant.HasKey(x => x.Id));

            modelBuilder.Entity<DeviceFlowCodes>(codes =>
            {
                codes.ToTable(storeOptions.DeviceFlowCodes);
                codes.HasKey(x => new { x.UserCode });
            });
        }

        private static EntityTypeBuilder<TEntity> ToTable<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder, TableConfiguration configuration)
            where TEntity : class
        {
            return entityTypeBuilder.ToTable(configuration.Name);
        }
    }";

    private static string DuendeSource(string extensions, string deviceFlowCodesSet = "DeviceFlowCodes") => @"
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Extensions;
using Duende.IdentityServer.EntityFramework.Options;

namespace Duende.IdentityServer.EntityFramework.Options
{
    public class OperationalStoreOptions
    {
        public string DefaultSchema { get; set; }
        public TableConfiguration PersistedGrants { get; set; }
        public TableConfiguration DeviceFlowCodes { get; set; }
    }

    public class TableConfiguration
    {
        public string Name { get; set; }
    }
}

namespace Duende.IdentityServer.EntityFramework.Entities
{
    public class PersistedGrant
    {
        public long Id { get; set; }
        public string Key { get; set; }
    }

    public class DeviceFlowCodes
    {
        public string DeviceCode { get; set; }
        public string UserCode { get; set; }
        public DateTime? Expiration { get; set; }
    }
}

namespace Duende.IdentityServer.EntityFramework.Interfaces
{
    public interface IPersistedGrantDbContext : IDisposable {}
}

namespace Duende.IdentityServer.EntityFramework.Extensions
{
" + extensions + @"
}

namespace Duende.IdentityServer.EntityFramework.DbContexts
{
    using Duende.IdentityServer.EntityFramework.Interfaces;

    public class PersistedGrantDbContext : PersistedGrantDbContext<PersistedGrantDbContext> {}

    public class PersistedGrantDbContext<TContext> : DbContext, IPersistedGrantDbContext
        where TContext : DbContext, IPersistedGrantDbContext
    {
        public OperationalStoreOptions StoreOptions { get; set; }

        public DbSet<PersistedGrant> PersistedGrants { get; set; }
        public DbSet<DeviceFlowCodes> " + deviceFlowCodesSet + @" { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (StoreOptions is null)
            {
                throw new ArgumentNullException(nameof(StoreOptions));
            }

            modelBuilder.ConfigurePersistedGrantContext(StoreOptions);

            base.OnModelCreating(modelBuilder);
        }
    }
}
" + EfMock;

    private static Task VerifyPreviewAsync(string source)
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = source
        };
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var parseOptions = (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
            return solution.WithProjectParseOptions(
                projectId,
                parseOptions.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview));
        });

        return test.RunAsync();
    }

    [Fact]
    public async Task Duende_KeyConfiguredInExtensionBlockHelper_ShouldNotTrigger()
    {
        await VerifyPreviewAsync(DuendeSource(DuendeExtensionBlocks));
    }

    [Fact]
    public async Task Duende_KeyConfiguredInClassicExtensionHelper_ShouldNotTrigger()
    {
        await VerifyCS.VerifyAnalyzerAsync(DuendeSource(DuendeClassicExtensions));
    }

    [Fact]
    public async Task ExtensionBlockHelperWithoutKey_ShouldTrigger()
    {
        var extensions = DuendeExtensionBlocks.Replace("codes.HasKey(x => new { x.UserCode });", "");
        await VerifyPreviewAsync(DuendeSource(extensions, "{|LC011:DeviceFlowCodes|}"));
    }

    [Fact]
    public async Task ExtensionBlockHelperNeverCalled_ShouldTrigger()
    {
        var source = DuendeSource(DuendeExtensionBlocks, "{|LC011:DeviceFlowCodes|}")
            .Replace("modelBuilder.ConfigurePersistedGrantContext(StoreOptions);", "");
        await VerifyPreviewAsync(source);
    }

    [Fact]
    public async Task EntityBuilderExtensionBlockHelper_ShouldNotTrigger()
    {
        var test = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App
{
    public class AppContext : DbContext
    {
        public DbSet<Tag> Tags { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Tag>().ConfigureTag();
        }
    }

    public static class TagBuilderExtensions
    {
        extension(EntityTypeBuilder<Tag> tag)
        {
            public void ConfigureTag()
            {
                tag.HasKey(t => t.Name);
            }
        }
    }

    public class Tag
    {
        public string Name { get; set; }
    }
}
" + EfMock;

        await VerifyPreviewAsync(test);
    }
}
