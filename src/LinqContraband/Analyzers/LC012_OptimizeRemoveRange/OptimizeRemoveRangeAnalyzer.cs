using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

/// <summary>
/// Analyzes RemoveRange() calls that could be optimized using ExecuteDelete(). Diagnostic ID: LC012
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> RemoveRange() loads all entities into memory before deleting them, which is inefficient
/// for bulk delete operations. ExecuteDelete() performs a direct SQL DELETE statement without loading entities, providing
/// significantly better performance. Note that ExecuteDelete() bypasses change tracking and client-side cascades, so verify
/// that these behaviors are not required before applying this optimization.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class OptimizeRemoveRangeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC012";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Optimize: Use ExecuteDelete() instead of RemoveRange()";

    private static readonly LocalizableString MessageFormat =
        "Call to '{0}' can be replaced with 'ExecuteDelete()' for better performance. Warning: ExecuteDelete bypasses change tracking and cascades.";

    private static readonly LocalizableString Description =
        "Using RemoveRange() fetches entities into memory before deleting them. ExecuteDelete() performs a direct SQL DELETE statement, which is much faster for bulk operations. Be aware that ExecuteDelete() does not respect change tracking or client-side cascades.";

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

        if (method.Name != "RemoveRange") return;

        // Check if it's DbSet.RemoveRange or DbContext.RemoveRange
        var type = method.ContainingType;
        if (!IsDbSet(type) && !IsDbContext(type)) return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
    }

    private bool IsDbSet(ITypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbSet" &&
                (current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore" ||
                 current.ContainingNamespace?.ToString() == "TestNamespace")) // Support mock in tests
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private bool IsDbContext(ITypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbContext" &&
                (current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore" ||
                 current.ContainingNamespace?.ToString() == "TestNamespace")) // Support mock in tests
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
