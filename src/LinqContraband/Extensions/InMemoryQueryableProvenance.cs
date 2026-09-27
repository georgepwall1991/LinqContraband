using System;
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
    /// a local with a single write that is one of these; or the sequence returned by a
    /// <c>System.Linq.Enumerable</c> operator whose every sequence input is itself proven here. Enumerable
    /// operators are lazy, so <c>db.Users.AsEnumerable().Where(...)</c> still enumerates the EF query, and
    /// <c>AsEnumerable</c>, <c>Cast</c> and <c>OfType</c> can hand back their source (possibly a <c>DbSet</c>)
    /// unchanged. A bare <c>IEnumerable&lt;T&gt;</c> may be a <c>DbSet</c> at run time, in which case
    /// <c>AsQueryable()</c> returns the EF query itself.
    /// </summary>
    /// <param name="isInMemoryLeafType">
    /// An extra proof for a leaf the walk cannot follow, judged by its static type (LC008 accepts
    /// interface-typed sequences of non-entity types).
    /// </param>
    public static bool IsInMemorySequenceSource(IOperation? sequence, Func<ITypeSymbol, bool>? isInMemoryLeafType = null)
    {
        return IsInMemorySequenceSource(sequence, isInMemoryLeafType, 0);
    }

    private static bool IsInMemorySequenceSource(IOperation? sequence, Func<ITypeSymbol, bool>? isInMemoryLeafType, int depth)
    {
        if (depth >= MaxDepth)
            return false;

        sequence = sequence?.UnwrapConversions();
        var type = sequence?.Type;
        if (sequence == null || type == null || type.IsIQueryable())
            return false;

        if (IsConcreteInMemorySequence(type) || IsNonQueryCollectionInterface(type))
            return true;

        switch (sequence)
        {
            case IInvocationOperation invocation when IsEnumerableOperator(invocation.TargetMethod):
                return AreSequenceInputsInMemory(invocation, isInMemoryLeafType, depth);

            case ILocalReferenceOperation localReference:
            {
                var root = localReference.FindOwningExecutableRoot();
                if (root != null &&
                    LocalAssignmentCache.GetAssignments(root, localReference.Local).Count == 1 &&
                    LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        root,
                        localReference.Local,
                        localReference.Syntax.SpanStart,
                        out var value) &&
                    IsInMemorySequenceSource(value, isInMemoryLeafType, depth + 1))
                    return true;

                break;
            }
        }

        return isInMemoryLeafType?.Invoke(type) == true;
    }

    /// <summary>
    /// A <c>System.Linq.Enumerable</c> operator that returns a sequence (Select, Where, Concat, AsEnumerable,
    /// ...). Element operators such as <c>First()</c> are excluded: they can return a stored sequence, which
    /// may be a <c>DbSet</c>.
    /// </summary>
    private static bool IsEnumerableOperator(IMethodSymbol targetMethod)
    {
        var method = targetMethod.ReducedFrom ?? targetMethod;
        if (method.ContainingType is not { Name: "Enumerable" } containingType ||
            containingType.ContainingNamespace?.ToDisplayString() != "System.Linq")
            return false;

        return method.OriginalDefinition.ReturnType is INamedTypeSymbol returnType &&
               (returnType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ||
                returnType.OriginalDefinition is { Name: "IOrderedEnumerable", Arity: 1 } ordered &&
                ordered.ContainingNamespace?.ToDisplayString() == "System.Linq");
    }

    /// <summary>
    /// Every sequence the operator enumerates (its source, and the second sequence of Concat, Join, Union,
    /// Zip, ...) must itself be in memory, because the operator's iterator enumerates them lazily. An
    /// operator with no sequence input (Range, Repeat, Empty) builds its own. SelectMany's collection
    /// selector counts too: <c>list.SelectMany(_ =&gt; db.Users)</c> enumerates the EF query, so each sequence
    /// the lambda returns must be proven in memory, and a selector that is not a lambda is not followed.
    /// Other selectors (Select, a Join or GroupJoin result selector, SelectMany's result selector) only
    /// yield their value, so a sequence they return is an element and is not enumerated.
    /// </summary>
    private static bool AreSequenceInputsInMemory(
        IInvocationOperation invocation,
        Func<ITypeSymbol, bool>? isInMemoryLeafType,
        int depth)
    {
        foreach (var argument in invocation.Arguments)
        {
            var parameterType = argument.Parameter?.Type;
            if (parameterType == null)
                continue;

            if (IsSequenceInputParameter(argument.Parameter!))
            {
                if (!IsInMemorySequenceSource(argument.Value, isInMemoryLeafType, depth + 1))
                    return false;

                continue;
            }

            if (IsEnumeratedSelector(invocation.TargetMethod, argument.Parameter!) &&
                !AreReturnedSequencesInMemory(argument.Value, isInMemoryLeafType, depth))
                return false;
        }

        return true;
    }

    private static bool AreReturnedSequencesInMemory(
        IOperation selector,
        Func<ITypeSymbol, bool>? isInMemoryLeafType,
        int depth)
    {
        var current = selector.UnwrapConversions();
        if (current is IDelegateCreationOperation creation)
            current = creation.Target.UnwrapConversions();

        if (current is not IAnonymousFunctionOperation lambda)
            return false;

        var returned = false;
        foreach (var operation in lambda.Body.Descendants())
        {
            if (operation is not IReturnOperation { Kind: OperationKind.Return } returnOperation ||
                !IsDirectlyInLambda(returnOperation, lambda))
                continue;

            if (!IsInMemorySequenceSource(returnOperation.ReturnedValue, isInMemoryLeafType, depth + 1))
                return false;

            returned = true;
        }

        return returned;
    }

    private static bool IsDirectlyInLambda(IOperation operation, IAnonymousFunctionOperation lambda)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current == lambda)
                return true;

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return false;
        }

        return false;
    }

    /// <summary>
    /// SelectMany's <c>selector</c> or <c>collectionSelector</c>: the delegate whose returned sequence the
    /// operator flattens, and so enumerates.
    /// </summary>
    private static bool IsEnumeratedSelector(IMethodSymbol targetMethod, IParameterSymbol parameter)
    {
        var method = targetMethod.ReducedFrom ?? targetMethod;
        return method.Name == "SelectMany" &&
               parameter.Name is "selector" or "collectionSelector" &&
               parameter.Type.TypeKind == TypeKind.Delegate;
    }

    /// <summary>
    /// A sequence the operator reads, judged by the operator's generic definition: a parameter declared
    /// as <c>IEnumerable</c> or as <c>IEnumerable&lt;TSource&gt;</c> over one of the operator's own type
    /// parameters (source, inner, second, ...). <c>Append</c>'s and <c>Prepend</c>'s <c>TSource element</c>
    /// is a value even when <c>TSource</c> is substituted with <c>IEnumerable&lt;User&gt;</c>.
    /// </summary>
    private static bool IsSequenceInputParameter(IParameterSymbol parameter)
    {
        var type = parameter.OriginalDefinition.Type;
        if (type.SpecialType == SpecialType.System_Collections_IEnumerable)
            return true;

        return type is INamedTypeSymbol { Arity: 1 } named &&
               named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
               named.TypeArguments[0] is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method };
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
