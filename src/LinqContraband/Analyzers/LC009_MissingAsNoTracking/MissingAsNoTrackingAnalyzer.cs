using System;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// Analyzes Entity Framework Core queries to detect missing AsNoTracking() calls in read-only operations. Diagnostic ID: LC009
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> When querying entities for read-only operations, EF Core creates change tracking snapshots
/// by default, which consumes memory and CPU time. Using AsNoTracking() prevents unnecessary tracking overhead and improves
/// performance in scenarios where entities are not being modified.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingAsNoTrackingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC009";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Performance: Missing AsNoTracking() in Read-Only path";

    private static readonly LocalizableString MessageFormat =
        "Method '{0}' appears to be read-only but returns tracked entities. Use AsNoTracking() to avoid tracking overhead.";

    private static readonly LocalizableString Description =
        "When querying entities for read-only operations, use .AsNoTracking() to prevent EF Core from creating unnecessary change tracking snapshots. This reduces memory usage and CPU time.";

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

        // 1. Must be a materializer (ToList, etc.) (Find/FindAsync ignored because tracking mods are ignored there)
        if (!IsMaterializer(method)) return;

        // 2. Analyze the query chain
        var analysis = AnalyzeQueryChain(invocation);
        if (!analysis.IsEfQuery) return;
        if (analysis.HasAsNoTracking || analysis.HasAsTracking) return;
        if (analysis.HasSelect) return; // Projections don't track

        // 3. Analyze the containing method for writes (SaveChanges)
        if (HasWriteOperations(context.Operation)) return;

        // If we got here: It's an EF query returning entities, no tracking mod, no writes in method.
        var containingMethodName = GetContainingMethodName(context.Operation);

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), containingMethodName));
    }

    private static bool IsMaterializer(IMethodSymbol method)
    {
        // Find/FindAsync have no effect with AsNoTracking, so skip them
        if (method.Name.StartsWith("Find", StringComparison.Ordinal)) return false;

        // Use shared extension method for materializer check
        return method.Name.IsMaterializerMethod();
    }

    private ChainAnalysis AnalyzeQueryChain(IInvocationOperation invocation)
    {
        var result = new ChainAnalysis();
        var current = invocation.GetInvocationReceiver();

        while (current != null)
        {
            // Unwrap implicit conversions
            current = current!.UnwrapConversions();

            if (current is IInvocationOperation prevInvocation)
            {
                var method = prevInvocation.TargetMethod;

                if (method.Name == "AsNoTracking" || method.Name == "AsNoTrackingWithIdentityResolution")
                    result.HasAsNoTracking = true;
                if (method.Name == "AsTracking") result.HasAsTracking = true;
                if (method.Name == "Select")
                    result.HasSelect = true; // Any projection invalidates need for AsNoTracking check

                // Move up
                current = prevInvocation.Instance ??
                          (prevInvocation.Arguments.Length > 0 ? prevInvocation.Arguments[0].Value : null);
            }
            else if (current is IPropertyReferenceOperation propRef)
            {
                // Check if it's a DbSet
                if (propRef.Type.IsDbSet()) result.IsEfQuery = true;
                // End of chain
                break;
            }
            else if (current is IFieldReferenceOperation fieldRef)
            {
                if (fieldRef.Type.IsDbSet()) result.IsEfQuery = true;
                break;
            }
            else if (current is IParameterReferenceOperation paramRef)
            {
                // Only flag DbSet parameters, not generic IQueryable<T> parameters.
                // IQueryable<T> parameters are commonly used in repository patterns where
                // the caller controls tracking behavior. We can't determine from inside
                // this method whether the caller intends to use the result for writes.
                // Flagging IQueryable params creates false positives in:
                //   - Repository GetAll() returning IQueryable for flexible querying
                //   - Methods that filter queries before passing to Add/Update contexts
                // DbSet is a concrete EF type that we can confidently flag.
                if (paramRef.Type.IsDbSet())
                    result.IsEfQuery = true;
                break;
            }
            else if (current is ILocalReferenceOperation localRef)
            {
                // Local typed as DbSet/IQueryable counts as EF query source
                if (localRef.Type.IsDbSet() || localRef.Type.IsIQueryable())
                    result.IsEfQuery = true;
                break;
            }
            else
            {
                if (current.Type.IsDbSet()) result.IsEfQuery = true;
                break;
            }
        }

        return result;
    }

    private bool HasWriteOperations(IOperation operation)
    {
        // Find method body
        var root = operation;
        while (root.Parent != null)
        {
            if (root is IMethodBodyOperation ||
                root is ILocalFunctionOperation ||
                root is IAnonymousFunctionOperation)
                break;
            root = root.Parent;
        }

        if (root == null) return false;

        // Walk the body to find SaveChanges
        var hasWrite = false;
        foreach (var descendant in root.Descendants())
            if (descendant is IInvocationOperation inv)
            {
                if (inv.TargetMethod.Name == "SaveChanges" ||
                    inv.TargetMethod.Name == "SaveChangesAsync")
                {
                    hasWrite = true;
                    break;
                }

                // Also check for Add, Update, Remove on DbSet?
                // Ideally yes, but SaveChanges is the commit point. 
                // If they Add but don't SaveChanges in this method, tracking is still technically needed?
                // Yes, because the Context tracks it.
                // So if they call Add(), we should assume tracking is needed.
                var name = inv.TargetMethod.Name;
                var receiverType =
                    inv.Instance?.Type ?? (inv.Arguments.Length > 0 ? inv.Arguments[0].Value.Type : null);

                if ((name == "Add" || name == "AddAsync" ||
                     name == "Update" || name == "Remove" || name == "RemoveRange" || name == "AddRange" ||
                     name == "AddRangeAsync") &&
                    (receiverType?.IsDbSet() == true || receiverType?.IsDbContext() == true))
                {
                    hasWrite = true;
                    break;
                }
            }

        return hasWrite;
    }

    private string GetContainingMethodName(IOperation operation)
    {
        var sym = operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart);
        return sym?.Name ?? "Unknown";
    }

    private class ChainAnalysis
    {
        public bool IsEfQuery { get; set; }
        public bool HasAsNoTracking { get; set; }
        public bool HasAsTracking { get; set; }
        public bool HasSelect { get; set; }
    }
}
