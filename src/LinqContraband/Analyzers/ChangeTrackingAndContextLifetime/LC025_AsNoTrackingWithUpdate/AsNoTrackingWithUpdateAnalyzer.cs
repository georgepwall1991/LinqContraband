using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

/// <summary>
/// Analyzes usage of AsNoTracking on entities that are subsequently passed to Update/Remove methods. Diagnostic ID: LC025
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsNoTrackingWithUpdateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC025";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Avoid AsNoTracking with Update/Remove";

    private static readonly LocalizableString MessageFormat =
        "Entity from an 'AsNoTracking' query is passed to '{0}'. This can lead to inefficient updates or tracking issues.";

    private static readonly LocalizableString Description =
        "Passing untracked entities to Update() causes EF Core to mark all properties as modified, leading to inefficient SQL. Remove AsNoTracking() if the entity will be modified.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC025_AsNoTrackingWithUpdate.md");

    private static readonly ImmutableHashSet<string> TrackingMethods = ImmutableHashSet.Create(
        "Update", "UpdateRange", "Remove", "RemoveRange"
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

        if (!TrackingMethods.Contains(method.Name)) return;

        // Verify it's an EF Core method (on DbContext or DbSet)
        if (!method.ContainingType.IsDbContext() && !method.ContainingType.IsDbSet()) return;

        // Check each entity argument
        foreach (var arg in invocation.Arguments)
        {
            var value = arg.Value.UnwrapConversions();

            // If it's a local variable reference
            if (value is ILocalReferenceOperation localRef)
            {
                if (IsFromNoTrackingQuery(localRef.Local, invocation))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, arg.Syntax.GetLocation(), method.Name));
                }
            }
        }
    }

    private bool IsFromNoTrackingQuery(ILocalSymbol local, IOperation currentOperation)
    {
        return IsFromNoTrackingQuery(local, currentOperation, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
    }

    private bool IsFromNoTrackingQuery(ILocalSymbol local, IOperation currentOperation, ISet<ISymbol> visited)
    {
        if (!visited.Add(local)) return false;

        var root = currentOperation.FindOwningExecutableRoot() ?? currentOperation;
        var currentStart = currentOperation.Syntax.SpanStart;
        var bestOriginPosition = -1;
        var bestOriginIsNoTracking = false;

        foreach (var op in EnumerateOperations(root))
        {
            // 1. Standard Assignments
            if (op is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                assignment.Syntax.SpanStart < currentStart &&
                assignment.Syntax.SpanStart >= bestOriginPosition)
            {
                bestOriginPosition = assignment.Syntax.SpanStart;
                bestOriginIsNoTracking = IsNoTrackingSource(assignment.Value, assignment, visited);
            }

            // 2. Variable Declarations
            if (op is IVariableDeclarationOperation decl)
            {
                foreach (var declarator in decl.Declarators)
                {
                    if (SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                        declarator.Initializer != null &&
                        declarator.Syntax.SpanStart < currentStart &&
                        declarator.Syntax.SpanStart >= bestOriginPosition)
                    {
                        bestOriginPosition = declarator.Syntax.SpanStart;
                        bestOriginIsNoTracking = IsNoTrackingSource(declarator.Initializer.Value, declarator.Initializer.Value, visited);
                    }
                }
            }

            // 3. Foreach Loops
            if (op is IForEachLoopOperation forEach)
            {
                // Check if our target 'local' is one of the locals defined by the loop
                if (forEach.Locals.Any(l => SymbolEqualityComparer.Default.Equals(l, local)) &&
                    forEach.Syntax.Span.Contains(currentStart) &&
                    forEach.Collection.Syntax.SpanStart < currentStart &&
                    forEach.Collection.Syntax.SpanStart >= bestOriginPosition)
                {
                    bestOriginPosition = forEach.Collection.Syntax.SpanStart;
                    bestOriginIsNoTracking = IsNoTrackingSource(forEach.Collection, forEach.Collection, visited);
                }
            }
        }

        return bestOriginIsNoTracking;
    }

    private bool IsNoTrackingSource(IOperation source, IOperation boundaryOperation, ISet<ISymbol> visited)
    {
        var unwrapped = source.UnwrapConversions();
        if (IsAsNoTrackingQuery(unwrapped)) return true;

        if (unwrapped is IInvocationOperation invocation &&
            invocation.TargetMethod.Name.IsMaterializerMethod() &&
            invocation.GetInvocationReceiver() is ILocalReferenceOperation receiverLocal)
        {
            return IsFromNoTrackingQuery(
                receiverLocal.Local,
                boundaryOperation,
                new HashSet<ISymbol>(visited, SymbolEqualityComparer.Default));
        }

        return unwrapped is ILocalReferenceOperation localRef &&
               IsFromNoTrackingQuery(
                   localRef.Local,
                   boundaryOperation,
                   new HashSet<ISymbol>(visited, SymbolEqualityComparer.Default));
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation root)
    {
        yield return root;

        foreach (var child in root.ChildOperations)
        {
            foreach (var descendant in EnumerateOperations(child))
                yield return descendant;
        }
    }

    private bool IsAsNoTrackingQuery(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        if (current is IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.Name == "AsNoTracking") return true;

            return HasAsNoTrackingInChain(invocation);
        }

        return false;
    }

    private bool HasAsNoTrackingInChain(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        while (current is IInvocationOperation inv)
        {
            if (inv.TargetMethod.Name == "AsNoTracking") return true;

            var next = inv.GetInvocationReceiver();
            if (next == null) break;
            current = next.UnwrapConversions();
        }
        return false;
    }
}
