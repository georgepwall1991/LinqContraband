namespace LinqContraband.Scan;

/// <summary>One fact the analyzer's cross-project exporter wrote into a project's compiler log.</summary>
internal sealed record CrossProjectFact(string Kind, IReadOnlyList<string> Fields, string Path, int Line, int Column);

/// <summary>
/// Follows a loop's call from one project into another. A compiler sees a referenced project as an assembly
/// without method bodies, so LC007 and LC010 stop at the project boundary during a build. In a scan build each
/// project also logs facts: which methods run a query or save, which methods of other projects they call, which
/// method implements which interface method, and which calls run once per item. The scanner has every project's
/// log, so it can join them: a per-item call is a finding when every body it can reach, in any scanned project,
/// runs a query (LC007) or saves (LC010).
/// </summary>
internal static class CrossProjectJoin
{
    /// <summary>The diagnostic ID the exporter reports facts under. It is never a finding.</summary>
    public const string FactRuleId = "LCX001";

    private const string Prefix = "lcx1";

    /// <summary>How many calls from project to project a per-item call is followed through.</summary>
    internal const int MaxDepth = 4;

    /// <summary>An interface or virtual call with more implementations than this is not followed.</summary>
    internal const int MaxImplementations = 8;

    private const string DocumentationSiteUri = ScanReport.DocumentationSiteUri;

    /// <summary>
    /// The two rules the join reports, for a scan in which no project reported them directly (the compiler logs
    /// then carry no metadata for them). A test keeps these equal to the analyzers' own descriptors.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RuleInfo> Rules = new Dictionary<string, RuleInfo>(StringComparer.Ordinal)
    {
        ["LC007"] = new RuleInfo(
            "LC007",
            "N+1 Problem: Database execution inside loop",
            "Warning",
            DocumentationSiteUri + "LC007_NPlusOneLooper.html",
            "Running EF Core database execution inside a loop causes one database operation per iteration. This includes Find/FindAsync, explicit loading, query materializers, aggregates, and EF set-based executors when the query source is provably EF-backed.",
            "Performance"),
        ["LC010"] = new RuleInfo(
            "LC010",
            "N+1 Write Problem: SaveChanges inside loop",
            "Warning",
            DocumentationSiteUri + "LC010_SaveChangesInLoop.html",
            "Calling SaveChanges or SaveChangesAsync inside a loop results in a separate database transaction and roundtrip for every iteration. This significantly degrades performance. Add all entities to the context and call SaveChanges once after the loop.",
            "Performance"),
    };

    /// <summary>Parses the message of a <see cref="FactRuleId"/> result. Anything else is not a fact.</summary>
    public static bool TryParse(string message, string path, int line, int column, out CrossProjectFact fact)
    {
        fact = null!;
        var parts = message.Split('|');
        if (parts.Length < 3 || parts[0] != Prefix)
            return false;

        var expected = parts[1] switch
        {
            "Q" or "S" => 2,
            "C" => 5,
            "I" => 3,
            "L" => 8,
            _ => -1,
        };
        if (parts.Length - 2 != expected)
            return false;

        fact = new CrossProjectFact(parts[1], parts[2..], path, line, column);
        return true;
    }

    /// <summary>The LC007 and LC010 findings the facts prove, one per per-item call site.</summary>
    public static IReadOnlyList<Finding> Resolve(IEnumerable<CrossProjectFact> facts)
    {
        var graph = new Graph();
        var callSites = new List<CrossProjectFact>();
        foreach (var fact in facts)
        {
            var fields = fact.Fields;
            switch (fact.Kind)
            {
                case "Q":
                    graph.Queries.TryAdd(fields[0], fields[1]);
                    break;
                case "S":
                    graph.Saves.TryAdd(fields[0], fields[1]);
                    break;
                case "C":
                    if (!graph.Calls.TryGetValue(fields[0], out var calls))
                        graph.Calls[fields[0]] = calls = [];
                    var call = new Call(fields[1], fields[2], fields[3], fields[4] == "1");
                    if (!calls.Contains(call))
                        calls.Add(call);
                    break;
                case "I":
                    // A test project's fakes implement the same interfaces as the real repositories.
                    if (fields[2] == "1")
                        break;
                    if (!graph.Implementations.TryGetValue(fields[0], out var implementations))
                        graph.Implementations[fields[0]] = implementations = new HashSet<string>(StringComparer.Ordinal);
                    implementations.Add(fields[1]);
                    break;
                case "L":
                    callSites.Add(fact);
                    break;
            }
        }

        var findings = new List<Finding>();
        foreach (var site in callSites)
        {
            var fields = site.Fields;
            var isQuery = fields[0] == "7";
            if (!isQuery && fields[0] != "10")
                continue;

            var executions = isQuery ? graph.Queries : graph.Saves;
            var execution = graph.ResolveCall(
                new Call(fields[1], fields[2], fields[3], Memoized: false),
                executions,
                followMemoized: !isQuery,
                MaxDepth,
                new HashSet<string>(StringComparer.Ordinal));
            if (execution is null)
                continue;

            var ruleId = isQuery ? "LC007" : "LC010";
            var severity = fields[7] switch
            {
                "e" => "Error",
                "w" => "Warning",
                "i" => "Info",
                _ => Rules[ruleId].Severity,
            };
            findings.Add(new Finding(ruleId, severity, Message(isQuery, fields[6], execution, fields[4], fields[5]), site.Path, site.Line, site.Column));
        }

        return findings;
    }

    private static string Message(bool isQuery, string helper, string execution, string loopKind, string operatorName)
    {
        if (!isQuery)
            return $"'{helper}' calls '{execution}' in another project, so this saves once per item and causes N+1 database writes. Batch changes and save once after the loop.";

        return loopKind == "lambda" && operatorName.Length > 0
            ? $"'{helper}' runs '{execution}' in another project for every element '{operatorName}' processes, causing N+1 database operations. Fetch data in bulk or eager load before iterating."
            : $"'{helper}' runs '{execution}' in another project on every iteration of the loop, causing N+1 database operations. Fetch data in bulk or eager load before the loop.";
    }

    /// <summary>A call to a method whose body is in another project: the callee kind, its ID, and the ID its implementations are recorded under.</summary>
    private sealed record Call(string Kind, string Callee, string Root, bool Memoized);

    private sealed class Graph
    {
        public Dictionary<string, string> Queries { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Saves { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<Call>> Calls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, HashSet<string>> Implementations { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// What the call executes once per call, or null. Every body the call can reach must execute: the one
        /// method of a direct call, every implementation of an interface or abstract method, a virtual method and
        /// every override. A body that is on the path already (a decorator forwarding to its own interface method)
        /// has no say.
        /// </summary>
        public string? ResolveCall(
            Call call,
            Dictionary<string, string> executions,
            bool followMemoized,
            int depth,
            HashSet<string> path)
        {
            var targets = new List<string>();
            if (call.Kind is "n" or "v")
                targets.Add(call.Callee);
            if (call.Kind is "a" or "v" && Implementations.TryGetValue(call.Root, out var implementations))
                targets.AddRange(implementations.Where(implementation => implementation != call.Callee).OrderBy(id => id, StringComparer.Ordinal));

            if (targets.Count == 0 || targets.Count > MaxImplementations)
                return null;

            string? execution = null;
            foreach (var target in targets)
            {
                if (path.Contains(target))
                    continue;

                // An implementation the compiler could not resolve is recorded without an ID.
                var found = target.Length == 0 ? null : Executes(target, executions, followMemoized, depth, path);
                if (found is null)
                    return null;

                execution ??= found;
            }

            return execution;
        }

        private string? Executes(
            string method,
            Dictionary<string, string> executions,
            bool followMemoized,
            int depth,
            HashSet<string> path)
        {
            if (executions.TryGetValue(method, out var execution))
                return execution;

            if (depth <= 1 || !Calls.TryGetValue(method, out var calls) || !path.Add(method))
                return null;

            try
            {
                foreach (var call in calls)
                {
                    // A method that consults a cache before calling on usually does not reach the database.
                    if (call.Memoized && !followMemoized)
                        continue;

                    if (ResolveCall(call, executions, followMemoized, depth - 1, path) is { } found)
                        return found;
                }

                return null;
            }
            finally
            {
                path.Remove(method);
            }
        }
    }
}
