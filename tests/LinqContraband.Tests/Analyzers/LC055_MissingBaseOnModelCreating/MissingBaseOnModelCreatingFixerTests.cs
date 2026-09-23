using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC055_MissingBaseOnModelCreating.MissingBaseOnModelCreatingAnalyzer,
    LinqContraband.Analyzers.LC055_MissingBaseOnModelCreating.MissingBaseOnModelCreatingFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC055_MissingBaseOnModelCreating;

public class MissingBaseOnModelCreatingFixerTests
{
    private static string Code(string contexts) => MissingBaseOnModelCreatingTests.Code(contexts);

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = Code(before), FixedCode = Code(after) }.RunAsync();
    }

    [Fact]
    public async Task InsertsBaseCallBeforeExistingConfiguration()
    {
        await VerifyFixAsync(@"
public class AppDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder)
    {
        // Orders use a natural key.
        builder.Entity<Order>().HasKey(""Id"");
    }
}", @"
public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Orders use a natural key.
        builder.Entity<Order>().HasKey(""Id"");
    }
}");
    }

    [Fact]
    public async Task UsesTheParameterName()
    {
        await VerifyFixAsync(@"
public class AppDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder mb)
    {
        mb.Entity<Order>().HasKey(""Id"");
    }
}", @"
public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        mb.Entity<Order>().HasKey(""Id"");
    }
}");
    }

    [Fact]
    public async Task FillsEmptyOverride()
    {
        await VerifyFixAsync(@"
public class AppDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder)
    {
    }
}", @"
public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }
}");
    }

    [Fact]
    public async Task ConvertsExpressionBodyToBlock()
    {
        await VerifyFixAsync(@"
public class AppDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder) => builder.Entity<Order>().HasKey(""Id"");
}", @"
public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Order>().HasKey(""Id"");
    }
}");
    }

    [Fact]
    public async Task FixAll_FixesEveryContext()
    {
        var before = Code(@"
public class AppDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(""Id"");
    }
}

public class AuditDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(""Id"");
    }
}");
        var after = Code(@"
public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Order>().HasKey(""Id"");
    }
}

public class AuditDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Order>().HasKey(""Id"");
    }
}");

        await new CodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }
}
