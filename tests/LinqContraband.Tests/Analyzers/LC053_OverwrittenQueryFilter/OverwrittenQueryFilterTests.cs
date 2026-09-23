using LinqContraband.Analyzers.LC053_OverwrittenQueryFilter;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC053_OverwrittenQueryFilter.OverwrittenQueryFilterAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC053_OverwrittenQueryFilter;

public class OverwrittenQueryFilterTests
{
    internal const string EfCoreMock = @"
using System;
using System.Linq.Expressions;

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    public class EntityTypeBuilder<TEntity> where TEntity : class
    {
        public virtual EntityTypeBuilder<TEntity> HasQueryFilter(Expression<Func<TEntity, bool>> filter) => this;
        public virtual EntityTypeBuilder<TEntity> HasQueryFilter(string filterKey, Expression<Func<TEntity, bool>> filter) => this;
        public virtual EntityTypeBuilder<TEntity> HasKey(Expression<Func<TEntity, object>> keyExpression) => this;
    }

    public interface IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        void Configure(EntityTypeBuilder<TEntity> builder);
    }
}

namespace Microsoft.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class ModelBuilder
    {
        public virtual EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => null;
    }
}

namespace TestApp
{
    public class Blog
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class Post
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }
}
";

    internal static string Wrap(string body, string members = "") => EfCoreMock + @"
namespace TestApp
{
    using System;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class Model
    {
        private int _tenantId = 1;
        private bool _softDelete = true;
" + members + @"
        public void OnModelCreating(ModelBuilder modelBuilder)
        {
" + body + @"
        }
    }
}";

    private static string Overwritten(int count) => OverwrittenQueryFilterAnalyzer.OverwrittenMessage(count);

    private static DiagnosticResult Local() => VerifyCS.Diagnostic(OverwrittenQueryFilterAnalyzer.Rule);

    private static DiagnosticResult CrossMember() => VerifyCS.Diagnostic(OverwrittenQueryFilterAnalyzer.CrossMemberRule);

    [Fact]
    public async Task TwoUnnamedFiltersOnOneEntity_ReportsBoth()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().{|#0:HasQueryFilter|}(b => b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().{|#1:HasQueryFilter|}(b => !b.IsDeleted);"),
            Local().WithLocation(0).WithLocation(1).WithArguments("Blog", Overwritten(2)),
            Local().WithLocation(1).WithLocation(0).WithArguments("Blog", Overwritten(2)));
    }

    [Fact]
    public async Task FiltersInSeparateConfigurationClasses_Report()
    {
        var code = EfCoreMock + @"
namespace TestApp
{
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class BlogTenantConfiguration : IEntityTypeConfiguration<Blog>
    {
        public void Configure(EntityTypeBuilder<Blog> builder)
        {
            builder.{|#0:HasQueryFilter|}(b => b.TenantId == 1);
        }
    }

    public class BlogSoftDeleteConfiguration : IEntityTypeConfiguration<Blog>
    {
        public void Configure(EntityTypeBuilder<Blog> builder)
        {
            builder.HasKey(b => b.Id).{|#1:HasQueryFilter|}(b => !b.IsDeleted);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(code,
            CrossMember().WithLocation(0).WithLocation(1).WithArguments("Blog", Overwritten(2)),
            CrossMember().WithLocation(1).WithLocation(0).WithArguments("Blog", Overwritten(2)));
    }

    [Fact]
    public async Task MixedNamedAndUnnamed_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasQueryFilter(""Tenant"", b => b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().{|#0:HasQueryFilter|}(b => !b.IsDeleted);"),
            Local().WithLocation(0).WithArguments(
                "Blog", OverwrittenQueryFilterAnalyzer.MixedMessage));
    }

    [Fact]
    public async Task NamedAndUnnamedInDifferentClasses_ReportsAtCompilationEnd()
    {
        var code = EfCoreMock + @"
namespace TestApp
{
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class BlogTenantConfiguration : IEntityTypeConfiguration<Blog>
    {
        public void Configure(EntityTypeBuilder<Blog> builder) => builder.HasQueryFilter(""Tenant"", b => b.TenantId == 1);
    }

    public class BlogSoftDeleteConfiguration : IEntityTypeConfiguration<Blog>
    {
        public void Configure(EntityTypeBuilder<Blog> builder) => builder.{|#0:HasQueryFilter|}(b => !b.IsDeleted);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(code,
            CrossMember().WithLocation(0).WithArguments("Blog", OverwrittenQueryFilterAnalyzer.MixedMessage));
    }

    [Fact]
    public async Task ConflictsInsideAndAcrossMembers_ReportBoth()
    {
        var code = Wrap(@"
            modelBuilder.Entity<Blog>().{|#0:HasQueryFilter|}(b => b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().{|#1:HasQueryFilter|}(b => !b.IsDeleted);", @"
        public void ConfigureMore(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Blog>().{|#2:HasQueryFilter|}(b => b.Id > 0);
        }
");

        await VerifyCS.VerifyAnalyzerAsync(code,
            Local().WithLocation(0).WithLocation(1).WithArguments("Blog", Overwritten(2)),
            Local().WithLocation(1).WithLocation(0).WithArguments("Blog", Overwritten(2)),
            CrossMember().WithLocation(0).WithLocation(2).WithLocation(1).WithArguments("Blog", Overwritten(3)),
            CrossMember().WithLocation(1).WithLocation(2).WithLocation(0).WithArguments("Blog", Overwritten(3)),
            CrossMember().WithLocation(2).WithLocation(0).WithLocation(1).WithArguments("Blog", Overwritten(3)));
    }

    [Fact]
    public async Task OneFilterPerEntity_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId && !b.IsDeleted);
            modelBuilder.Entity<Post>().HasQueryFilter(p => !p.IsDeleted);"));
    }

    [Fact]
    public async Task NamedFiltersOnly_StayQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasQueryFilter(""Tenant"", b => b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().HasQueryFilter(""SoftDelete"", b => !b.IsDeleted);"));
    }

    [Fact]
    public async Task FiltersInExclusiveBranches_StayQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            if (_softDelete)
                modelBuilder.Entity<Blog>().HasQueryFilter(b => !b.IsDeleted && b.TenantId == _tenantId);
            else if (_tenantId > 0)
                modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId);
            else
            {
                modelBuilder.Entity<Blog>().HasQueryFilter(b => true);
            }

            _ = _softDelete
                ? modelBuilder.Entity<Post>().HasQueryFilter(p => !p.IsDeleted)
                : modelBuilder.Entity<Post>().HasQueryFilter(p => p.Id > 0);"));
    }

    [Fact]
    public async Task SequentialIfs_Report()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            if (_softDelete)
                modelBuilder.Entity<Blog>().{|#0:HasQueryFilter|}(b => !b.IsDeleted);
            if (_tenantId > 0)
                modelBuilder.Entity<Blog>().{|#1:HasQueryFilter|}(b => b.TenantId == _tenantId);"),
            Local().WithLocation(0).WithLocation(1).WithArguments("Blog", Overwritten(2)),
            Local().WithLocation(1).WithLocation(0).WithArguments("Blog", Overwritten(2)));
    }

    [Fact]
    public async Task ClearingFilterWithNull_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasQueryFilter(b => !b.IsDeleted);
            modelBuilder.Entity<Blog>().HasQueryFilter(null);"));
    }

    [Fact]
    public async Task UnrelatedHasQueryFilter_StaysQuiet()
    {
        var code = EfCoreMock + @"
namespace TestApp
{
    public class FakeBuilder<T>
    {
        public FakeBuilder<T> HasQueryFilter(System.Func<T, bool> filter) => this;
    }

    public class Uses
    {
        public void Run(FakeBuilder<Blog> builder)
        {
            builder.HasQueryFilter(b => b.IsDeleted);
            builder.HasQueryFilter(b => b.TenantId == 1);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(code);
    }
}
