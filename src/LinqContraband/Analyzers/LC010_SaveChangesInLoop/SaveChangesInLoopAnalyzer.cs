using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SaveChangesInLoopAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC010";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "N+1 Write Problem: SaveChanges inside loop";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' inside a loop causes N+1 database writes. Batch changes and call SaveChanges once after the loop.";

    private static readonly LocalizableString Description =
        "Calling SaveChanges or SaveChangesAsync inside a loop results in a separate database transaction and roundtrip for every iteration. This significantly degrades performance. Add all entities to the context and call SaveChanges once after the loop.";

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
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // 1. Check method name
        if (method.Name != "SaveChanges" && method.Name != "SaveChangesAsync")
            return;

        // 2. Check containing type (DbContext)
        if (!IsDbContext(method.ContainingType))
            return;

        // 3. Check if inside a loop
        if (IsInsideLoop(invocation))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    private bool IsDbContext(ITypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbContext" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private bool IsInsideLoop(IOperation operation)
    {
        return operation.IsInsideLoop();
    }
}
