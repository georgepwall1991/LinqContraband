using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

/// <summary>
/// Analyzes IQueryable operations (Skip, Last, Chunk) that require ordering but are called on unordered sequences. Diagnostic ID: LC015
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Operations like Skip, Last, and Chunk depend on a specific ordering to produce deterministic
/// results. Without an explicit OrderBy or OrderByDescending, the database may return results in any order, leading to
/// non-deterministic behavior in pagination, retrieval of last elements, or chunking operations. This can cause unpredictable
/// application behavior and difficult-to-reproduce bugs.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingOrderByAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC015";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Ensure OrderBy before Skip/Last/Chunk";

    private static readonly LocalizableString MessageFormat =
        "The method '{0}' is called on an unordered IQueryable. Call 'OrderBy' or 'OrderByDescending' first to ensure deterministic results.";

    private static readonly LocalizableString Description =
        "Pagination and Last operations on unordered IQueryables are non-deterministic.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "Skip", "Last", "LastOrDefault", "Chunk"
    );

    private static readonly ImmutableHashSet<string> OrderingMethods = ImmutableHashSet.Create(
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!TargetMethods.Contains(method.Name)) return;

        // Check if it's IQueryable
        // Since these are extension methods, the instance is usually null (static call), 
        // and the first argument is the 'this' parameter.
        // But for IInvocationOperation, the 'Instance' property handles instance methods, 
        // and arguments handle extension methods. 

        IOperation? receiver = null;
        if (method.IsExtensionMethod)
        {
            if (invocation.Arguments.Length > 0) receiver = invocation.Arguments[0].Value;
        }
        else
        {
            receiver = invocation.Instance;
        }

        if (receiver == null) return;

        if (!receiver.Type.IsIQueryable()) return;

        // Walk up the chain to find OrderBy
        if (!HasOrderByUpstream(receiver))
        {
            var location = invocation.Syntax.GetLocation();
            if (invocation.Syntax is InvocationExpressionSyntax invocationSyntax &&
                invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
                location = memberAccess.Name.GetLocation();

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, method.Name));
        }
    }

    private bool HasOrderByUpstream(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        while (current != null)
            if (current is IInvocationOperation inv)
            {
                var method = inv.TargetMethod;

                if (OrderingMethods.Contains(method.Name) && method.ReturnType.IsIQueryable()) return true;

                // Move "upstream" (to the previous call in the chain)
                if (method.IsExtensionMethod && inv.Arguments.Length > 0)
                    current = inv.Arguments[0].Value.UnwrapConversions();
                else if (!method.IsStatic && inv.Instance != null)
                    current = inv.Instance.UnwrapConversions();
                else
                    // Unknown structure or broken chain
                    return false;
            }
            else
            {
                // If we hit a variable reference, property reference, etc.
                // For now, we assume if we hit the "source" (DbSet) without seeing OrderBy, it's missing.
                // Edge case: The variable itself might be an OrderedQueryable.
                // Check if the type itself implies ordering (IOrderedQueryable).
                // Note: IOrderedQueryable inherits IQueryable.
                if (current.Type != null && IsOrderedQueryable(current.Type)) return true;

                return false;
            }

        return false;
    }

    private bool IsOrderedQueryable(ITypeSymbol type)
    {
        // Check if it implements IOrderedQueryable
        if (type.Name == "IOrderedQueryable" && type.ContainingNamespace?.ToString() == "System.Linq") return true;
        foreach (var i in type.AllInterfaces)
            if (i.Name == "IOrderedQueryable" && i.ContainingNamespace?.ToString() == "System.Linq")
                return true;
        return false;
    }
}
