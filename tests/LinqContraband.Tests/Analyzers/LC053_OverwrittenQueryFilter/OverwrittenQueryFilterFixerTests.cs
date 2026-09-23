using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC053_OverwrittenQueryFilter.OverwrittenQueryFilterAnalyzer,
    LinqContraband.Analyzers.LC053_OverwrittenQueryFilter.OverwrittenQueryFilterFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC053_OverwrittenQueryFilter;

public class OverwrittenQueryFilterFixerTests
{
    private static string Wrap(string body, string members = "") => OverwrittenQueryFilterTests.Wrap(body, members);

    /// <summary>LC053 has a live and a compilation-end descriptor; markup only needs the id and location.</summary>
    private sealed class Test : CodeFixTest
    {
        public Test()
        {
            MarkupOptions = MarkupOptions.UseFirstDescriptor;
        }
    }

    [Fact]
    public async Task CombinesTwoFiltersInOneMethod()
    {
        await new Test
        {
            TestCode = Wrap(@"
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => !b.IsDeleted);"),
            FixedCode = Wrap(@"
            modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId && !b.IsDeleted);")
        }.RunAsync();
    }

    [Fact]
    public async Task RenamesTheEarlierParameter()
    {
        await new Test
        {
            TestCode = Wrap(@"
            var blog = modelBuilder.Entity<Blog>();
            blog.{|LC053:HasQueryFilter|}(x => x.TenantId == _tenantId);
            blog.HasKey(x => x.Id);
            blog.{|LC053:HasQueryFilter|}((Blog b) => !b.IsDeleted);"),
            FixedCode = Wrap(@"
            var blog = modelBuilder.Entity<Blog>();
            blog.HasKey(x => x.Id);
            blog.HasQueryFilter((Blog b) => b.TenantId == _tenantId && !b.IsDeleted);")
        }.RunAsync();
    }

    [Fact]
    public async Task ParenthesizesLooserOperands()
    {
        await new Test
        {
            TestCode = Wrap(@"
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => b.TenantId == 0 || b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => _softDelete ? !b.IsDeleted : true);"),
            FixedCode = Wrap(@"
            modelBuilder.Entity<Blog>().HasQueryFilter(b => (b.TenantId == 0 || b.TenantId == _tenantId) && (_softDelete ? !b.IsDeleted : true));")
        }.RunAsync();
    }

    [Fact]
    public async Task FixAll_CombinesEachEntity()
    {
        await new Test
        {
            TestCode = Wrap(@"
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => b.TenantId == _tenantId);
            modelBuilder.Entity<Post>().{|LC053:HasQueryFilter|}(p => p.Id > 0);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => !b.IsDeleted);
            modelBuilder.Entity<Post>().{|LC053:HasQueryFilter|}(p => !p.IsDeleted);"),
            FixedCode = Wrap(@"            modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId && !b.IsDeleted);
            modelBuilder.Entity<Post>().HasQueryFilter(p => p.Id > 0 && !p.IsDeleted);"),
            BatchFixedCode = Wrap(@"            modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId && !b.IsDeleted);
            modelBuilder.Entity<Post>().HasQueryFilter(p => p.Id > 0 && !p.IsDeleted);"),
            NumberOfIncrementalIterations = 2
        }.RunAsync();
    }

    [Fact]
    public async Task KeepsCommentAboveTheRemovedFilter()
    {
        await new Test
        {
            TestCode = Wrap(@"
            // Tenant isolation
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => !b.IsDeleted);"),
            FixedCode = Wrap(@"
            // Tenant isolation
            modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId && !b.IsDeleted);")
        }.RunAsync();
    }

    [Fact]
    public async Task NameCollision_NoFix()
    {
        var code = Wrap(@"
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(x => x.TenantId == b);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => !b.IsDeleted);", @"
        private int b = 1;
");

        await new Test { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task ReceiverWithSideEffects_NoFix()
    {
        var code = Wrap(@"
            Blogs(modelBuilder).{|LC053:HasQueryFilter|}(b => b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => !b.IsDeleted);", @"
        private EntityTypeBuilder<Blog> Blogs(ModelBuilder modelBuilder) => modelBuilder.Entity<Blog>();
");

        await new Test { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task ThreeFilters_NoFix()
    {
        var code = Wrap(@"
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => b.TenantId == _tenantId);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => !b.IsDeleted);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => b.Id > 0);");

        await new Test { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task ChainedCall_NoFix()
    {
        var code = Wrap(@"
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => b.TenantId == _tenantId).HasKey(b => b.Id);
            modelBuilder.Entity<Blog>().{|LC053:HasQueryFilter|}(b => !b.IsDeleted);");

        await new Test { TestCode = code, FixedCode = code }.RunAsync();
    }
}
