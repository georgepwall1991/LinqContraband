using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

/// <summary>
/// Proves that an <c>IQueryable</c> runs on LINQ to Objects: its chain starts at
/// <c>Queryable.AsQueryable()</c> over an in-memory sequence (see <see cref="IsInMemorySequenceSource"/>), or at <c>new EnumerableQuery&lt;T&gt;(...)</c>.
/// Unit tests, in-memory repositories and MockQueryable-style fakes build queries this way, and
/// EF translation rules (local methods, <c>DateTime.Now</c>, comparison overloads, nested
/// <c>ToList</c>) do not apply to them. Anything the walk cannot follow (a parameter, field,
/// property, <c>DbSet</c>, or a local with more than one write) is not proven in-memory.
/// </summary>
public static class InMemoryQueryableProvenance
{
    private const int MaxDepth = 32;

    public static bool IsProvablyInMemoryQueryable(this IOperation? operation)
    {
        var current = operation;
        for (var depth = 0; current != null && depth < MaxDepth; depth++)
        {
            current = Unwrap(current);

            switch (current)
            {
                case IInvocationOperation invocation:
                {
                    var receiver = invocation.GetInvocationReceiver();
                    if (IsQueryableAsQueryable(invocation.TargetMethod))
                    {
                        // AsQueryable() over something already queryable is a pass-through.
                        if (receiver?.Type.IsIQueryable() == true)
                        {
                            current = receiver;
                            continue;
                        }

                        return IsInMemorySequenceSource(receiver);
                    }

                    // Query operators and query-to-query helpers (Where, OrderBy, BuildMock, ...)
                    // keep the provider of their source.
                    if (receiver?.Type.IsIQueryable() == true && invocation.Type.IsIQueryable())
                    {
                        current = receiver;
                        continue;
                    }

                    return false;
                }

                case IObjectCreationOperation creation:
                    return creation.Type is INamedTypeSymbol { Name: "EnumerableQuery", Arity: 1 } created &&
                           created.ContainingNamespace?.ToDisplayString() == "System.Linq";

                case ILocalReferenceOperation localReference:
                {
                    var root = localReference.FindOwningExecutableRoot();
                    if (root == null ||
                        LocalAssignmentCache.GetAssignments(root, localReference.Local).Count != 1 ||
                        !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                            root,
                            localReference.Local,
                            localReference.Syntax.SpanStart,
                            out var value))
                        return false;

                    current = value;
                    continue;
                }

                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <c>AsQueryable()</c> over <paramref name="sequence"/> (a non-queryable source, conversions
    /// unwrapped) yields a LINQ-to-Objects query: an array or a class or struct that is not itself
    /// queryable (<c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c>, ...); a value statically typed as one of the
    /// collection interfaces no EF query type implements (<c>ICollection&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>,
    /// <c>IReadOnlyCollection&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>ISet&lt;T&gt;</c>, <c>IReadOnlySet&lt;T&gt;</c>);
    /// or the sequence returned by a <c>System.Linq.Enumerable</c> operator, which is always a LINQ-to-Objects
    /// iterator. <c>AsEnumerable</c>, <c>Cast</c> and <c>OfType</c> are excluded because they can hand back
    /// their source unchanged, and a bare <c>IEnumerable&lt;T&gt;</c> may be a <c>DbSet</c> at run time, in
    /// which case <c>AsQueryable()</c> returns the EF query itself.
    /// </summary>
    public static bool IsInMemorySequenceSource(IOperation? sequence)
    {
        var type = sequence?.Type;
        if (type == null || type.IsIQueryable())
            return false;

        return IsConcreteInMemorySequence(type) ||
               IsNonQueryCollectionInterface(type) ||
               sequence is IInvocationOperation invocation && IsEnumerableOperatorResult(invocation);
    }

    private static bool IsConcreteInMemorySequence(ITypeSymbol type)
    {
        return type switch
        {
            IArrayTypeSymbol => true,
            INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named => !named.IsIQueryable(),
            _ => false
        };
    }

    private static bool IsNonQueryCollectionInterface(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Interface, Arity: 1 } named)
            return false;

        var definition = named.OriginalDefinition;
        switch (definition.SpecialType)
        {
            case SpecialType.System_Collections_Generic_ICollection_T:
            case SpecialType.System_Collections_Generic_IList_T:
            case SpecialType.System_Collections_Generic_IReadOnlyCollection_T:
            case SpecialType.System_Collections_Generic_IReadOnlyList_T:
                return true;
        }

        return definition.Name is "ISet" or "IReadOnlySet" &&
               definition.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }

    private static bool IsEnumerableOperatorResult(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (method.Name is "AsEnumerable" or "Cast" or "OfType" ||
            method.ContainingType is not { Name: "Enumerable" } containingType ||
            containingType.ContainingNamespace?.ToDisplayString() != "System.Linq")
            return false;

        // Only operators that build a new sequence (Select, Where, Concat, ...); element operators such as
        // First() can return a stored sequence, which may be a DbSet.
        return method.OriginalDefinition.ReturnType is INamedTypeSymbol returnType &&
               (returnType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ||
                returnType.OriginalDefinition is { Name: "IOrderedEnumerable", Arity: 1 } ordered &&
                ordered.ContainingNamespace?.ToDisplayString() == "System.Linq");
    }

    private static IOperation Unwrap(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        while (current is ITranslatedQueryOperation query)
            current = query.Operation.UnwrapConversions();

        return current;
    }

    private static bool IsQueryableAsQueryable(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return original.Name == "AsQueryable" &&
               original.ContainingType is { Name: "Queryable" } containingType &&
               containingType.ContainingNamespace?.ToDisplayString() == "System.Linq";
    }
}
