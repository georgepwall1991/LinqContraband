using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC036_DbContextCapturedAcrossThreads;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class DbContextCapturedAcrossThreadsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC036";
    private const string Category = "Safety";
    private static readonly LocalizableString Title = "DbContext captured by thread work item";

    private static readonly LocalizableString MessageFormat =
        "DbContext symbol '{0}' is captured inside '{1}', which can run on a different thread";

    private static readonly LocalizableString Description =
        "DbContext instances are not thread-safe. Capturing them in Task.Run, Parallel.ForEach, or ThreadPool.QueueUserWorkItem can cause race conditions and invalid usage.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC036_DbContextCapturedAcrossThreads.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsTargetThreadApi(method))
            return;

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel == null)
            return;

        foreach (var argument in invocation.Arguments)
        {
            if (TryFindCapturedDbContext(argument.Value.Syntax, semanticModel, out var symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), symbol.Name, method.Name));
                return;
            }
        }
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (!IsTargetThreadObject(creation.Constructor?.ContainingType))
            return;

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel == null)
            return;

        foreach (var argument in creation.Arguments)
        {
            if (TryFindCapturedDbContext(argument.Value.Syntax, semanticModel, out var symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation(), symbol.Name, creation.Type?.Name ?? "thread work item"));
                return;
            }
        }
    }

    private static bool IsTargetThreadApi(IMethodSymbol method)
    {
        return (method.Name == "Run" &&
                method.ContainingType.Name == "Task" &&
                method.ContainingNamespace?.ToString() == "System.Threading.Tasks") ||
               (method.Name == "StartNew" &&
                method.ContainingType.Name == "TaskFactory" &&
                method.ContainingNamespace?.ToString() == "System.Threading.Tasks") ||
               (method.Name is "For" or "ForEach" or "Invoke" &&
                method.ContainingType.Name == "Parallel" &&
                method.ContainingNamespace?.ToString() == "System.Threading.Tasks") ||
               (method.Name == "QueueUserWorkItem" &&
                method.ContainingType.Name == "ThreadPool" &&
                method.ContainingNamespace?.ToString() == "System.Threading");
    }

    private static bool IsTargetThreadObject(INamedTypeSymbol? type)
    {
        if (type == null)
            return false;

        var ns = type.ContainingNamespace?.ToString();
        return ns == "System.Threading" && type.Name is "Thread" or "Timer";
    }
}
