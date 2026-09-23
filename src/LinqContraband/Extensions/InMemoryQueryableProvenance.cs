using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

/// <summary>
/// Proves that an <c>IQueryable</c> runs on LINQ to Objects: its chain starts at
/// <c>Queryable.AsQueryable()</c> over an in-memory sequence, or at <c>new EnumerableQuery&lt;T&gt;(...)</c>.
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

                        return IsConcreteInMemorySequence(receiver?.Type);
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
    /// An array, or a class or struct that is not itself queryable (<c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c>, ...).
    /// An interface-typed source such as <c>IEnumerable&lt;T&gt;</c> may be a <c>DbSet</c> at runtime, in which
    /// case <c>AsQueryable()</c> hands back the EF query itself.
    /// </summary>
    private static bool IsConcreteInMemorySequence(ITypeSymbol? type)
    {
        return type switch
        {
            IArrayTypeSymbol => true,
            INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named => !named.IsIQueryable(),
            _ => false
        };
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
