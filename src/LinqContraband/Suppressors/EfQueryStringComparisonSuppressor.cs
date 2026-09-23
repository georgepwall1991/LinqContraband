using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Suppressors;

/// <summary>
/// Suppresses the .NET string-comparison and culture analyzers (CA1862, CA1304, CA1305, CA1307, CA1309, CA1310,
/// CA1311) inside EF Core query expression trees. Their suggested fixes add a <c>StringComparison</c>,
/// <c>CultureInfo</c> or <c>IFormatProvider</c> argument, or switch to <c>ToLowerInvariant()</c>, and EF Core cannot
/// translate any of those to SQL: the query that followed the advice throws at run time. The database collation decides
/// how the comparison behaves, not the .NET culture.
/// </summary>
/// <remarks>
/// A warning is suppressed only when the flagged expression reads a parameter of a lambda that EF Core translates: a
/// lambda converted to <c>Expression&lt;T&gt;</c> and passed to a <c>System.Linq.Queryable</c> or EF Core method, or a
/// lambda nested inside one. The same call on a captured variable (<c>name.ToLower()</c>) runs in .NET before the
/// query is sent, so its warning stays. Queries over <c>list.AsQueryable()</c> run in memory and keep their warnings.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EfQueryStringComparisonSuppressor : DiagnosticSuppressor
{
    private const string Justification =
        "EF Core translates this expression to SQL, where the database collation decides the comparison. The suggested StringComparison, CultureInfo or invariant overload cannot be translated and would make the query throw.";

    private static readonly ImmutableArray<string> SuppressedIds = ImmutableArray.Create(
        "CA1304", "CA1305", "CA1307", "CA1309", "CA1310", "CA1311", "CA1862");

    private static readonly ImmutableDictionary<string, SuppressionDescriptor> DescriptorsById =
        SuppressedIds.ToImmutableDictionary(
            id => id,
            id => new SuppressionDescriptor("LCS" + id.Substring(2), id, Justification));

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
        DescriptorsById.Values.OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal).ToImmutableArray();

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        if (context.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext") is null)
            return;

        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (!DescriptorsById.TryGetValue(diagnostic.Id, out var descriptor))
                continue;

            if (IsInsideTranslatedQuery(diagnostic, context))
                context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
        }
    }

    private static bool IsInsideTranslatedQuery(Diagnostic diagnostic, SuppressionAnalysisContext context)
    {
        var tree = diagnostic.Location.SourceTree;
        if (tree is null || !diagnostic.Location.IsInSource)
            return false;

        var cancellationToken = context.CancellationToken;
        var root = tree.GetRoot(cancellationToken);
        if (!root.FullSpan.Contains(diagnostic.Location.SourceSpan))
            return false;

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var model = context.GetSemanticModel(tree);
        var flagged = GetFlaggedOperation(node, model, cancellationToken);

        return flagged is not null && ReadsTranslatedLambdaParameter(flagged);
    }

    /// <summary>
    /// Maps the reported location to an operation. CA1311 points at the method name (<c>ToLower</c>), the others at
    /// the invocation or comparison, so a name is first widened to the member access and invocation that own it.
    /// </summary>
    private static IOperation? GetFlaggedOperation(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        var current = node;
        while (true)
        {
            if (current.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == current)
            {
                current = memberAccess;
                continue;
            }

            if (current.Parent is InvocationExpressionSyntax invocation && invocation.Expression == current)
            {
                current = invocation;
                continue;
            }

            break;
        }

        for (var candidate = current; candidate is not null; candidate = candidate.Parent)
        {
            if (candidate is StatementSyntax or MemberDeclarationSyntax)
                return null;

            if (candidate is not ExpressionSyntax)
                continue;

            var operation = model.GetOperation(candidate, cancellationToken);
            if (operation is not null)
                return operation;
        }

        return null;
    }

    private static bool ReadsTranslatedLambdaParameter(IOperation flagged)
    {
        var lambdaParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);

        for (var current = flagged.Parent; current is not null; current = current.Parent)
        {
            if (current is not IAnonymousFunctionOperation lambda)
                continue;

            foreach (var parameter in lambda.Symbol.Parameters)
                lambdaParameters.Add(parameter);

            // A lambda converted to a delegate (Enumerable.Any inside a query) is still part of the expression tree
            // when an outer lambda is; keep climbing until the lambda that is converted to Expression<T>.
            if (lambda.Parent is not IConversionOperation conversion || !IsExpressionOfT(conversion.Type))
                continue;

            if (!IsTranslatedQueryArgument(conversion))
                return false;

            return flagged.DescendantsAndSelf()
                .OfType<IParameterReferenceOperation>()
                .Any(reference => lambdaParameters.Contains(reference.Parameter));
        }

        return false;
    }

    private static bool IsExpressionOfT(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "Expression", Arity: 1 } named &&
               named.ContainingNamespace?.ToString() == "System.Linq.Expressions";
    }

    private static bool IsTranslatedQueryArgument(IConversionOperation conversion)
    {
        if (conversion.Parent is not IArgumentOperation { Parent: IInvocationOperation invocation })
            return false;

        var method = invocation.TargetMethod;
        if (IsQueryableMethod(method))
            return !IsInMemoryQueryable(invocation.GetInvocationReceiver());

        if (!IsEntityFrameworkMethod(method))
            return false;

        // EF Core query operators (FirstOrDefaultAsync, Include, ...) take the query as their receiver. Outside a query,
        // only query filters and ExecuteUpdate setters are translated: a HasConversion lambda is compiled and runs in
        // .NET, where culture does apply.
        var receiver = invocation.GetInvocationReceiver();
        if (receiver?.Type.IsIQueryable() == true)
            return !IsInMemoryQueryable(receiver);

        return method.Name is "HasQueryFilter" or "SetProperty";
    }

    private static bool IsEntityFrameworkMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        return ns is not null &&
               (ns.Equals("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal));
    }

    private static bool IsQueryableMethod(IMethodSymbol method)
    {
        return method.ContainingType is { Name: "Queryable" } type &&
               type.ContainingNamespace?.ToString() == "System.Linq";
    }

    /// <summary>
    /// Follows the query chain back to its source. A chain that starts at <c>AsQueryable()</c> over a type that is not
    /// already <c>IQueryable</c> (a list or array) runs as LINQ to Objects, where culture and comparison do apply.
    /// </summary>
    private static bool IsInMemoryQueryable(IOperation? source)
    {
        var current = source;
        while (current is IInvocationOperation invocation && IsQueryableMethod(invocation.TargetMethod))
        {
            var receiver = invocation.GetInvocationReceiver();
            if (invocation.TargetMethod.Name == "AsQueryable")
                return receiver is not null && !receiver.Type.IsIQueryable();

            current = receiver;
        }

        return false;
    }
}
