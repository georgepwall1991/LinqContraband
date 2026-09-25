using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingWhereBeforeExecuteDeleteUpdateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC035";
    private const string Category = "Safety";
    private static readonly LocalizableString Title = "Missing Where before bulk execute";

    private static readonly LocalizableString MessageFormat =
        "Call to '{0}' can affect the entire query because no Where() filter is present";

    private static readonly LocalizableString Description =
        "ExecuteDelete/ExecuteUpdate should usually follow a filter. A missing Where() can delete or update every row in the table.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC035_MissingWhereBeforeExecuteDeleteUpdate.html");

    // Reported once the whole compilation is seen: a helper's query parameter is unfiltered at one of
    // its call sites, or the helper's callers cannot all be seen.
    private static readonly DiagnosticDescriptor CallerFlowRule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC035_MissingWhereBeforeExecuteDeleteUpdate.html",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "ExecuteDelete",
        "ExecuteDeleteAsync",
        "ExecuteUpdate",
        "ExecuteUpdateAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, CallerFlowRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var callerFlow = new CallerFlowState();
            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, callerFlow),
                OperationKind.Invocation);
            compilationContext.RegisterOperationAction(callerFlow.RecordMethodReference, OperationKind.MethodReference);
            compilationContext.RegisterCompilationEndAction(callerFlow.Report);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, CallerFlowState callerFlow)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!TargetMethods.Contains(method.Name))
        {
            callerFlow.RecordCallSites(invocation, context.CancellationToken);
            return;
        }

        if (!IsEntityFrameworkCoreNamespace(method.ContainingNamespace))
            return;

        var receiverType = invocation.GetInvocationReceiver()?.Type;
        if (receiverType?.IsIQueryable() != true && receiverType?.IsDbSet() != true)
            return;

        var state = new LocalFlowState(trackParameters: true);
        if (!HasWhereInChain(invocation.GetInvocationReceiver(), context.CancellationToken, state))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
            return;
        }

        // Filtered only if the helper's callers pass filtered queries: decide at compilation end.
        if (state.ParameterRoots!.Count > 0)
            callerFlow.AddExecute(invocation.Syntax.GetLocation(), method.Name, state.ParameterRoots);
    }

    private static bool IsEntityFrameworkCoreNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }
}
