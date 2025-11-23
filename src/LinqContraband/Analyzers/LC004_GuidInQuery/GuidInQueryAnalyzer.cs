using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_GuidInQuery;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GuidInQueryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC004";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Avoid Guid generation inside IQueryable";

    private static readonly LocalizableString MessageFormat =
        "Guid generation '{0}' inside IQueryable may fail translation or cause client-side evaluation";

    private static readonly LocalizableString Description =
        "Generate the Guid outside the query and pass it as a parameter.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name == "NewGuid" &&
            method.ContainingType.Name == "Guid" &&
            method.ContainingNamespace?.ToString() == "System")
            if (IsInsideIQueryable(invocation))
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), "Guid.NewGuid()"));
    }

    private void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var type = creation.Type;

        if (type != null &&
            type.Name == "Guid" &&
            type.ContainingNamespace?.ToString() == "System")
            if (IsInsideIQueryable(creation))
                context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation(), "new Guid(...)"));
    }

    private bool IsInsideIQueryable(IOperation operation)
    {
        var parent = operation.Parent;
        IOperation? lambda = null;

        while (parent != null)
        {
            if (parent.Kind == OperationKind.AnonymousFunction)
            {
                lambda = parent;
                break;
            }

            parent = parent.Parent;
        }

        if (lambda == null) return false;

        var current = lambda.Parent;
        while (current != null)
        {
            if (current is IInvocationOperation queryInvocation)
            {
                var type = queryInvocation.Instance?.Type;
                if (type == null && queryInvocation.Arguments.Length > 0)
                    type = queryInvocation.Arguments[0].Value.Type;

                if (type.IsIQueryable()) return true;
            }

            current = current.Parent;
        }

        return false;
    }
}