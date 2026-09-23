using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC055_MissingBaseOnModelCreating.MissingBaseOnModelCreatingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC055_MissingBaseOnModelCreating;

public class MissingBaseOnModelCreatingTests
{
    internal const string Usings = @"
using System;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
";

    internal const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
    }

    public class ModelBuilder
    {
        public EntityTypeBuilder<T> Entity<T>() where T : class => new EntityTypeBuilder<T>();
    }

    public class EntityTypeBuilder<T> where T : class
    {
        public EntityTypeBuilder<T> HasKey(string name) => this;
        public EntityTypeBuilder<T> HasQueryFilter(Func<T, bool> filter) => this;
    }
}

namespace Microsoft.AspNetCore.Identity.EntityFrameworkCore
{
    public class IdentityUserLogin { public string LoginProvider { get; set; } }

    public class IdentityDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<IdentityUserLogin>().HasKey(""LoginProvider"");
        }
    }
}

public class Order { public int Id { get; set; } public bool IsDeleted { get; set; } }";

    internal static string Code(string contexts) => Usings + contexts + EfMock;

    [Fact]
    public async Task IdentityContext_WithoutBaseCall_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public class AppDbContext : IdentityDbContext
{
    protected override void {|#0:OnModelCreating|}(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(""Id"");
    }
}"), VerifyCS.Diagnostic().WithLocation(0).WithArguments("AppDbContext", "IdentityDbContext"));
    }

    [Fact]
    public async Task ExpressionBodiedOverride_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public class AppDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder) => builder.Entity<Order>().HasKey(""Id"");
}"));
    }

    [Fact]
    public async Task EmptyOverride_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public class AppDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder)
    {
    }
}"));
    }

    [Fact]
    public async Task ProjectBaseContextWithConfiguration_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public abstract class SoftDeleteContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);
    }
}

public class ShopContext : SoftDeleteContext
{
    protected override void {|#0:OnModelCreating|}(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(""Id"");
    }
}"), VerifyCS.Diagnostic().WithLocation(0).WithArguments("ShopContext", "SoftDeleteContext"));
    }

    [Fact]
    public async Task ForwardingMiddleContext_StillReportsSkippedConfiguration()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public class TenantContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder) => base.OnModelCreating(builder);
}

public class AppDbContext : TenantContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(""Id"");
    }
}"));
    }

    [Fact]
    public async Task DirectDbContextSubclass_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public class AppDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(""Id"");
    }
}"));
    }

    [Theory]
    // The base override configures nothing.
    [InlineData(@"
public class BaseContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}")]
    // The base override only forwards to DbContext's empty implementation.
    [InlineData(@"
public class BaseContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}")]
    [InlineData(@"
public class BaseContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => base.OnModelCreating(modelBuilder);
}")]
    // The base does not override OnModelCreating at all.
    [InlineData(@"
public class BaseContext : DbContext
{
    public int Version { get; set; }
}")]
    public async Task BaseConfiguresNothing_NoDiagnostic(string baseContext)
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(baseContext + @"

public class AppDbContext : BaseContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(""Id"");
    }
}"));
    }

    [Theory]
    [InlineData(@"
        base.OnModelCreating(builder);
        builder.Entity<Order>().HasKey(""Id"");")]
    [InlineData(@"
        builder.Entity<Order>().HasKey(""Id"");
        base.OnModelCreating(builder);")]
    [InlineData(@"
        if (builder != null)
        {
            base.OnModelCreating(builder);
        }")]
    [InlineData(@"
        ConfigureAll(builder);
    }

    private void ConfigureAll(ModelBuilder builder)
    {
        base.OnModelCreating(builder);")]
    [InlineData(@"
        Action configure = () => base.OnModelCreating(builder);
        configure();")]
    public async Task BaseCalledAnywhereInType_NoDiagnostic(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {" + body + @"
    }
}"));
    }

    [Fact]
    public async Task PartialContext_BaseCallInOtherPart_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public partial class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ConfigureIdentity(builder);
    }
}

public partial class AppDbContext
{
    private void ConfigureIdentity(ModelBuilder builder) => base.OnModelCreating(builder);
}"));
    }

    [Fact]
    public async Task AbstractBaseOverride_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public abstract class BaseContext : IdentityDbContext
{
    protected abstract override void OnModelCreating(ModelBuilder builder);
}

public class AppDbContext : BaseContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(""Id"");
    }
}"));
    }

    [Fact]
    public async Task NotADbContext_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(Code(@"
public class ModelHost
{
    protected virtual void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(""Id"");
    }
}

public class DerivedHost : ModelHost
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
    }
}"));
    }
}
