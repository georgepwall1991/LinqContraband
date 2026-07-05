using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingWhereBeforeExecuteDeleteUpdateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC035";
    private const string Category = "Safety";
    private static readonly LocalizableString Title = "Missing Where before bulk execute";

    private static readonly LocalizableString MessageFormat =
        "Call to '{0}' can affect the entire query because no Where() filter is present";

    private static readonly LocalizableString Description =
        "ExecuteDelete/ExecuteUpdate should usually follow a filter. A missing Where() can delete or update every row in the table.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC035_MissingWhereBeforeExecuteDeleteUpdate.md");

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "ExecuteDelete",
        "ExecuteDeleteAsync",
        "ExecuteUpdate",
        "ExecuteUpdateAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!TargetMethods.Contains(method.Name))
            return;

        if (!IsEntityFrameworkCoreNamespace(method.ContainingNamespace))
            return;

        var receiverType = invocation.GetInvocationReceiver()?.Type;
        if (receiverType?.IsIQueryable() != true && receiverType?.IsDbSet() != true)
            return;

        if (HasWhereInChain(invocation.GetInvocationReceiver(), context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
    }

    private static bool HasWhereInChain(IOperation? operation, CancellationToken cancellationToken)
    {
        return HasWhereInChain(
            operation,
            cancellationToken,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool HasWhereInChain(
        IOperation? operation,
        CancellationToken cancellationToken,
        ISet<ILocalSymbol> visitedLocals)
    {
        var current = operation;

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                if (IsKnownLinqWhere(invocation.TargetMethod))
                    return true;

                current = invocation.GetInvocationReceiver();
                continue;
            }

            if (current is ITranslatedQueryOperation translatedQuery)
            {
                if (HasQuerySyntaxWhere(translatedQuery.Syntax))
                    return true;

                current = translatedQuery.Operation;
                continue;
            }

            if (current is IConditionalOperation conditional)
            {
                return HasWhereInChain(conditional.WhenTrue, cancellationToken, ForkVisitedLocals(visitedLocals)) &&
                       HasWhereInChain(conditional.WhenFalse, cancellationToken, ForkVisitedLocals(visitedLocals));
            }

            if (current is ISwitchExpressionOperation switchExpression)
            {
                return switchExpression.Arms.Length > 0 &&
                       switchExpression.Arms.All(arm =>
                           HasWhereInChain(arm.Value, cancellationToken, ForkVisitedLocals(visitedLocals)));
            }

            if (current is ILocalReferenceOperation localReference)
                return HasWhereInLocalInitializer(localReference, cancellationToken, visitedLocals);

            if (current is IParameterReferenceOperation or IFieldReferenceOperation or IPropertyReferenceOperation)
                return false;

            if (current.Type.IsDbSet() || current.Type.IsIQueryable())
                return false;

            current = current.Parent;
        }

        return false;
    }

    private static bool HasWhereInLocalInitializer(
        ILocalReferenceOperation localReference,
        CancellationToken cancellationToken,
        ISet<ILocalSymbol> visitedLocals)
    {
        if (!visitedLocals.Add(localReference.Local))
            return false;

        var executableRoot = localReference.FindOwningExecutableRoot();
        if (executableRoot == null)
            return false;

        // Split the assignments before this use into the latest unconditional one — the "base" every
        // control-flow path passes through — and the conditional reassignments after it, each taken
        // only on some paths. The local is filtered on EVERY path iff the unconditional base is
        // filtered AND every later conditional reassignment is also filtered. This stops a false
        // positive on the common "base filter + optional extra narrowing" shape
        //   var q = db.Set<User>().Where(...); if (flag) q = q.Where(...); q.ExecuteDelete();
        // while still reporting when a conditional path reassigns to an UNfiltered query.
        LocalAssignment? latestUnconditional = null;
        var conditionalReassignments = new List<LocalAssignment>();

        var assignments = LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken);

        foreach (var assignment in assignments)
        {
            if (assignment.SpanStart >= localReference.Syntax.SpanStart)
                continue;

            if (IsControlFlowConditionalAssignment(assignment.Value.Syntax))
                conditionalReassignments.Add(assignment);
            else if (latestUnconditional == null || assignment.SpanStart > latestUnconditional.Value.SpanStart)
                latestUnconditional = assignment;
        }

        if (latestUnconditional == null)
            return HasWhereInExhaustiveIfElseAssignments(
                assignments,
                localReference.Syntax.SpanStart,
                cancellationToken,
                visitedLocals);

        if (!HasWhereInChain(
                latestUnconditional.Value.Value.UnwrapConversions(),
                cancellationToken,
                ForkVisitedLocals(visitedLocals)))
            return false;

        foreach (var conditional in conditionalReassignments)
        {
            // Only reassignments after the dominating unconditional base can change the value on a path;
            // earlier conditional assignments are overwritten by the base.
            if (conditional.SpanStart <= latestUnconditional.Value.SpanStart)
                continue;

            if (!HasWhereInChain(
                    conditional.Value.UnwrapConversions(),
                    cancellationToken,
                    ForkVisitedLocals(visitedLocals)))
                return false;
        }

        return true;
    }

    private static bool HasWhereInExhaustiveIfElseAssignments(
        IReadOnlyList<LocalAssignment> assignments,
        int beforePosition,
        CancellationToken cancellationToken,
        ISet<ILocalSymbol> visitedLocals)
    {
        var earlierAssignments = assignments
            .Where(assignment => assignment.SpanStart < beforePosition)
            .ToArray();
        if (earlierAssignments.Length == 0)
            return false;

        foreach (var ifStatement in earlierAssignments
                     .Select(assignment => assignment.Value.Syntax.FirstAncestorOrSelf<IfStatementSyntax>())
                     .Where(ifStatement => ifStatement?.Else != null)
                     .Distinct())
        {
            if (ifStatement == null)
                continue;

            var assignmentsFromCandidate = earlierAssignments
                .Where(assignment => assignment.SpanStart >= ifStatement.SpanStart)
                .ToArray();

            var thenAssignments = earlierAssignments
                .Where(assignment => ifStatement.Statement.Span.Contains(assignment.Value.Syntax.Span))
                .ToArray();
            var elseAssignments = earlierAssignments
                .Where(assignment => ifStatement.Else!.Statement.Span.Contains(assignment.Value.Syntax.Span))
                .ToArray();
            var laterAssignments = assignmentsFromCandidate
                .Where(assignment => !ifStatement.Span.Contains(assignment.Value.Syntax.Span))
                .ToArray();

            if (thenAssignments.Length == 0 || elseAssignments.Length == 0)
                continue;

            if (thenAssignments.All(assignment =>
                    HasWhereInChain(
                        assignment.Value.UnwrapConversions(),
                        cancellationToken,
                        ForkVisitedLocals(visitedLocals))) &&
                elseAssignments.All(assignment =>
                    HasWhereInChain(
                        assignment.Value.UnwrapConversions(),
                        cancellationToken,
                        ForkVisitedLocals(visitedLocals))) &&
                laterAssignments.All(assignment =>
                    HasWhereInChain(
                        assignment.Value.UnwrapConversions(),
                        cancellationToken,
                        ForkVisitedLocals(visitedLocals))))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<ILocalSymbol> ForkVisitedLocals(ISet<ILocalSymbol> visitedLocals)
    {
        return new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default);
    }

    private static bool IsControlFlowConditionalAssignment(SyntaxNode syntax)
    {
        return syntax.Ancestors().Any(ancestor =>
            ancestor is IfStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax or
                ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                TryStatementSyntax or CatchClauseSyntax);
    }

    private static bool IsKnownLinqWhere(IMethodSymbol method)
    {
        return method.Name == "Where" &&
               method.ContainingNamespace?.ToString() == "System.Linq" &&
               method.ContainingType?.Name is "Queryable" or "Enumerable";
    }

    private static bool HasQuerySyntaxWhere(SyntaxNode syntax)
    {
        return syntax
            .DescendantNodesAndSelf()
            .OfType<QueryExpressionSyntax>()
            .Any(query => query.Body.Clauses.OfType<WhereClauseSyntax>().Any());
    }

    private static bool IsEntityFrameworkCoreNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }
}
