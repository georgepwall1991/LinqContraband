using LinqContraband.Analyzers.LC002_PrematureMaterialization;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

// Bitwarden builds lookups with `(await query.ToListAsync()).ToLookup(...)`. EF Core has no
// ToLookupAsync, so the async buffer is the only way to build one without blocking.
public class PrematureMaterializationAsyncBufferTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        namespace TestNamespace
        {
            public class User
            {
                public int Id { get; set; }
                public int GroupId { get; set; }
            }

            public class DbContext
            {
                public IQueryable<User> Users => new List<User>().AsQueryable();
            }

            class Program
            {
                async Task Run(DbContext db)
                {
        BODY
                }
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public static class AsyncExtensions
            {
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(source.ToList());
                public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source) => Task.FromResult(source.ToArray());
            }
        }
        """;

    [Theory]
    [InlineData("var lookup = (await db.Users.ToListAsync()).ToLookup(u => u.GroupId);")]
    [InlineData("var lookup = (await db.Users.ToArrayAsync()).ToLookup(u => u.GroupId, u => u.Id);")]
    [InlineData("var list = (await db.Users.ToListAsync()).ToImmutableList();")]
    public async Task AsyncBufferBeforeMaterializerWithoutAsyncCounterpart_DoesNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Source.Replace("BODY", body));
    }

    [Fact]
    public async Task AsyncBufferBeforeToHashSet_ReportsWithMaterializeOnceMessage()
    {
        var test = Source.Replace("BODY", "var ids = {|#0:(await db.Users.ToListAsync()).ToHashSet()|};");

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToHashSet", "ToListAsync")
            .WithMessage("'ToListAsync' already materialized the sequence, so 'ToHashSet' copies it again; materialize once with 'ToHashSet'");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SyncBufferBeforeToLookup_StillReports()
    {
        var test = Source.Replace("BODY", "var lookup = {|#0:db.Users.ToList().ToLookup(u => u.GroupId)|};");

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToLookup", "ToList");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
