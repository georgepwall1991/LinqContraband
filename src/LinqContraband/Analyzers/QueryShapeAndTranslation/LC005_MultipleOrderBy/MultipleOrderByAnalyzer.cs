using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC005_MultipleOrderBy;

/// <summary>
/// Analyzes consecutive OrderBy or OrderByDescending calls that reset previous sorting. Diagnostic ID: LC005
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Calling OrderBy or OrderByDescending multiple times in a chain completely replaces
/// the previous sort order rather than adding a secondary sort. This is almost always a bug where the developer intended
/// to create a multi-level sort (e.g., by LastName then FirstName). Use ThenBy or ThenByDescending after the first OrderBy
/// to create proper multi-level sorting that preserves the original sort while adding sub-sorting for ties.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MultipleOrderByAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC005";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Multiple OrderBy calls";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' after an existing sort resets the order";

    private static readonly LocalizableString Description =
        "Consecutive OrderBy calls reset the sorting. Use ThenBy to chain sorts.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC005_MultipleOrderBy.md");

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

        if (!IsOrderBy(method)) return;

        var receiver = invocation.GetInvocationReceiver();

        if (TryGetPreviousSortInvocation(receiver, invocation, context.Operation.SemanticModel, out var previousInvocation))
        {
            var previousMethod = previousInvocation.TargetMethod;
            if (IsSortMethod(previousMethod))
            {
                // Fluent syntax (`a.OrderBy(...).OrderBy(...)`) carries an InvocationExpressionSyntax;
                // point the diagnostic at the offending method name. Query-comprehension syntax
                // (`orderby x orderby y`) lowers to the same OrderBy(...).OrderBy(...) operations but
                // their syntax node is an OrderingSyntax, not an invocation — casting it crashes the
                // analyzer (AD0001), so fall back to the operation's own location and still report the reset.
                var location = invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
                    ? memberAccess.Name.GetLocation()
                    : invocation.Syntax.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, method.Name));
            }
        }
    }

    private bool TryGetPreviousSortInvocation(
        IOperation? receiver,
        IInvocationOperation currentInvocation,
        SemanticModel? semanticModel,
        out IInvocationOperation previousInvocation)
    {
        if (receiver is IInvocationOperation directInvocation)
        {
            previousInvocation = directInvocation;
            return true;
        }

        if (receiver is ILocalReferenceOperation localReference &&
            semanticModel != null &&
            TryGetSingleAssignmentLocalInitializer(localReference, currentInvocation, semanticModel, out var initializer) &&
            initializer?.UnwrapConversions() is IInvocationOperation initializerInvocation)
        {
            previousInvocation = initializerInvocation;
            return true;
        }

        previousInvocation = null!;
        return false;
    }

    private bool TryGetSingleAssignmentLocalInitializer(
        ILocalReferenceOperation localReference,
        IInvocationOperation currentInvocation,
        SemanticModel semanticModel,
        out IOperation? initializer)
    {
        initializer = null;

        var declarationSyntax = localReference.Local.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();

        if (declarationSyntax?.Initializer?.Value == null)
            return false;

        if (HasWriteBeforeUse(localReference.Local, declarationSyntax, currentInvocation.Syntax, semanticModel))
            return false;

        initializer = semanticModel.GetOperation(UnwrapInitializerExpression(declarationSyntax.Initializer.Value))?.UnwrapConversions();
        return initializer != null;
    }

    private static ExpressionSyntax UnwrapInitializerExpression(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool HasWriteBeforeUse(
        ILocalSymbol local,
        VariableDeclaratorSyntax declarationSyntax,
        SyntaxNode useSyntax,
        SemanticModel semanticModel)
    {
        var scope = (SyntaxNode?)declarationSyntax.FirstAncestorOrSelf<BlockSyntax>() ??
            declarationSyntax.FirstAncestorOrSelf<CompilationUnitSyntax>();
        if (scope == null)
            return false;

        foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.SpanStart <= declarationSyntax.SpanStart || assignment.SpanStart >= useSyntax.SpanStart)
                continue;

            if (IsWriteToLocal(assignment.Left, local, semanticModel))
                return true;
        }

        foreach (var argument in scope.DescendantNodes().OfType<ArgumentSyntax>())
        {
            if (argument.SpanStart <= declarationSyntax.SpanStart || argument.SpanStart >= useSyntax.SpanStart)
                continue;

            var keywordText = argument.RefOrOutKeyword.Text;
            if (keywordText is not ("out" or "ref"))
                continue;

            var symbol = semanticModel.GetSymbolInfo(argument.Expression).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, local))
                return true;
        }

        return false;
    }

    private static bool IsWriteToLocal(
        ExpressionSyntax target,
        ILocalSymbol local,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(target).Symbol;
        if (SymbolEqualityComparer.Default.Equals(symbol, local))
            return true;

        return target switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                IsWriteToLocal(parenthesized.Expression, local, semanticModel),
            TupleExpressionSyntax tuple =>
                tuple.Arguments.Any(argument => IsWriteToLocal(argument.Expression, local, semanticModel)),
            _ => false
        };
    }

    private bool IsOrderBy(IMethodSymbol method)
    {
        return (method.Name == "OrderBy" || method.Name == "OrderByDescending") &&
               (method.ContainingType.Name == "Enumerable" || method.ContainingType.Name == "Queryable") &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }

    private bool IsSortMethod(IMethodSymbol method)
    {
        return (method.Name == "OrderBy" || method.Name == "OrderByDescending" ||
                method.Name == "ThenBy" || method.Name == "ThenByDescending") &&
               (method.ContainingType.Name == "Enumerable" || method.ContainingType.Name == "Queryable") &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }
}
