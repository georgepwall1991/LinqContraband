using System.Collections.Immutable;
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
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC035_MissingWhereBeforeExecuteDeleteUpdate.md");

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "ExecuteDelete",
        "ExecuteDeleteAsync",
        "ExecuteUpdate",
        "ExecuteUpdateAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!TargetMethods.Contains(method.Name))
            return;

        if (!IsEntityFrameworkCoreNamespace(method.ContainingNamespace))
            return;

        var receiverType = invocation.GetInvocationReceiver()?.Type;
        if (receiverType?.IsIQueryable() != true && receiverType?.IsDbSet() != true)
            return;

        if (HasWhereInChain(invocation.GetInvocationReceiver(), context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
    }

    private static bool IsEntityFrameworkCoreNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }
}
