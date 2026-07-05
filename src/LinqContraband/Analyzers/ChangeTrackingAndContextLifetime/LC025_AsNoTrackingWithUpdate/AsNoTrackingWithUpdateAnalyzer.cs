using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        context.RegisterOperationAction(AnalyzeSimpleAssignment, OperationKind.SimpleAssignment);
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

    private void AnalyzeSimpleAssignment(OperationAnalysisContext context)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (!TryParseEntryStateWrite(assignment, out var entityLocal, out var entityOperation, out var stateName))
            return;

        if (IsFromNoTrackingQuery(entityLocal, assignment))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, entityOperation.Syntax.GetLocation(), $"Entry.State = {stateName}"));
        }
    }

    private static bool TryParseEntryStateWrite(
        ISimpleAssignmentOperation assignment,
        out ILocalSymbol entityLocal,
        out IOperation entityOperation,
        out string stateName)
    {
        entityLocal = null!;
        entityOperation = null!;
        stateName = string.Empty;

        if (assignment.Target is not IPropertyReferenceOperation targetProperty ||
            targetProperty.Property.Name != "State" ||
            targetProperty.Property.ContainingType?.Name != "EntityEntry")
        {
            return false;
        }

        if (targetProperty.Instance?.UnwrapConversions() is not IInvocationOperation entryInvocation ||
            entryInvocation.TargetMethod.Name != "Entry" ||
            !entryInvocation.TargetMethod.ContainingType.IsDbContext() ||
            entryInvocation.Arguments.Length == 0)
        {
            return false;
        }

        entityOperation = entryInvocation.Arguments[0].Value.UnwrapConversions();
        if (entityOperation is not ILocalReferenceOperation localReference)
            return false;

        if (!TryGetEntityStateName(assignment.Value, out stateName))
            return false;

        entityLocal = localReference.Local;
        return true;
    }

    private static bool TryGetEntityStateName(IOperation value, out string stateName)
    {
        stateName = string.Empty;
        if (value.UnwrapConversions() is not IFieldReferenceOperation fieldReference)
            return false;

        var containingType = fieldReference.Field.ContainingType;
        if (containingType?.Name != "EntityState" ||
            containingType.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
        {
            return false;
        }

        if (fieldReference.Field.Name is not ("Modified" or "Deleted"))
            return false;

        stateName = fieldReference.Field.Name;
        return true;
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
        var origins = new List<LocalOrigin>();

        foreach (var op in EnumerateOperations(root))
        {
            // 1. Standard Assignments
            if (op is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                assignment.Syntax.SpanStart < currentStart)
            {
                origins.Add(new LocalOrigin(
                    assignment.Syntax.SpanStart,
                    IsNoTrackingSource(assignment.Value, assignment, visited),
                    assignment.Syntax));
            }

            // 2. Variable Declarations
            if (op is IVariableDeclarationOperation decl)
            {
                foreach (var declarator in decl.Declarators)
                {
                    if (SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                        declarator.Initializer != null &&
                        declarator.Syntax.SpanStart < currentStart)
                    {
                        origins.Add(new LocalOrigin(
                            declarator.Syntax.SpanStart,
                            IsNoTrackingSource(declarator.Initializer.Value, declarator.Initializer.Value, visited),
                            declarator.Syntax));
                    }
                }
            }

            // 3. Foreach Loops
            if (op is IForEachLoopOperation forEach)
            {
                // Check if our target 'local' is one of the locals defined by the loop
                if (forEach.Locals.Any(l => SymbolEqualityComparer.Default.Equals(l, local)) &&
                    forEach.Syntax.Span.Contains(currentStart) &&
                    forEach.Collection.Syntax.SpanStart < currentStart)
                {
                    // The loop local is rebound from the collection on every iteration the
                    // use participates in, so this origin is never conditional.
                    origins.Add(new LocalOrigin(
                        forEach.Collection.Syntax.SpanStart,
                        IsNoTrackingSource(forEach.Collection, forEach.Collection, visited),
                        null));
                }
            }
        }

        if (origins.Count == 0) return false;

        var best = origins[0];
        for (var i = 1; i < origins.Count; i++)
        {
            if (origins[i].Position >= best.Position)
                best = origins[i];
        }

        if (!best.IsNoTracking) return false;

        // Path-insensitivity guard: when the latest origin sits in a branch that may not
        // execute before the use, the effective state on the skip path is whatever the
        // latest UNCONDITIONAL origin before it established (anything earlier is dead,
        // superseded history). If that fallback disagrees — or only conditional origins
        // exist at all — the verdict depends on the path taken: stay quiet (same ambiguity
        // trade-off as LC044's multiple-assignment gate). An unconditional latest origin
        // dominates and needs no guard.
        if (IsConditionalRelativeTo(best.Syntax, currentStart, root.Syntax))
        {
            LocalOrigin? fallback = null;
            foreach (var origin in origins)
            {
                if (origin.Position < best.Position &&
                    (fallback == null || origin.Position > fallback.Value.Position) &&
                    !IsConditionalRelativeTo(origin.Syntax, currentStart, root.Syntax))
                {
                    fallback = origin;
                }
            }

            if (fallback == null || !fallback.Value.IsNoTracking)
                return false;
        }

        return true;
    }

    private readonly struct LocalOrigin
    {
        public LocalOrigin(int position, bool isNoTracking, SyntaxNode? syntax)
        {
            Position = position;
            IsNoTracking = isNoTracking;
            Syntax = syntax;
        }

        public int Position { get; }
        public bool IsNoTracking { get; }
        public SyntaxNode? Syntax { get; }
    }

    private static bool IsConditionalRelativeTo(SyntaxNode? originSyntax, int usePosition, SyntaxNode rootSyntax)
    {
        if (originSyntax == null) return false;

        for (var node = originSyntax.Parent; node != null && node != rootSyntax; node = node.Parent)
        {
            var isBranching = node is IfStatementSyntax
                or SwitchStatementSyntax
                or SwitchExpressionSyntax
                or ConditionalExpressionSyntax
                or CatchClauseSyntax
                or WhileStatementSyntax
                or ForStatementSyntax
                or CommonForEachStatementSyntax;

            if (isBranching && !node.Span.Contains(usePosition))
                return true;
        }

        return false;
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
            if (IsEfCoreNoTrackingDirective(invocation.TargetMethod)) return true;

            return HasAsNoTrackingInChain(invocation);
        }

        return false;
    }

    private bool HasAsNoTrackingInChain(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        var outermostSelectChecked = false;
        while (current is IInvocationOperation inv)
        {
            // Only the outermost Select (the one nearest the materializer) determines the
            // shape of the materialized result. If it projects to a newly-constructed object
            // the result is detached from EF change tracking entirely — constructed instances
            // are never tracked regardless of AsNoTracking — so LC025 must not fire. A later
            // (inner) Select that re-exposes the entity, e.g. Select(u => new { E = u }) then
            // Select(x => x.E), is handled because only the first Select encountered governs:
            // there the outermost Select is the member access, which falls through and keeps
            // the rule firing. Identity/navigation projections also fall through.
            if (inv.TargetMethod.Name == "Select" && !outermostSelectChecked)
            {
                outermostSelectChecked = true;
                if (IsProjectionToConstructedObject(inv))
                    return false;
            }

            // The last tracking directive applied wins — each AsTracking/AsNoTracking overwrites the
            // query's QueryTrackingBehavior. Walking up the receiver chain, the first directive
            // encountered is the one applied last, so it decides the effective mode:
            // AsNoTracking().AsTracking() is tracked (AsTracking overrides) and must NOT fire, while
            // AsTracking().AsNoTracking() is untracked and still fires.
            if (IsEfCoreNoTrackingDirective(inv.TargetMethod))
                return true;
            if (IsEfCoreAsTracking(inv.TargetMethod))
                return false;

            var next = inv.GetInvocationReceiver();
            if (next == null) break;
            current = next.UnwrapConversions();
        }
        return false;
    }

    private static bool IsProjectionToConstructedObject(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "Select")
            return false;

        var selector = invocation.Arguments.LastOrDefault()?.Value?.UnwrapConversions();
        var lambda = selector switch
        {
            IDelegateCreationOperation delegateCreation => delegateCreation.Target as IAnonymousFunctionOperation,
            IAnonymousFunctionOperation direct => direct,
            _ => null
        };

        if (lambda == null)
            return false;

        var projected = GetSingleProjectedExpression(lambda);
        return projected is IObjectCreationOperation or IAnonymousObjectCreationOperation;
    }

    private static IOperation? GetSingleProjectedExpression(IAnonymousFunctionOperation lambda)
    {
        foreach (var op in lambda.Body.Operations)
        {
            switch (op)
            {
                case IReturnOperation { ReturnedValue: { } returned }:
                    return returned.UnwrapConversions();
                case IExpressionStatementOperation expressionStatement:
                    return expressionStatement.Operation.UnwrapConversions();
            }
        }

        return null;
    }

    private static bool IsEfCoreNoTrackingDirective(IMethodSymbol method)
    {
        if (method.Name is not ("AsNoTracking" or "AsNoTrackingWithIdentityResolution"))
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

    private static bool IsEfCoreAsTracking(IMethodSymbol method)
    {
        if (method.Name != "AsTracking")
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }
}
