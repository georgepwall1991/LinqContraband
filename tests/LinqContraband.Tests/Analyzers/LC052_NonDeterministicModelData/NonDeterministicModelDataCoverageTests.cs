using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC052_NonDeterministicModelData.NonDeterministicModelDataAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC052_NonDeterministicModelData;

public partial class NonDeterministicModelDataTests
{
    // Leftover 5.10.0 LC052 arms that the original 13 fixtures do not isolate:
    // * DateTimeOffset.Now (DateTimeOffset.UtcNow / DateTime.Now stay green)
    // * MaxSymbolHops == 3 (one-hop now / seed fixtures stay green at depth 0-1)
    // * ref / out writes that clear a local (assignment overwrite stays green)
    // Guid.CreateVersion7() is implemented but cannot be compiled in the verifier's
    // reference assemblies (CS0117), so it is not pinned here.

    [Fact]
    public async Task HasData_WithDateTimeOffsetNow_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, UpdatedAt = {|LC052:DateTimeOffset.Now|} });"));
    }

    [Fact]
    public async Task HasData_ThroughThreeLocalHops_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var first = DateTime.UtcNow;
            var second = first;
            var third = second;
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = {|LC052:third|} });"));
    }

    [Fact]
    public async Task HasData_AfterRefOverwrite_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var created = DateTime.UtcNow;
            Overwrite(ref created);
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = created });

            static void Overwrite(ref DateTime value) => value = new DateTime(2024, 1, 1);"));
    }

    [Fact]
    public async Task HasData_AfterOutOverwrite_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
            var created = DateTime.UtcNow;
            Assign(out created);
            modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = created });

            static void Assign(out DateTime value) => value = new DateTime(2024, 1, 1);"));
    }
}
