using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingWhereBeforeExecuteDeleteUpdateAnalyzer : DiagnosticAnalyzer
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
        helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC035_MissingWhereBeforeExecuteDeleteUpdate.md");

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

        if (method.ContainingNamespace?.ToString().StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) != true)
            return;

        var receiverType = invocation.GetInvocationReceiver()?.Type;
        if (receiverType?.IsIQueryable() != true && receiverType?.IsDbSet() != true)
            return;

        if (HasWhereInChain(invocation.GetInvocationReceiver()))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
    }

    private static bool HasWhereInChain(IOperation? operation)
    {
        var current = operation;

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                if (invocation.TargetMethod.Name == "Where")
                    return true;

                current = invocation.GetInvocationReceiver();
                continue;
            }

            if (current is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation or IPropertyReferenceOperation)
                return false;

            if (current.Type.IsDbSet() || current.Type.IsIQueryable())
                return false;

            current = current.Parent;
        }

        return false;
    }
}
