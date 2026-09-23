using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC042_MissingQueryTags;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingQueryTagsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC042";
    private const string Category = "Performance";
    private const int DefaultThreshold = 3;
    private const string ThresholdKey = "dotnet_code_quality.LC042.query_operator_threshold";

    private static readonly LocalizableString Title = "Complex query should be tagged";

    private static readonly LocalizableString MessageFormat =
        "Query ending in '{0}' has complexity {1} but no TagWith/TagWithCallSite; tag it so its SQL can be traced back to this code";

    private static readonly LocalizableString Description =
        "Complex EF Core queries are easier to trace in logs, profilers, and query stores when they are tagged. The rule scores the query's shape operators (joins, groupings, and SelectMany count twice; tracking and split-query options do not count) and reports untagged queries that reach the configured threshold.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC042_MissingQueryTags.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsTerminal(invocation.TargetMethod))
            return;

        // A query inside an expression-tree lambda is a subquery of the outer query, which is tagged (or not) as a whole.
        if (IsInsideExpressionTree(invocation))
            return;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null)
            return;

        if (!TryScoreChain(receiver, out var score))
            return;

        if (HasLambdaArgument(invocation))
            score++;

        var threshold = GetThreshold(context.Options.AnalyzerConfigOptionsProvider, invocation.Syntax.SyntaxTree);
        if (score < threshold)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, GetLocation(invocation), invocation.TargetMethod.Name, score));
    }

    private static Location GetLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
            return memberAccess.Name.GetLocation();

        return invocation.Syntax.GetLocation();
    }

    private static int GetThreshold(AnalyzerConfigOptionsProvider provider, SyntaxTree syntaxTree)
    {
        var options = provider.GetOptions(syntaxTree);
        if (options.TryGetValue(ThresholdKey, out var value) && int.TryParse(value, out var parsed) && parsed > 0)
            return parsed;

        return DefaultThreshold;
    }
}
