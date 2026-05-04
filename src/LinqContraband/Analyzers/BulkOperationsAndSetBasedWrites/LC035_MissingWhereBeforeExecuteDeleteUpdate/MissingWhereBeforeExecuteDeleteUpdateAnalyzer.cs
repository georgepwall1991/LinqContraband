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

        if (!TryGetLatestStraightLineAssignedValueBefore(
                executableRoot,
                localReference,
                cancellationToken,
                out var assignedValue))
        {
            return false;
        }

        return HasWhereInChain(assignedValue, cancellationToken, visitedLocals);
    }

    private static bool TryGetLatestStraightLineAssignedValueBefore(
        IOperation executableRoot,
        ILocalReferenceOperation localReference,
        CancellationToken cancellationToken,
        out IOperation assignedValue)
    {
        assignedValue = null!;
        LocalAssignment? latest = null;

        foreach (var assignment in LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken))
        {
            if (assignment.SpanStart >= localReference.Syntax.SpanStart)
                continue;

            if (latest == null || assignment.SpanStart > latest.Value.SpanStart)
                latest = assignment;
        }

        if (latest == null || IsControlFlowConditionalAssignment(latest.Value.Value.Syntax))
            return false;

        assignedValue = latest.Value.Value.UnwrapConversions();
        return true;
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
