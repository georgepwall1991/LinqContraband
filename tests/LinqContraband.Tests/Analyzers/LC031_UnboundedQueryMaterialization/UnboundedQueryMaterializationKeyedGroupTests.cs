using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization.UnboundedQueryMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// <c>Where(x =&gt; ids.Contains(x.Prop)).GroupBy(x =&gt; x.Prop).Select(g =&gt; new { g.Key, Count = g.Count() })</c>
/// returns at most one row per id in the in-memory list, so it is bounded by the keys supplied. The projection may
/// only read the group's key and aggregates; a projection that carries the group's rows is not bounded.
/// </summary>
public partial class UnboundedQueryMaterializationTests
{
    [Theory]
    [InlineData("var result = db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.Count() }).ToList();")]
    [InlineData("var result = db.Users.Where(u => ids.Contains(u.TeamId) && u.IsActive).GroupBy(u => u.TeamId).Select(g => new { TeamId = g.Key, Active = g.Count(u => u.IsActive), Max = g.Max(u => u.Id) }).ToList();")]
    [InlineData("var result = db.Users.Where(u => ids.Contains(u.TeamId)).Where(u => u.IsActive).OrderBy(u => u.Id).GroupBy(u => u.TeamId).Select(g => g.Count()).ToList();")]
    [InlineData("var result = db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);")]
    [InlineData("var result = db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.Count() }).OrderBy(x => x.Count).ToList();")]
    [InlineData("var result = (from u in db.Users where ids.Contains(u.TeamId) group u by u.TeamId into g select new { g.Key, Count = g.Count() }).ToList();")]
    public Task ContainsFilterGroupedBySameKeyWithAggregateProjection_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(BoundaryProgram(body));

    [Theory]
    // Grouped by a different property than the Contains filters on.
    [InlineData("var result = {|LC031:db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.Id).Select(g => new { g.Key, Count = g.Count() }).ToList()|};")]
    // No Contains filter: one row per distinct key in the table.
    [InlineData("var result = {|LC031:db.Users.Where(u => u.IsActive).GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.Count() }).ToList()|};")]
    [InlineData("var result = {|LC031:db.Users.Where(u => ids.Contains(u.TeamId) || u.IsActive).GroupBy(u => u.TeamId).Select(g => new { g.Key, Count = g.Count() }).ToList()|};")]
    // The groups themselves, or their rows, are materialized.
    [InlineData("var result = {|LC031:db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).ToList()|};")]
    [InlineData("var result = {|LC031:db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).Select(g => new { g.Key, Users = g.ToList() }).ToList()|};")]
    [InlineData("var result = {|LC031:db.Users.Where(u => ids.Contains(u.TeamId)).GroupBy(u => u.TeamId).SelectMany(g => g).ToList()|};")]
    // A Select between the filter and the GroupBy changes what the key refers to.
    [InlineData("var result = {|LC031:db.Users.Where(u => ids.Contains(u.TeamId)).Select(u => new User { Id = u.Id, TeamId = u.Id }).GroupBy(u => u.TeamId).Select(g => g.Count()).ToList()|};")]
    public Task KeyedGroupShapesThatAreNotBounded_StillReport(string body) =>
        VerifyCS.VerifyAnalyzerAsync(BoundaryProgram(body));
}
