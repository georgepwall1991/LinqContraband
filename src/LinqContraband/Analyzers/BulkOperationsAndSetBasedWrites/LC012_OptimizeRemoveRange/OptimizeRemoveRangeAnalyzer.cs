using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC012_OptimizeRemoveRange.md");

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

        if (HasSubsequentSaveChangesInvocation(invocation, context.CancellationToken))
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

    private static bool HasSubsequentSaveChangesInvocation(IInvocationOperation invocation, CancellationToken cancellationToken)
    {
        var root = invocation.FindOwningExecutableRoot();
        if (root == null)
            return false;

        var removeRangeReceiver = GetRemoveRangeContextReceiver(invocation);

        foreach (var candidate in root.Descendants().OfType<IInvocationOperation>())
        {
            if (candidate.Syntax.SpanStart <= invocation.Syntax.SpanStart ||
                !IsSaveChangesMethod(candidate.TargetMethod))
            {
                continue;
            }

            // A save in a mutually exclusive if/else or switch branch can never run after
            // this RemoveRange in the same execution, so it is not committing these
            // removals. try/catch deliberately still suppresses: the try may throw after
            // RemoveRange ran, and a catch-side save would commit the pending removals.
            if (AreMutuallyExclusiveBranches(invocation.Syntax, candidate.Syntax))
                continue;

            // A save on a provably different context never commits these removals. Proof
            // requires both receivers to resolve — through single-assignment alias chains —
            // to two DIFFERENT freshly-created locals; parameters, fields, and anything
            // reassigned could alias the same instance and stay conservatively suppressing.
            if (removeRangeReceiver != null &&
                TryResolveFreshContextLocal(removeRangeReceiver, root, cancellationToken, out var removeLocal) &&
                TryResolveFreshContextLocal(candidate.Instance, root, cancellationToken, out var saveLocal) &&
                !SymbolEqualityComparer.Default.Equals(removeLocal, saveLocal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// The context that owns the pending removals: the receiver itself for
    /// DbContext.RemoveRange, or the DbSet member's instance for DbSet.RemoveRange. A DbSet
    /// arriving as a local/parameter has no visible owning context and resolves null.
    /// </summary>
    private static IOperation? GetRemoveRangeContextReceiver(IInvocationOperation invocation)
    {
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null)
            return null;

        if (invocation.TargetMethod.ContainingType.IsDbContext())
            return receiver;

        return receiver is IMemberReferenceOperation memberReference ? memberReference.Instance : null;
    }

    /// <summary>
    /// Follows single-assignment local alias chains (var db2 = db1) back to the local whose
    /// one assignment is an object creation. Only such locals prove a distinct instance;
    /// anything else (parameters, fields, factory results, reassigned locals) could alias.
    /// </summary>
    private static bool TryResolveFreshContextLocal(
        IOperation? receiver,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out ILocalSymbol? creationLocal)
    {
        creationLocal = null;
        var current = receiver?.UnwrapConversions();

        for (var depth = 0; depth < 16; depth++)
        {
            if (current is not ILocalReferenceOperation localReference)
                return false;

            var assignments = LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken);
            if (assignments.Count != 1)
                return false;

            var value = assignments[0].Value.UnwrapConversions();
            if (value is IObjectCreationOperation)
            {
                creationLocal = localReference.Local;
                return true;
            }

            current = value;
        }

        return false;
    }

    private static bool AreMutuallyExclusiveBranches(SyntaxNode left, SyntaxNode right)
    {
        foreach (var ifStatement in left.AncestorsAndSelf().OfType<IfStatementSyntax>())
        {
            if (!ifStatement.Span.Contains(right.SpanStart))
                continue;

            var leftBranch = GetContainingIfBranch(ifStatement, left);
            var rightBranch = GetContainingIfBranch(ifStatement, right);

            if (leftBranch != null && rightBranch != null && leftBranch != rightBranch)
                return true;
        }

        foreach (var switchStatement in left.AncestorsAndSelf().OfType<SwitchStatementSyntax>())
        {
            if (!switchStatement.Span.Contains(right.SpanStart))
                continue;

            // goto case / goto default lets one section flow into another, so sections of
            // a switch containing any such jump are not provably exclusive.
            if (switchStatement.DescendantNodes().Any(node =>
                    node.IsKind(SyntaxKind.GotoCaseStatement) || node.IsKind(SyntaxKind.GotoDefaultStatement)))
            {
                continue;
            }

            var leftSection = GetContainingSwitchSection(switchStatement, left);
            var rightSection = GetContainingSwitchSection(switchStatement, right);

            if (leftSection != null && rightSection != null && leftSection != rightSection)
                return true;
        }

        return false;
    }

    private static SyntaxNode? GetContainingIfBranch(IfStatementSyntax ifStatement, SyntaxNode node)
    {
        if (ifStatement.Statement.Span.Contains(node.Span))
            return ifStatement.Statement;

        var elseClause = ifStatement.Else;
        return elseClause != null && elseClause.Span.Contains(node.Span) ? elseClause : null;
    }

    private static SwitchSectionSyntax? GetContainingSwitchSection(SwitchStatementSyntax switchStatement, SyntaxNode node)
    {
        foreach (var section in switchStatement.Sections)
        {
            if (section.Span.Contains(node.Span))
                return section;
        }

        return null;
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
