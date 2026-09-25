using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerAnalyzer,
    LinqContraband.Analyzers.LC008_SyncBlocker.SyncBlockerFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC008_SyncBlocker;

public partial class SyncBlockerFixerTests
{
    // `await` is illegal in a lock body (CS1996) and in an unsafe context (CS4004), so the fix is withheld there.
    [Fact]
    public async Task FixCrime_InsideLockBody_NoFix()
    {
        var test = Usings + @"
class Program
{
    private readonly object _gate = new object();

    async Task Main()
    {
        var db = new MyDbContext();
        await Task.Yield();
        lock (_gate)
        {
            var n = {|LC008:db.Users.ToList()|};
        }
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = test }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_AfterLockStatement_FixIsApplied()
    {
        var test = Usings + @"
class Program
{
    private readonly object _gate = new object();

    async Task Main()
    {
        var db = new MyDbContext();
        lock (_gate)
        {
        }
        var n = {|LC008:db.Users.ToList()|};
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    private readonly object _gate = new object();

    async Task Main()
    {
        var db = new MyDbContext();
        lock (_gate)
        {
        }
        var n = await db.Users.ToListAsync();
    }
}
" + MockNamespace;

        await new CodeFixTest { TestCode = test, FixedCode = fixedCode }.RunAsync();
    }

    [Fact]
    public async Task FixCrime_InsideUnsafeBlock_NoFix()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        await Task.Yield();
        unsafe
        {
            var n = {|LC008:db.Users.ToList()|};
        }
    }
}
" + MockNamespace;

        await RunWithUnsafeAsync(test);
    }

    [Fact]
    public async Task FixCrime_InsideUnsafeLocalFunction_NoFix()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        await Inner(new MyDbContext());

        unsafe async Task Inner(MyDbContext db)
        {
            var n = {|LC008:db.Users.ToList()|};
        }
    }
}
" + MockNamespace;

        await RunWithUnsafeAsync(test);
    }

    [Fact]
    public async Task FixCrime_InsideUnsafeType_NoFix()
    {
        var test = Usings + @"
unsafe class Program
{
    async Task Main()
    {
        var db = new MyDbContext();
        var n = {|LC008:db.Users.ToList()|};
    }
}
" + MockNamespace;

        await RunWithUnsafeAsync(test);
    }

    private static async Task RunWithUnsafeAsync(string test)
    {
        var testObj = new CodeFixTest { TestCode = test, FixedCode = test };
        testObj.SolutionTransforms.Add((solution, projectId) =>
        {
            var options = (CSharpCompilationOptions)solution.GetProject(projectId)!.CompilationOptions!;
            return solution.WithProjectCompilationOptions(projectId, options.WithAllowUnsafe(true));
        });
        await testObj.RunAsync();
    }
}
