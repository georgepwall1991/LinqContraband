using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerAnalyzer,
    LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC008_SyncBlocker;

public partial class SyncBlockerFixerTests
{
    // The call site sees System.Linq but not Microsoft.EntityFrameworkCore, like the sample project's
    // SyncBlockerSample.cs. The mock entity namespace imports EF Core for itself so the input compiles.
    private const string UsingsWithoutEfCore = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestNamespace;
";

    private const string UsingsWithAddedEfCore = UsingsWithoutEfCore + @"using Microsoft.EntityFrameworkCore;
";

    private static readonly string MockNamespaceImportingEfCoreLocally = MockNamespace.Replace(
        "namespace TestNamespace\n{\n",
        "namespace TestNamespace\n{\n    using Microsoft.EntityFrameworkCore;\n\n");

    [Fact]
    public async Task FixCrime_ToListWithoutEfCoreUsing_AddsTheUsing()
    {
        var test = UsingsWithoutEfCore + @"
class Program
{
    async Task RunAsync(IQueryable<User> users)
    {
        var list = {|LC008:users.ToList()|};
    }
}
" + MockNamespaceImportingEfCoreLocally;

        var fixedCode = UsingsWithAddedEfCore + @"
class Program
{
    async Task RunAsync(IQueryable<User> users)
    {
        var list = await users.ToListAsync();
    }
}
" + MockNamespaceImportingEfCoreLocally;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixAll_WithoutEfCoreUsing_AddsTheUsingOnce()
    {
        var test = UsingsWithoutEfCore + @"
class Program
{
    async Task RunAsync(IQueryable<User> users)
    {
        var list = {|LC008:users.ToList()|};
        var count = {|LC008:users.Count()|};
    }
}
" + MockNamespaceImportingEfCoreLocally;

        var fixedCode = UsingsWithAddedEfCore + @"
class Program
{
    async Task RunAsync(IQueryable<User> users)
    {
        var list = await users.ToListAsync();
        var count = await users.CountAsync();
    }
}
" + MockNamespaceImportingEfCoreLocally;

        await new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "UseAsyncMethod"
        }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_SaveChangesWithoutEfCoreUsing_AddsNoUsing()
    {
        // SaveChangesAsync is an instance member of DbContext, so it binds without the EF Core namespace.
        var test = UsingsWithoutEfCore + @"
class Program
{
    async Task RunAsync()
    {
        var db = new MyDbContext();
        {|LC008:db.SaveChanges()|};
    }
}
" + MockNamespaceImportingEfCoreLocally;

        var fixedCode = UsingsWithoutEfCore + @"
class Program
{
    async Task RunAsync()
    {
        var db = new MyDbContext();
        await db.SaveChangesAsync();
    }
}
" + MockNamespaceImportingEfCoreLocally;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_EfCoreImportedInsideNamespace_AddsNoTopLevelUsing()
    {
        var test = UsingsWithoutEfCore + @"
namespace App
{
    using Microsoft.EntityFrameworkCore;

    class Program
    {
        async Task RunAsync(IQueryable<User> users)
        {
            var list = {|LC008:users.ToList()|};
        }
    }
}
" + MockNamespaceImportingEfCoreLocally;

        var fixedCode = UsingsWithoutEfCore + @"
namespace App
{
    using Microsoft.EntityFrameworkCore;

    class Program
    {
        async Task RunAsync(IQueryable<User> users)
        {
            var list = await users.ToListAsync();
        }
    }
}
" + MockNamespaceImportingEfCoreLocally;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }
}
