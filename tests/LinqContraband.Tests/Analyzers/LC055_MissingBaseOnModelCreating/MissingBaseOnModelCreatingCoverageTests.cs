using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    LinqContraband.Analyzers.LC055_MissingBaseOnModelCreating.MissingBaseOnModelCreatingAnalyzer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC055_MissingBaseOnModelCreating;

/// <summary>
/// Leftover 5.10.0 LC055 arms the original 18 analyzer cases do not isolate.
/// Every shipped fixture keeps <c>DbContext</c>, <c>IdentityDbContext</c> and project
/// bases in the same compilation, so <c>DeclaringSyntaxReferences.Length</c> is 1 and
/// the body is visible. A compiled base (Identity from a package, or any other
/// referenced context) has no syntax: the analyzer assumes it configures the model.
/// EF Core's own <c>DbContext.OnModelCreating</c> is still recognised by name even
/// when it arrives only as metadata.
/// </summary>
public class MissingBaseOnModelCreatingCoverageTests
{
    private const string Library = @"
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
    }
}

namespace Microsoft.AspNetCore.Identity.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore;

    public class IdentityUserLogin { public string LoginProvider { get; set; } }

    public class IdentityDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<IdentityUserLogin>().HasKey(""LoginProvider"");
        }
    }
}

namespace Shared.Persistence
{
    using Microsoft.EntityFrameworkCore;

    public abstract class EmptyPackageContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        }
    }
}
";

    private static async Task<MetadataReference> BuildLibraryAsync()
    {
        var library = CSharpCompilation.Create(
            "Shared.Persistence",
            new[] { CSharpSyntaxTree.ParseText(Library) },
            await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, default),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new MemoryStream();
        var emit = library.Emit(image);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static async Task VerifyAgainstLibraryAsync(string testCode)
    {
        var test = new AnalyzerTest
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AdditionalReferences.Add(await BuildLibraryAsync());
        await test.RunAsync();
    }

    [Fact]
    public async Task ReferencedIdentityDbContext_WithoutBaseCall_Reports()
    {
        // Identity's OnModelCreating lives in a referenced assembly. The body cannot
        // be read, so Length != 1 assumes it configures; skipping that call drops keys.
        await VerifyAgainstLibraryAsync(@"
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class Order { public int Id { get; set; } }

public class AppDbContext : IdentityDbContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(""Id"");
    }
}
");
    }

    [Fact]
    public async Task ReferencedIdentityDbContext_WithBaseCall_StaysQuiet()
    {
        await VerifyAgainstLibraryAsync(@"
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class Order { public int Id { get; set; } }

public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Order>().HasKey(""Id"");
    }
}
");
    }

    [Fact]
    public async Task ReferencedEmptyCustomBase_Reports()
    {
        // A compiled empty override is still assumed to configure: its body cannot
        // be inspected. Adding the base call is harmless.
        await VerifyAgainstLibraryAsync(@"
using Microsoft.EntityFrameworkCore;
using Shared.Persistence;

public class Order { public int Id { get; set; } }

public class ShopContext : EmptyPackageContext
{
    protected override void {|LC055:OnModelCreating|}(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(""Id"");
    }
}
");
    }

    [Fact]
    public async Task ReferencedEfCoreDbContext_StaysQuiet()
    {
        // The name-and-namespace check for EF Core's empty OnModelCreating runs
        // before the metadata assume-configures arm, including when DbContext
        // itself is only a metadata reference.
        await VerifyAgainstLibraryAsync(@"
using Microsoft.EntityFrameworkCore;

public class Order { public int Id { get; set; } }

public class AppDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(""Id"");
    }
}
");
    }
}
