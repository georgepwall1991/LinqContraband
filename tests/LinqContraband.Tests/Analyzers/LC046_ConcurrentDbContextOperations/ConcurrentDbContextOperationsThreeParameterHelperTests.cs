using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsThreeParameterHelperTests
{
    private static string App(string body) =>
        @"using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;" + ConcurrentDbContextOperationsTests.EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
        public int ThrowingIndex => throw new InvalidOperationException();
    }

    public sealed class Selector
    {
        public int Select(User user) => 0;
    }

    public sealed class Program
    {
" + body + @"
    }
}";

    [Fact]
    public async Task TaskWhenAll_WithThreeParameterCapturedContext_ShouldTrigger()
    {
        var test = App(@"
        public async Task Run(AppDbContext db)
        {
            Task<User> Load(int id, bool unused, CancellationToken token) =>
                db.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                {|#0:Load(0, false, CancellationToken.None)|},
                {|#1:Load(1, false, CancellationToken.None)|});
        }
");

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithThreeParameterContextFirst_ShouldTrigger()
    {
        var test = App(@"
        public async Task Run(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int id, CancellationToken token) =>
                current.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                {|#0:Load(db, 0, CancellationToken.None)|},
                {|#1:Load(db, 1, CancellationToken.None)|});
        }
");

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithOmittedOptionalThirdParameter_ShouldTrigger()
    {
        var test = App(@"
        public async Task CapturedContext(AppDbContext db)
        {
            Task<User> Load(int id, bool unused, CancellationToken token = default) =>
                db.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                {|#0:Load(0, false)|},
                {|#1:Load(1, false)|});
        }

        public async Task ContextParameter(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int id, int ignored = 0) =>
                current.Users.ElementAtAsync(id);

            await Task.WhenAll(
                {|#2:Load(db, 0)|},
                {|#3:Load(db, 1)|});
        }
");

        var captured = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var contextParameter = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, captured, contextParameter);
    }

    [Fact]
    public async Task TaskWhenAll_WithReorderedNamedThreeParameterContext_ShouldTrigger()
    {
        var test = App(@"
        public async Task Run(AppDbContext db)
        {
            Task<User> Load(int id, AppDbContext current, CancellationToken token) =>
                current.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                {|#0:Load(current: db, id: 0, token: CancellationToken.None)|},
                {|#1:Load(current: db, id: 1, token: CancellationToken.None)|});
        }
");

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithFourParameterHelpers_ShouldNotTrigger()
    {
        var test = App(@"
        public async Task CapturedContext(AppDbContext db)
        {
            Task<User> Load(int id, bool unused, CancellationToken token, bool extra) =>
                db.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                Load(0, false, CancellationToken.None, true),
                Load(1, false, CancellationToken.None, true));
        }

        public async Task ContextParameter(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int id, bool unused, CancellationToken token) =>
                current.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                Load(db, 0, false, CancellationToken.None),
                Load(db, 1, false, CancellationToken.None));
        }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithUnprovenThreeParameterHelpers_ShouldNotTrigger()
    {
        var test = App(@"
        public async Task HelperChain(AppDbContext db)
        {
            Task<User> Inner(int id, CancellationToken token) =>
                db.Users.ElementAtAsync(id, token);
            Task<User> Load(int id, bool unused, CancellationToken token) =>
                Inner(id, token);

            await Task.WhenAll(
                Load(0, false, CancellationToken.None),
                Load(1, false, CancellationToken.None));
        }

        public async Task MultiOperationBody(AppDbContext db)
        {
            Task<User> Load(int id, bool unused, CancellationToken token)
            {
                var index = id;
                return db.Users.ElementAtAsync(index, token);
            }

            await Task.WhenAll(
                Load(0, false, CancellationToken.None),
                Load(1, false, CancellationToken.None));
        }

        public async Task BranchBody(AppDbContext db, bool first)
        {
            Task<User> Load(int id, bool unused, CancellationToken token) =>
                first
                    ? db.Users.ElementAtAsync(id, token)
                    : db.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                Load(0, false, CancellationToken.None),
                Load(1, false, CancellationToken.None));
        }

        public async Task DistinctContexts(AppDbContext db, AppDbContext other)
        {
            Task<User> Load(AppDbContext current, int id, CancellationToken token) =>
                current.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                Load(db, 0, CancellationToken.None),
                Load(other, 1, CancellationToken.None));
        }

        public async Task ReassignedContext(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int id, CancellationToken token) =>
                current.Users.ElementAtAsync(id, token);

            var first = Load(db, 0, CancellationToken.None);
            db = new AppDbContext();
            var second = Load(db, 1, CancellationToken.None);
            await Task.WhenAll(first, second);
        }

        public async Task ThrowingCallArgument(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int id, CancellationToken token) =>
                current.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                Load(db, GetIndex(), CancellationToken.None),
                Load(db, GetIndex(), CancellationToken.None));
        }

        public async Task ThrowingParameterTransform(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int divisor, CancellationToken token) =>
                current.Users.ElementAtAsync(10 / divisor, token);

            await Task.WhenAll(
                Load(db, 1, CancellationToken.None),
                Load(db, 2, CancellationToken.None));
        }

        public async Task ThrowingPropertyArgument(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int id, CancellationToken token) =>
                current.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                Load(db, db.ThrowingIndex, CancellationToken.None),
                Load(db, db.ThrowingIndex, CancellationToken.None));
        }

        public async Task NullableMethodGroupReceiver(AppDbContext db, Selector? selector)
        {
            Task<Dictionary<int, User>> Load(
                AppDbContext current,
                int ignored,
                CancellationToken token) =>
                current.Users.ToDictionaryAsync(selector.Select);

            await Task.WhenAll(
                Load(db, 0, CancellationToken.None),
                Load(db, 1, CancellationToken.None));
        }

        public async Task DefinitelyCancelledToken(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int id, CancellationToken token) =>
                current.Users.ElementAtAsync(id, token);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(
                Load(db, 0, canceled),
                Load(db, 1, canceled));
        }

        public async Task AmbiguousTwoContextParameters(
            AppDbContext db,
            AppDbContext other)
        {
            Task<bool> Load(AppDbContext current, AppDbContext also, int ignored) =>
                current.Users.AnyAsync();

            await Task.WhenAll(
                Load(db, other, 0),
                Load(db, other, 1));
        }

        public async Task HelperLocalContext(AppDbContext db)
        {
            Task<User> Load(int id, bool unused, CancellationToken token) =>
                new AppDbContext().Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                Load(0, false, CancellationToken.None),
                Load(1, false, CancellationToken.None));
        }

        public async Task CapturedThrowingCallArgument(AppDbContext db)
        {
            Task<User> Load(int id, bool unused, CancellationToken token) =>
                db.Users.ElementAtAsync(id, token);

            await Task.WhenAll(
                Load(GetIndex(), false, CancellationToken.None),
                Load(GetIndex(), false, CancellationToken.None));
        }

        public async Task CapturedDefinitelyCancelledToken(AppDbContext db)
        {
            Task<User> Load(int id, bool unused, CancellationToken token) =>
                db.Users.ElementAtAsync(id, token);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(
                Load(0, false, canceled),
                Load(1, false, canceled));
        }

        private static int GetIndex() => 0;
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithInvalidOrTransformedThreeParameterRequiredArguments_ShouldNotTrigger()
    {
        var test = App(@"
        public async Task InvalidRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql, int ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(Execute(db, """", 0), Execute(db, """", 1));
        }

        public async Task CapturedInvalidRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(string sql, bool unused, int ignored) =>
                db.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(Execute("""", false, 0), Execute("""", false, 1));
        }

        public async Task TransformedInvalidRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql, int ignored) =>
                current.Database.ExecuteSqlRawAsync(sql ?? """");

            await Task.WhenAll(Execute(db, null, 0), Execute(db, null, 1));
        }

        public async Task TransformedInvalidBoundRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string command, int ignored) =>
                current.Database.ExecuteSqlRawAsync(command);

            string sql = null;
            await Task.WhenAll(
                Execute(db, sql ?? """", 0),
                Execute(db, sql ?? """", 1));
        }

        public async Task TransformedCapturedInvalidRequiredArgument(AppDbContext db)
        {
            string sql = null;
            Task<int> Execute(AppDbContext current, int ignored, bool unused) =>
                current.Database.ExecuteSqlRawAsync(sql ?? """");

            await Task.WhenAll(Execute(db, 0, false), Execute(db, 1, false));
        }

        public async Task WrappedCancelledToken(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int id, CancellationToken token) =>
                current.Users.ElementAtAsync(
                    id,
                    true ? token : CancellationToken.None);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(Load(db, 0, canceled), Load(db, 1, canceled));
        }

        public async Task CapturedWrappedCancelledToken(AppDbContext db)
        {
            Task<User> Load(int id, bool unused, CancellationToken token) =>
                db.Users.ElementAtAsync(
                    id,
                    true ? token : CancellationToken.None);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(
                Load(0, false, canceled),
                Load(1, false, canceled));
        }

        public async Task StableInvalidRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql, int ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            var sql = """";
            await Task.WhenAll(Execute(db, sql, 0), Execute(db, sql, 1));
        }

        public async Task StringEmptyRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql, int ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                Execute(db, string.Empty, 0),
                Execute(db, string.Empty, 1));
        }

        public async Task InvalidNestedSetName(AppDbContext db)
        {
            Task<bool> Load(AppDbContext current, string name, int ignored) =>
                current.Set<User>(name).AnyAsync();

            await Task.WhenAll(Load(db, """", 0), Load(db, """", 1));
        }

        public async Task CapturedRequiredLocalInvalidatedByCompanionArgument(
            AppDbContext db)
        {
            var sql = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, bool ignored, int extra) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                Execute(db, (sql = """") != null, 0),
                Execute(db, false, 1));
        }

        public async Task CapturedRequiredLocalInvalidatedByDeconstructionArgument(
            AppDbContext db)
        {
            var sql = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, (string, int) ignored, int extra) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                Execute(db, (sql, _) = ("""", 0), 0),
                Execute(db, (sql, _) = ("""", 0), 1));
        }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public void DirectLocalFunctionProof_CapsParametersAtThree()
    {
        var classificationPath = Path.Combine(
            LinqContraband.Tests.Architecture.RepositoryLayout.GetRepositoryRoot(),
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC046_ConcurrentDbContextOperations",
            "ConcurrentDbContextOperationsClassification.cs");
        var source = File.ReadAllText(classificationPath);
        Assert.Contains("localFunction.Symbol.Parameters.Length > 3", source, StringComparison.Ordinal);
    }
}
