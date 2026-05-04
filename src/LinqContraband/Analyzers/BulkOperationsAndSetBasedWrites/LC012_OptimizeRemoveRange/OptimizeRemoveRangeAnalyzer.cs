using System;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
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
public sealed class OptimizeRemoveRangeAnalyzer : DiagnosticAnalyzer
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

        if (!HasExecuteDeleteSupport(context.Compilation)) return;

        // Check if it's DbSet.RemoveRange or DbContext.RemoveRange
        // Use shared extension methods for type checking
        var type = method.ContainingType;
        if (!type.IsDbSet() && !type.IsDbContext()) return;

        if (!HasQueryableDeleteSource(invocation))
            return;

        if (HasSubsequentSaveChangesInvocation(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
    }

    private static bool HasQueryableDeleteSource(IInvocationOperation invocation)
    {
        if (invocation.Arguments.Length != 1)
            return false;

        var deleteSource = invocation.Arguments.FirstOrDefault()?.Value.UnwrapConversions();
        return deleteSource?.Type.IsIQueryable() == true || deleteSource?.Type.IsDbSet() == true;
    }

    private static bool HasExecuteDeleteSupport(Compilation compilation)
    {
        if (HasExecuteDeleteMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")) ||
            HasExecuteDeleteMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")))
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteDelete", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Any(IsExecuteDeleteLikeMethod);
    }

    private static bool HasExecuteDeleteMethod(INamedTypeSymbol? type)
    {
        return type?.GetMembers("ExecuteDelete").OfType<IMethodSymbol>().Any(IsExecuteDeleteLikeMethod) == true;
    }

    private static bool IsExecuteDeleteLikeMethod(IMethodSymbol method)
    {
        if (!method.IsExtensionMethod || method.Parameters.Length == 0)
            return false;

        if (!IsEntityFrameworkCoreNamespace(method.ContainingNamespace))
            return false;

        return method.Parameters[0].Type.IsIQueryable();
    }

    private static bool HasSubsequentSaveChangesInvocation(IInvocationOperation invocation)
    {
        var root = invocation.FindOwningExecutableRoot();
        if (root == null)
            return false;

        return root.Descendants()
            .OfType<IInvocationOperation>()
            .Any(candidate => candidate.Syntax.SpanStart > invocation.Syntax.SpanStart &&
                              IsSaveChangesMethod(candidate.TargetMethod));
    }

    private static bool IsSaveChangesMethod(IMethodSymbol method)
    {
        return method.Name is "SaveChanges" or "SaveChangesAsync" &&
               method.ContainingType.IsDbContext();
    }

    private static bool IsEntityFrameworkCoreNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) == true;
    }
}
