using LinqContraband.Analyzers.LC007_NPlusOneLooper;
using LinqContraband.Analyzers.LC010_SaveChangesInLoop;
using LinqContraband.CrossProject;
using LinqContraband.Scan;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.CrossProject;

/// <summary>
/// The join on its own, fed facts by hand: which per-item calls become findings, how far a call is followed, and
/// that the scanner and the analyzer agree on the option, the fact ID and the two rules.
/// </summary>
public class CrossProjectJoinTests
{
    private const string GetById = "M:Shop.Domain.ICustomerRepository.GetById(System.Int32)";
    private const string SqlGetById = "M:Shop.Data.CustomerRepository.GetById(System.Int32)";

    private static CrossProjectFact Fact(string kind, params string[] fields) => new(kind, fields, "src/App.cs", 12, 9);

    private static CrossProjectFact Query(string method, string query = "Find") => Fact("Q", method, query);

    private static CrossProjectFact Save(string method, string save = "SaveChanges") => Fact("S", method, save);

    private static CrossProjectFact Calls(string method, string callee, string kind = "n", string? root = null, bool memoized = false) =>
        Fact("C", method, kind, callee, root ?? callee, memoized ? "1" : "0");

    private static CrossProjectFact Implements(string root, string implementation, bool inTestProject = false) =>
        Fact("I", root, implementation, inTestProject ? "1" : "0");

    private static CrossProjectFact Loop(
        string callee,
        string kind = "n",
        string? root = null,
        string rule = "7",
        string loopKind = "foreach",
        string operatorName = "",
        string helper = "GetById",
        string severity = "d") =>
        Fact("L", rule, kind, callee, root ?? callee, loopKind, operatorName, helper, severity);

    private static IReadOnlyList<Finding> Resolve(params CrossProjectFact[] facts) => CrossProjectJoin.Resolve(facts);

    [Fact]
    public void DirectCall_ToAMethodThatRunsAQuery_IsAnLC007FindingAtTheCallSite()
    {
        var finding = Assert.Single(Resolve(Loop(SqlGetById), Query(SqlGetById)));

        Assert.Equal(
            new Finding(
                "LC007",
                "Warning",
                "'GetById' runs 'Find' in another project on every iteration of the loop, causing N+1 database operations. Fetch data in bulk or eager load before the loop.",
                "src/App.cs",
                12,
                9),
            finding);
    }

    [Fact]
    public void Messages_NameTheOperatorOfALambdaAndTheSaveOfAWrite()
    {
        var lambda = Assert.Single(Resolve(Loop(SqlGetById, loopKind: "lambda", operatorName: "Select", helper: "Load"), Query(SqlGetById, "FirstOrDefaultAsync")));
        Assert.Equal(
            "'Load' runs 'FirstOrDefaultAsync' in another project for every element 'Select' processes, causing N+1 database operations. Fetch data in bulk or eager load before iterating.",
            lambda.Message);

        var save = Assert.Single(Resolve(Loop(SqlGetById, rule: "10", helper: "Add"), Save(SqlGetById, "SaveChangesAsync")));
        Assert.Equal("LC010", save.RuleId);
        Assert.Equal(
            "'Add' calls 'SaveChangesAsync' in another project, so this saves once per item and causes N+1 database writes. Batch changes and save once after the loop.",
            save.Message);
    }

    [Fact]
    public void ACallSite_IsJudgedByItsOwnRule()
    {
        // The target saves, but the site was only a candidate for LC007, and the other way round.
        Assert.Empty(Resolve(Loop(SqlGetById, rule: "7"), Save(SqlGetById)));
        Assert.Empty(Resolve(Loop(SqlGetById, rule: "10"), Query(SqlGetById)));
        Assert.Empty(Resolve(Loop(SqlGetById, rule: "45"), Query(SqlGetById), Save(SqlGetById)));
    }

    [Theory]
    [InlineData("d", "Warning")]
    [InlineData("e", "Error")]
    [InlineData("w", "Warning")]
    [InlineData("i", "Info")]
    [InlineData("?", "Warning")]
    public void Severity_IsTheOneTheCallSitesProjectConfigured(string exported, string expected)
    {
        var finding = Assert.Single(Resolve(Loop(SqlGetById, severity: exported), Query(SqlGetById)));
        Assert.Equal(expected, finding.Severity);
    }

    [Fact]
    public void InterfaceCall_NeedsEveryImplementationToRunTheQuery()
    {
        const string cached = "M:Shop.Data.CachedCustomers.GetById(System.Int32)";
        var site = Loop(GetById, kind: "a");

        Assert.Single(Resolve(site, Implements(GetById, SqlGetById), Query(SqlGetById)));
        Assert.Single(Resolve(site, Implements(GetById, SqlGetById), Query(SqlGetById), Implements(GetById, cached), Query(cached)));

        // No implementation in any scanned project, one that does not query, and one the compiler could not resolve.
        Assert.Empty(Resolve(site, Query(SqlGetById)));
        Assert.Empty(Resolve(site, Implements(GetById, SqlGetById), Query(SqlGetById), Implements(GetById, cached)));
        Assert.Empty(Resolve(site, Implements(GetById, SqlGetById), Query(SqlGetById), Implements(GetById, "")));

        // A fake in a test project does not count as an implementation.
        Assert.Single(Resolve(site, Implements(GetById, SqlGetById), Query(SqlGetById), Implements(GetById, cached, inTestProject: true)));
        Assert.Empty(Resolve(site, Implements(GetById, cached, inTestProject: true), Query(cached)));
    }

    [Fact]
    public void InterfaceCall_WithTooManyImplementations_IsNotFollowed()
    {
        static IEnumerable<CrossProjectFact> Implementations(int count) =>
            Enumerable.Range(0, count).SelectMany(i => new[] { Implements(GetById, "M:Impl" + i + ".GetById"), Query("M:Impl" + i + ".GetById") });

        var site = Loop(GetById, kind: "a");

        Assert.Single(CrossProjectJoin.Resolve(Implementations(CrossProjectJoin.MaxImplementations).Append(site)));
        Assert.Empty(CrossProjectJoin.Resolve(Implementations(CrossProjectJoin.MaxImplementations + 1).Append(site)));
    }

    [Fact]
    public void VirtualCall_NeedsTheBodyAndEveryOverride()
    {
        const string virtualLoad = "M:Shop.Data.Loader.Load(System.Int32)";
        const string overrideLoad = "M:Shop.Data.FastLoader.Load(System.Int32)";
        var site = Loop(virtualLoad, kind: "v", helper: "Load");

        Assert.Single(Resolve(site, Query(virtualLoad)));
        Assert.Single(Resolve(site, Query(virtualLoad), Implements(virtualLoad, overrideLoad), Query(overrideLoad)));
        Assert.Empty(Resolve(site, Query(virtualLoad), Implements(virtualLoad, overrideLoad)));
        Assert.Empty(Resolve(site, Implements(virtualLoad, overrideLoad), Query(overrideLoad)));

        // An abstract method has no body of its own: only the overrides count.
        Assert.Single(Resolve(Loop(virtualLoad, kind: "a", helper: "Load"), Implements(virtualLoad, overrideLoad), Query(overrideLoad)));
    }

    [Fact]
    public void Call_IsFollowedThroughMethodsThatCallOn_UpToTheDepthLimit()
    {
        static CrossProjectFact[] Chain(int bodies)
        {
            // M1 calls M2 calls ... and the last one runs the query.
            var facts = new List<CrossProjectFact> { Loop("M:M1", helper: "M1") };
            for (var i = 1; i < bodies; i++)
                facts.Add(Calls("M:M" + i, "M:M" + (i + 1)));
            facts.Add(Query("M:M" + bodies, "ToList"));
            return [.. facts];
        }

        var finding = Assert.Single(Resolve(Chain(CrossProjectJoin.MaxDepth)));
        Assert.Contains("'M1' runs 'ToList'", finding.Message);
        Assert.Empty(Resolve(Chain(CrossProjectJoin.MaxDepth + 1)));
    }

    [Fact]
    public void Call_ThroughAService_FollowsItsInterfaceCalls()
    {
        const string service = "M:Shop.Services.CustomerService.Describe(System.Int32)";
        var site = Loop(service, helper: "Describe");

        Assert.Single(Resolve(site, Calls(service, GetById, kind: "a"), Implements(GetById, SqlGetById), Query(SqlGetById)));
        Assert.Empty(Resolve(site, Calls(service, GetById, kind: "a"), Implements(GetById, SqlGetById)));

        // One of the calls the service makes is enough.
        Assert.Single(Resolve(
            site,
            Calls(service, "M:Shop.Services.Clock.Now"),
            Calls(service, GetById, kind: "a"),
            Implements(GetById, SqlGetById),
            Query(SqlGetById)));
    }

    [Fact]
    public void MethodsThatCallEachOther_EndTheWalk()
    {
        Assert.Empty(Resolve(Loop("M:A"), Calls("M:A", "M:B"), Calls("M:B", "M:A")));
        Assert.Empty(Resolve(Loop("M:A"), Calls("M:A", "M:A")));
    }

    [Fact]
    public void DecoratorThatForwardsToItsOwnInterface_HasNoSay()
    {
        const string logging = "M:Shop.Data.LoggingCustomers.GetById(System.Int32)";
        var site = Loop(GetById, kind: "a");
        CrossProjectFact[] decorated =
        [
            site,
            Implements(GetById, logging),
            Calls(logging, GetById, kind: "a"),
            Implements(GetById, SqlGetById),
        ];

        Assert.Single(Resolve([.. decorated, Query(SqlGetById)]));
        Assert.Empty(Resolve(decorated));
    }

    [Fact]
    public void CallBehindACache_IsFollowedForSavesOnly()
    {
        const string cached = "M:Shop.Data.CachedCustomers.GetById(System.Int32)";

        Assert.Empty(Resolve(Loop(cached), Calls(cached, SqlGetById, memoized: true), Query(SqlGetById)));
        Assert.Single(Resolve(Loop(cached), Calls(cached, SqlGetById), Query(SqlGetById)));
        Assert.Single(Resolve(Loop(cached, rule: "10"), Calls(cached, SqlGetById, memoized: true), Save(SqlGetById)));
    }

    [Theory]
    [InlineData("lcx1|Q|M:A|Find", "Q", 2)]
    [InlineData("lcx1|S|M:A|SaveChanges", "S", 2)]
    [InlineData("lcx1|C|M:A|n|M:B|M:B|0", "C", 5)]
    [InlineData("lcx1|I|M:I.M|M:A.M|0", "I", 3)]
    [InlineData("lcx1|I|M:I.M||1", "I", 3)]
    [InlineData("lcx1|L|7|a|M:I.M|M:I.M|foreach||M|d", "L", 8)]
    public void TryParse_ReadsEveryKindOfFact(string message, string kind, int fields)
    {
        Assert.True(CrossProjectJoin.TryParse(message, "a.cs", 3, 5, out var fact));
        Assert.Equal(kind, fact.Kind);
        Assert.Equal(fields, fact.Fields.Count);
        Assert.Equal(("a.cs", 3, 5), (fact.Path, fact.Line, fact.Column));
    }

    [Theory]
    [InlineData("")]
    [InlineData("N+1 Problem")]
    [InlineData("lcx1|Q")]
    [InlineData("lcx2|Q|M:A|Find")]
    [InlineData("lcx1|Q|M:A")]
    [InlineData("lcx1|Q|M:A|Find|extra")]
    [InlineData("lcx1|X|M:A|Find")]
    [InlineData("lcx1|L|7|a|M:I.M|M:I.M|foreach||M")]
    public void TryParse_RejectsAnythingElse(string message)
    {
        Assert.False(CrossProjectJoin.TryParse(message, "a.cs", 1, 1, out _));
    }

    [Fact]
    public void ScannerAndAnalyzer_AgreeOnTheOptionAndTheFactFormat()
    {
        Assert.Equal(CrossProjectFacts.OptionKey, ScanTargets.CrossProjectFactsOption);
        Assert.Equal(CrossProjectFacts.DiagnosticId, CrossProjectJoin.FactRuleId);

        Assert.Contains(CrossProjectFacts.OptionKey + " = true", Lines(ScanTargets.GlobalConfig(crossProjectFacts: true)));
        Assert.DoesNotContain(Lines(ScanTargets.GlobalConfig()), line => line.Contains(CrossProjectFacts.OptionKey, StringComparison.Ordinal));

        var prefix = CrossProjectFacts.Prefix + CrossProjectFacts.Separator;
        Assert.True(CrossProjectJoin.TryParse(prefix + CrossProjectFacts.RunsQuery + "|M:A|Find", "a.cs", 1, 1, out _));
        Assert.True(CrossProjectJoin.TryParse(prefix + CrossProjectFacts.Saves + "|M:A|SaveChanges", "a.cs", 1, 1, out _));
        Assert.True(CrossProjectJoin.TryParse(prefix + CrossProjectFacts.Calls + "|M:A|n|M:B|M:B|0", "a.cs", 1, 1, out _));
        Assert.True(CrossProjectJoin.TryParse(prefix + CrossProjectFacts.Implements + "|M:I.M|M:A.M|0", "a.cs", 1, 1, out _));
        Assert.True(CrossProjectJoin.TryParse(prefix + CrossProjectFacts.PerItemCall + "|7|n|M:A|M:A|foreach||A|d", "a.cs", 1, 1, out _));

        static string[] Lines(string text) => text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
    }

    /// <summary>
    /// A scan in which only the join reports LC007 or LC010 has no compiler log with their metadata, so the report
    /// takes it from the join. It must read the same as when the analyzer reports the rule.
    /// </summary>
    [Fact]
    public void Rules_MatchTheAnalyzersOwnDescriptors()
    {
        AssertSame(new NPlusOneLooperAnalyzer(), "LC007");
        AssertSame(new SaveChangesInLoopAnalyzer(), "LC010");
        Assert.Equal(["LC007", "LC010"], CrossProjectJoin.Rules.Keys.Order());

        static void AssertSame(DiagnosticAnalyzer analyzer, string id)
        {
            var descriptor = Assert.Single(analyzer.SupportedDiagnostics, descriptor => descriptor.Id == id);
            var rule = CrossProjectJoin.Rules[id];

            Assert.Equal(id, rule.Id);
            Assert.Equal(descriptor.Title.ToString(), rule.Title);
            Assert.Equal(descriptor.DefaultSeverity.ToString(), rule.Severity);
            Assert.Equal(descriptor.HelpLinkUri, rule.HelpUri);
            Assert.Equal(descriptor.Description.ToString(), rule.Description);
            Assert.Equal(descriptor.Category, rule.Category);
        }
    }

    [Fact]
    public void TheExporter_IsNotARule()
    {
        var descriptor = Assert.Single(new CrossProjectFactExporter().SupportedDiagnostics);

        Assert.Equal(CrossProjectFacts.DiagnosticId, descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
        Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, descriptor.CustomTags);
        Assert.DoesNotContain(CrossProjectFacts.DiagnosticId, ScanTargets.ReportedRules);
    }
}
