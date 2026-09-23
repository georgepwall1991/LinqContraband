using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenAnalyzer,
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC026_MissingCancellationToken;

/// <summary>
/// <c>CancellationToken.None</c> is how code says "this must not be cancelled" (audit writes, compensating saves in
/// <c>finally</c>), so LC026 no longer reports it unless configured to. Tokens the fixer could not legally use at the
/// call site (declared later, or instance members seen from a static method) no longer count as "in scope".
/// </summary>
public partial class MissingCancellationTokenEdgeCasesTests
{
    private const string ReportExplicitNone = "is_global = true\ndotnet_code_quality.LC026.report_explicit_none = true\n";

    private static string IntentProgram(string members) => @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class User { public int Id { get; set; } }

    public class TestClass
    {
" + members + @"
    }
}";

    private static async Task VerifyFixWithExplicitNoneReportedAsync(string test, string fixedCode)
    {
        var fixTest = new CodeFixTest { TestCode = test, FixedCode = fixedCode };
        fixTest.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", ReportExplicitNone));
        fixTest.FixedState.AnalyzerConfigFiles.Add(("/.globalconfig", ReportExplicitNone));
        await fixTest.RunAsync();
    }

    [Theory]
    [InlineData(@"
        public async Task Run(DbSet<User> query, CancellationToken ct)
        {
            var users = await query.ToListAsync(CancellationToken.None);
        }")]
    [InlineData(@"
        public async Task Run(DbSet<User> query, CancellationToken ct)
        {
            var users = await query.ToListAsync(cancellationToken: CancellationToken.None);
        }")]
    [InlineData(@"
        public async Task Run(DbSet<User> query, CancellationToken ct)
        {
            try
            {
                await query.ToListAsync(ct);
            }
            finally
            {
                await query.ToListAsync(CancellationToken.None);
            }
        }")]
    public Task ExplicitCancellationTokenNone_IsQuietByDefault(string members) =>
        VerifyCS.VerifyAnalyzerAsync(IntentProgram(members));

    [Fact]
    public async Task ExplicitCancellationTokenNone_ReportsWhenConfigured()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC026_MissingCancellationToken.MissingCancellationTokenAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = IntentProgram(@"
        public async Task Run(DbSet<User> query, CancellationToken ct)
        {
            var users = await {|LC026:query.ToListAsync(CancellationToken.None)|};
        }")
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", ReportExplicitNone));
        await test.RunAsync();
    }

    [Theory]
    // `default` still reads as "forgot the token".
    [InlineData(@"
        public async Task Run(DbSet<User> query, CancellationToken ct)
        {
            var users = await {|LC026:query.ToListAsync(default)|};
        }")]
    [InlineData(@"
        public async Task Run(DbSet<User> query, CancellationToken ct)
        {
            var users = await {|LC026:query.ToListAsync()|};
        }")]
    public Task OmittedOrDefaultToken_StillReports(string members) =>
        VerifyCS.VerifyAnalyzerAsync(IntentProgram(members));

    [Theory]
    // A token declared after the call cannot be passed to it (CS0841).
    [InlineData(@"
        public async Task Run(DbSet<User> query)
        {
            var users = await query.ToListAsync();
            var ct = new CancellationTokenSource().Token;
            await query.ToListAsync(ct);
        }")]
    // An instance token is not reachable from a static method (CS0120).
    [InlineData(@"
        private CancellationToken _stopping;

        public static async Task Run(DbSet<User> query)
        {
            var users = await query.ToListAsync();
        }")]
    [InlineData(@"
        private CancellationToken Stopping { get; }

        public static async Task Run(DbSet<User> query)
        {
            var users = await query.ToListAsync();
        }")]
    public Task TokenNotUsableAtTheCall_IsQuiet(string members) =>
        VerifyCS.VerifyAnalyzerAsync(IntentProgram(members));

    [Theory]
    // A static token is reachable from a static method, and an earlier local from a later call.
    [InlineData(@"
        private static CancellationToken s_stopping;

        public static async Task Run(DbSet<User> query)
        {
            var users = await {|LC026:query.ToListAsync()|};
        }")]
    [InlineData(@"
        public async Task Run(DbSet<User> query)
        {
            var ct = new CancellationTokenSource().Token;
            var users = await {|LC026:query.ToListAsync()|};
        }")]
    public Task UsableToken_StillReports(string members) =>
        VerifyCS.VerifyAnalyzerAsync(IntentProgram(members));
}
