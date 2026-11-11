using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A repository that hands out an <c>IQueryable</c> hides the query's root: <c>repository.Query().Count()</c>.
/// A method or property declared in this compilation whose type is a queryable counts as EF-backed when it has at
/// least one <c>return</c> and every returned value is provably EF-backed, looked up to
/// <see cref="MaxReturnedQueryDepth"/> members deep. An interface, abstract or virtual method is followed into
/// every implementation, which must all agree.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    internal const int MaxReturnedQueryDepth = 2;

    private static bool ReturnsEfQuery(IInvocationOperation invocation, int depth, CancellationToken cancellationToken)
    {
        var method = invocation.TargetMethod;
        if (depth <= 0 ||
            invocation.SemanticModel == null ||
            !IsQueryType(method.ReturnType) ||
            (method.ReducedFrom ?? method).OriginalDefinition.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        var cache = NPlusOneLooperHelperCache.For(invocation.SemanticModel.Compilation);
        var targets = GetHelperTargets(invocation, cache, cancellationToken);
        if (targets.IsEmpty)
            return false;

        foreach (var target in targets)
        {
            if (!MemberReturnsEfQuery(target, depth, cache, cancellationToken))
                return false;
        }

        return true;
    }

    private static bool ReturnsEfQuery(IPropertyReferenceOperation propertyReference, int depth, CancellationToken cancellationToken)
    {
        var property = propertyReference.Property.OriginalDefinition;
        if (depth <= 0 ||
            propertyReference.SemanticModel == null ||
            property.GetMethod == null ||
            property.IsAbstract ||
            property.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        // An overridable property might be replaced by one that reads a list.
        if ((property.IsVirtual || property.IsOverride) &&
            !property.IsSealed &&
            property.ContainingType?.IsSealed != true)
        {
            return false;
        }

        var cache = NPlusOneLooperHelperCache.For(propertyReference.SemanticModel.Compilation);
        return MemberReturnsEfQuery(property.GetMethod, depth, cache, cancellationToken);
    }

    private static bool IsQueryType(ITypeSymbol? type)
    {
        return type != null && (type.IsDbSet() || type.IsIQueryable());
    }

    private static bool MemberReturnsEfQuery(
        IMethodSymbol member,
        int depth,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetReturnsEfQuery(member, out var cached))
            return cached;

        cancellationToken.ThrowIfCancellationRequested();
        var returnsQuery = TryGetMemberBody(member, cache, cancellationToken, out var root) &&
                           EveryReturnIsEfQuery(root, depth, cancellationToken);

        // A verdict reached with the full depth holds at any depth; one reached with less might be too cautious.
        return depth == MaxReturnedQueryDepth
            ? cache.StoreReturnsEfQuery(member, returnsQuery)
            : returnsQuery;
    }

    private static bool EveryReturnIsEfQuery(IOperation root, int depth, CancellationToken cancellationToken)
    {
        var sawReturn = false;
        foreach (var operation in DescendantsInSameBody(root))
        {
            if (operation is not IReturnOperation { ReturnedValue: { } returned })
                continue;

            sawReturn = true;
            if (AnalyzeQueryProvenance(returned, root, cancellationToken, depth - 1).Kind != QueryProvenanceKind.Proven)
                return false;
        }

        return sawReturn;
    }

    /// <summary>The body of a method, local function or property getter declared in this compilation.</summary>
    private static bool TryGetMemberBody(
        IMethodSymbol member,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken,
        out IOperation root)
    {
        if (member.MethodKind != MethodKind.PropertyGet)
            return TryGetHelperBody(member, cache, cancellationToken, out root);

        root = null!;
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(cancellationToken);
            var hasBody = syntax switch
            {
                ArrowExpressionClauseSyntax => true,
                AccessorDeclarationSyntax accessor => accessor.Body != null || accessor.ExpressionBody != null,
                _ => false
            };
            if (!hasBody || !cache.TryGetSemanticModel(syntax.SyntaxTree, out var semanticModel))
                return false;

            var operation = semanticModel.GetOperation(syntax, cancellationToken);
            if (operation == null)
                return false;

            root = operation;
            return true;
        }

        return false;
    }
}
