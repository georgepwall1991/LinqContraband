using LinqContraband.Analyzers.LC002_PrematureMaterialization;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

// Duende IdentityServer's EntityFramework.Storage stores filter in SQL, materialize, then re-apply the same
// predicate in memory on purpose: the database collation may be case-insensitive, and the in-memory re-check
// enforces the exact, case-sensitive match. Folding the re-check back into SQL would remove that guarantee.
public class PrematureMaterializationUpstreamRecheckTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Query;

        namespace TestNamespace
        {
            public class Scope
            {
                public int Id { get; set; }
            }

            public class Client
            {
                public int Id { get; set; }
                public string ClientId { get; set; } = "";
                public string Name { get; set; } = "";
                public bool Enabled { get; set; }
                public List<Scope> AllowedScopes { get; set; } = new();
            }

            public class PersistedGrant
            {
                public string Key { get; set; } = "";
                public string Type { get; set; } = "";
            }

            public class DbContext
            {
                public IQueryable<Client> Clients => new List<Client>().AsQueryable();
                public IQueryable<PersistedGrant> PersistedGrants => new List<PersistedGrant>().AsQueryable();
            }

            class Store
            {
                DbContext Context = new DbContext();

                async Task Run(string clientId, string key, string otherId, CancellationToken ct)
                {
        BODY
                }
            }
        }

        namespace Microsoft.EntityFrameworkCore.Query
        {
            public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity>
            {
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public static class EntityFrameworkQueryableExtensions
            {
                public static IIncludableQueryable<T, P> Include<T, P>(this IQueryable<T> source, System.Linq.Expressions.Expression<Func<T, P>> path) => throw null!;
                public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
                public static IQueryable<T> AsSplitQuery<T>(this IQueryable<T> source) => source;
                public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Task.FromResult(source.ToArray());
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Task.FromResult(source.ToList());
            }
        }
        """;

    [Theory]
    // Query held in a local, as in ClientStore.
    [InlineData("""
        var query = Context.Clients.Where(x => x.ClientId == clientId).Include(x => x.AllowedScopes).AsNoTracking().AsSplitQuery();
        var client = (await query.ToArrayAsync(ct)).SingleOrDefault(x => x.ClientId == clientId);
        """)]
    // Direct chain, as in PersistedGrantStore.
    [InlineData("var grant = (await Context.PersistedGrants.Where(x => x.Key == key).ToArrayAsync(ct)).SingleOrDefault(x => x.Key == key);")]
    // Different lambda parameter name.
    [InlineData("var grant = (await Context.PersistedGrants.Where(x => x.Key == key).ToArrayAsync(ct)).SingleOrDefault(g => g.Key == key);")]
    // Symmetric == operands.
    [InlineData("var grant = (await Context.PersistedGrants.Where(x => x.Key == key).ToArrayAsync(ct)).SingleOrDefault(x => key == x.Key);")]
    // Parenthesized body.
    [InlineData("var grant = (await Context.PersistedGrants.Where(x => x.Key == key).ToArrayAsync(ct)).FirstOrDefault(x => (x.Key == key));")]
    // Sync materializer and Where re-check.
    [InlineData("var grants = Context.PersistedGrants.Where(x => x.Key == key).ToList().Where(x => x.Key == key);")]
    // && of conditions that are each applied upstream, across one or several Where calls.
    [InlineData("var grant = Context.PersistedGrants.Where(x => x.Key == key && x.Type == \"code\").ToList().SingleOrDefault(x => x.Type == \"code\" && x.Key == key);")]
    [InlineData("var grant = Context.PersistedGrants.Where(x => x.Key == key).Where(x => x.Type == \"code\").ToList().SingleOrDefault(x => x.Key == key && x.Type == \"code\");")]
    // Subset of an upstream && filter.
    [InlineData("var grant = Context.PersistedGrants.Where(x => x.Key == key && x.Type == \"code\").ToList().SingleOrDefault(x => x.Key == key);")]
    // Upstream filter reached through two locals.
    [InlineData("""
        var filtered = Context.Clients.Where(x => x.ClientId == clientId);
        var query = filtered.AsNoTracking();
        var client = query.ToList().FirstOrDefault(c => c.ClientId == clientId);
        """)]
    // Boolean member and negation.
    [InlineData("var clients = Context.Clients.Where(x => !x.Enabled).ToList().Any(x => !x.Enabled);")]
    public async Task InMemoryRecheckOfUpstreamFilter_DoesNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Source.Replace("BODY", body));
    }

    [Theory]
    // Different member.
    [InlineData("var client = {|#0:Context.Clients.Where(x => x.ClientId == clientId).ToList().SingleOrDefault(x => x.Name == clientId)|};", "SingleOrDefault")]
    // Different value.
    [InlineData("var client = {|#0:Context.Clients.Where(x => x.ClientId == clientId).ToList().SingleOrDefault(x => x.ClientId == otherId)|};", "SingleOrDefault")]
    // Different operator.
    [InlineData("var client = {|#0:Context.Clients.Where(x => x.ClientId == clientId).ToList().SingleOrDefault(x => x.ClientId != clientId)|};", "SingleOrDefault")]
    // Extra condition not applied upstream.
    [InlineData("var client = {|#0:Context.Clients.Where(x => x.ClientId == clientId).ToList().SingleOrDefault(x => x.ClientId == clientId && x.Enabled)|};", "SingleOrDefault")]
    // || is not a conjunction of applied filters.
    [InlineData("var client = {|#0:Context.Clients.Where(x => x.ClientId == clientId).ToList().SingleOrDefault(x => x.ClientId == clientId || x.Enabled)|};", "SingleOrDefault")]
    // No upstream filter at all.
    [InlineData("var clients = {|#0:Context.Clients.ToList().Where(x => x.ClientId == clientId)|};", "Where")]
    // Upstream filter is on the far side of a projection.
    [InlineData("var ids = {|#0:Context.Clients.Where(x => x.Enabled).Select(x => x.Enabled).ToList().Count(x => x)|};", "Count")]
    // Upstream filter captured in a local.
    [InlineData("""
        var query = Context.Clients.Where(x => x.ClientId == clientId);
        var client = {|#0:(await query.ToArrayAsync(ct)).SingleOrDefault(x => x.Name == clientId)|};
        """, "SingleOrDefault")]
    public async Task InMemoryPredicateNotAppliedUpstream_StillReports(string body, string method)
    {
        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments(method);

        await VerifyCS.VerifyAnalyzerAsync(Source.Replace("BODY", body), expected);
    }
}
