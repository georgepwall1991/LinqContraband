using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer,
    LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeFixer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeFixerTests
{
    // The call site only needs DbSet<T>.RemoveRange, an instance member, so it compiles without
    // `using Microsoft.EntityFrameworkCore;`. ExecuteDelete is an extension method and does not.
    private static string WithAddedEfCoreUsing(string mock) =>
        mock.Replace(
            "using System.Threading.Tasks;\n",
            "using System.Threading.Tasks;\nusing Microsoft.EntityFrameworkCore;\n");

    [Fact]
    public async Task Fixer_WithoutEfCoreUsing_AddsTheUsing()
    {
        var test = EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            {|LC012:users.RemoveRange(query)|};
        }
    }
}";

        var fixedCode = WithAddedEfCoreUsing(EFCoreMock) + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            query.ExecuteDelete();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_WithoutEfCoreUsing_InAsyncMethod_AddsTheUsing()
    {
        var test = EFCoreMockWithAsync + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public async Task TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            {|LC012:users.RemoveRange(query)|};
            await Task.CompletedTask;
        }
    }
}";

        var fixedCode = WithAddedEfCoreUsing(EFCoreMockWithAsync) + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public async Task TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            await query.ExecuteDeleteAsync();
            await Task.CompletedTask;
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task FixAll_RepeatedRemoveRangeOfSameQuery_KeepsBothDeletes()
    {
        // Each RemoveRange becomes its own ExecuteDelete. The second one deletes no rows because
        // the first already removed them, the same end state the tracked deletes produced, so the
        // fixer keeps both statements rather than guessing which one the author meant to keep.
        var test = EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            {|LC012:users.RemoveRange(query)|};
            {|LC012:users.RemoveRange(query)|};
        }
    }
}";

        var fixedCode = WithAddedEfCoreUsing(EFCoreMock) + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }
    public class TestClass
    {
        public void TestMethod(Microsoft.EntityFrameworkCore.DbSet<User> users)
        {
            var query = users.Where(x => x.Id > 0);
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            query.ExecuteDelete();
            // Warning: ExecuteDelete bypasses change tracking and cascades.
            query.ExecuteDelete();
        }
    }
}";

        await new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "UseExecuteDelete"
        }.RunAsync();
    }
}
