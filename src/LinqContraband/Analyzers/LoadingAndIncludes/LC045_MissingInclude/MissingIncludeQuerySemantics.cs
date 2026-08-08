using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static IOperation? GetQuerySource(IInvocationOperation invocation)
    {
        if (invocation.Instance != null)
            return invocation.Instance;

        return invocation
            .Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)
            ?.Value;
    }

    private static bool TryGetSupportedCollectionCallback(
        IInvocationOperation invocation,
        IInvocationOperation materializer,
        ILocalSymbol? resultLocal,
        out IAnonymousFunctionOperation callback
    )
    {
        callback = null!;
        var compilation = invocation.SemanticModel?.Compilation;
        if (compilation == null)
            return false;

        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        IOperation? source;
        var callbackOrdinal = -1;
        var enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (
            SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                enumerable
            )
            && method.Name
                is "Where"
                    or "Select"
                    or "Any"
                    or "All"
                    // An ordering key selector reads the entity exactly like a predicate does.
                    or "OrderBy"
                    or "OrderByDescending"
                    // So does an aggregate selector, a counting predicate, and a partition
                    // predicate: each is invoked once per element of the materialized sequence.
                    or "Count"
                    or "LongCount"
                    or "Sum"
                    or "Average"
                    or "Min"
                    or "Max"
                    or "SkipWhile"
                    or "TakeWhile"
                    // The extraction predicates and key selectors run per element too. Their
                    // result is a tracked extraction rather than an escape, so no scalar guard.
                    or "First"
                    or "FirstOrDefault"
                    or "Single"
                    or "SingleOrDefault"
                    or "Last"
                    or "LastOrDefault"
                    or "MinBy"
                    or "MaxBy"
                    // These run per element too, but their result carries the entities onward,
                    // so they are read-analysed without becoming escape-exempt below.
                    or "ToDictionary"
                    or "ToLookup"
                    or "GroupBy"
                    or "SelectMany"
                    or "DistinctBy"
            && method.Parameters.Length == 2
            && IsIEnumerableSourceParameter(method.Parameters[0], compilation)
        )
        {
            source = GetQuerySource(invocation);
            callbackOrdinal = invocation.Instance == null ? 1 : 0;
        }
        else if (
            SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                enumerable
            )
            && method.Name is "ThenBy" or "ThenByDescending"
            && method.Parameters.Length == 2
            && IsIOrderedEnumerableSourceParameter(method.Parameters[0], compilation)
        )
        {
            // A secondary ordering chains from the primary one, which the view proof already
            // follows back to the materialized collection.
            source = GetQuerySource(invocation);
            callbackOrdinal = invocation.Instance == null ? 1 : 0;
        }
        else
        {
            var list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            if (
                !SymbolEqualityComparer.Default.Equals(
                    method.ContainingType.OriginalDefinition,
                    list
                )
                || method.Name != "ForEach"
                || method.Parameters.Length != 1
                || invocation.Instance == null
            )
            {
                return false;
            }

            source = invocation.Instance;
            callbackOrdinal = 0;
        }

        if (!IsProvenMaterializedCollectionSource(source, materializer, resultLocal, compilation))
        {
            return false;
        }

        var callbackValue = invocation
            .Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == callbackOrdinal)
            ?.Value;
        if (
            TryGetInlineAnonymousFunction(callbackValue) is not { } anonymous
            || anonymous.Symbol.Parameters.Length != 1
        )
        {
            return false;
        }

        callback = anonymous;
        return true;
    }

    /// <summary>
    /// A cheap pre-filter for collection-alias resolution: a sequence local is either an array or
    /// a constructed generic such as <c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c> or
    /// <c>IOrderedEnumerable&lt;T&gt;</c>. Entity and <c>DbContext</c> locals are neither, so they
    /// never reach the assignment lookup.
    /// </summary>
    private static bool CouldHoldASequence(ITypeSymbol? type)
    {
        return type is IArrayTypeSymbol
            || type is INamedTypeSymbol { TypeArguments.Length: > 0 };
    }

    /// <summary>Bounds alias-of-alias resolution so a pathological chain cannot walk far.</summary>
    private const int MaxCollectionAliasDepth = 8;

    private static bool IsProvenMaterializedCollectionSource(
        IOperation? source,
        IInvocationOperation materializer,
        ILocalSymbol? resultLocal,
        Compilation compilation,
        int depth = 0
    )
    {
        source = source == null ? null : UnwrapTranslatedQuery(source);
        if (
            source is IInvocationOperation directMaterializer
            && directMaterializer.Syntax.SyntaxTree == materializer.Syntax.SyntaxTree
            && directMaterializer.Syntax.Span == materializer.Syntax.Span
        )
        {
            return true;
        }

        if (resultLocal != null && source is ILocalReferenceOperation localReference)
        {
            if (SymbolEqualityComparer.Default.Equals(localReference.Local, resultLocal))
                return true;

            // `var active = orders.Where(...);` names the collection. Following the local back to
            // the single value it was given lets every consumer — a loop, an extraction, a
            // callback — read through that name exactly as it reads through the collection.
            // Only a local that could hold a sequence is worth walking the tree for: this check
            // keeps entity and context locals off the resolution path entirely.
            return CouldHoldASequence(localReference.Type)
                && depth < MaxCollectionAliasDepth
                && localReference.FindOwningExecutableRoot() is { } aliasRoot
                && LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                    aliasRoot,
                    localReference.Local,
                    localReference.Syntax.SpanStart,
                    out var aliasValue,
                    default
                )
                && IsProvenMaterializedCollectionSource(
                    aliasValue,
                    materializer,
                    resultLocal,
                    compilation,
                    depth + 1
                );
        }

        if (source is not IInvocationOperation invocation)
            return false;

        // A copy such as `orders.ToList()` is a different collection holding the same entity
        // instances. It is accepted only here, where the source is already proven to be the
        // materialized collection, so the query materializer itself can never match: its own
        // source is a DbSet, not that collection.
        return (
                IsElementPreservingInMemoryView(invocation, compilation)
                || IsSequenceCopy(invocation, compilation)
            )
            && IsProvenMaterializedCollectionSource(
                GetQuerySource(invocation),
                materializer,
                resultLocal,
                compilation,
                depth + 1
            );
    }

    /// <summary>
    /// True when the invocation is an element-preserving in-memory view whose source is the
    /// proven materialized collection. Such a view neither reshapes the sequence nor hands the
    /// entities to user code, so it is not an escape and it carries the collection's origin.
    /// </summary>
    private static bool IsElementPreservingMaterializedCollectionView(
        IInvocationOperation invocation,
        IInvocationOperation? materializer,
        ILocalSymbol? resultLocal
    )
    {
        var compilation = invocation.SemanticModel?.Compilation;
        return materializer != null
            && compilation != null
            && (
                IsElementPreservingInMemoryView(invocation, compilation)
                || IsSequenceCopy(invocation, compilation)
            )
            && IsProvenMaterializedCollectionSource(
                GetQuerySource(invocation),
                materializer,
                resultLocal,
                compilation
            );
    }

    /// <summary>
    /// True when the invocation is an exact <c>System.Linq.Enumerable</c> operator that yields
    /// the same element instances its source holds — a filter, an ordering, a page, or a
    /// widening. The entities are already materialized, so such a view is the same read as one
    /// over the collection itself. Callback-taking operators additionally require an inline,
    /// effect-free lambda: a predicate or key selector that hands the entity to a helper could
    /// have loaded the navigation itself.
    /// </summary>
    /// <summary>
    /// An exact <c>Enumerable</c> copy of a sequence. The copy is a different collection but
    /// holds the same element references, so the entities it yields have the same origin.
    /// </summary>
    private static bool IsSequenceCopy(IInvocationOperation invocation, Compilation compilation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (
            SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                enumerable
            )
        )
        {
            return method.Name is "ToList" or "ToArray" or "ToHashSet"
                && method.Parameters.Length == 1
                && IsIEnumerableSourceParameter(method.Parameters[0], compilation);
        }

        // `items.ToArray()` on a List<T> binds to the instance method, not to Enumerable —
        // the same trap as List<T>.Reverse(), which returns void and is deliberately not an
        // element-preserving view.
        var list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        return list != null
            && SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                list
            )
            && method.Name == "ToArray"
            && method.Parameters.Length == 0
            && invocation.Instance != null;
    }

    private static bool IsElementPreservingInMemoryView(
        IInvocationOperation invocation,
        Compilation compilation
    )
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (
            !SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                enumerable
            )
            || method.Parameters.Length == 0
        )
        {
            return false;
        }

        if (IsIdentityProjection(invocation, enumerable))
            return true;

        var callbackOrdinal = method.Name switch
        {
            "Where" or "SkipWhile" or "TakeWhile" when method.Parameters.Length == 2 => 1,
            "OrderBy"
            or "OrderByDescending"
            or "ThenBy"
            or "ThenByDescending" when method.Parameters.Length == 2 => 1,
            "Skip" or "Take" when method.Parameters.Length == 2 => -1,
            "Distinct" or "Reverse" or "AsEnumerable" when method.Parameters.Length == 1 => -1,
            _ => -2,
        };

        if (callbackOrdinal == -2)
            return false;

        // ThenBy chains from IOrderedEnumerable<T>; every other operator takes IEnumerable<T>.
        var takesOrderedSource = method.Name is "ThenBy" or "ThenByDescending";
        if (
            takesOrderedSource
                ? !IsIOrderedEnumerableSourceParameter(method.Parameters[0], compilation)
                : !IsIEnumerableSourceParameter(method.Parameters[0], compilation)
        )
        {
            return false;
        }

        if (callbackOrdinal < 0)
            return true;

        var callbackArgument = invocation
            .Arguments.FirstOrDefault(argument =>
                argument.Parameter?.Ordinal == callbackOrdinal
            )
            ?.Value;
        return TryGetInlineAnonymousFunction(callbackArgument) is { } callback
            && callback.Symbol.Parameters.Length == 1
            && IsEffectFreeCallback(callback);
    }

    private static bool IsIOrderedEnumerableSourceParameter(
        IParameterSymbol parameter,
        Compilation compilation
    )
    {
        var ordered = compilation.GetTypeByMetadataName("System.Linq.IOrderedEnumerable`1");
        return ordered != null
            && SymbolEqualityComparer.Default.Equals(
                parameter.Type.OriginalDefinition,
                ordered
            );
    }

    private static IAnonymousFunctionOperation? TryGetInlineAnonymousFunction(IOperation? operation)
    {
        operation = operation?.UnwrapConversions();
        return operation switch
        {
            IAnonymousFunctionOperation anonymous => anonymous,
            IDelegateCreationOperation delegateCreation =>
                delegateCreation.Target.UnwrapConversions() as IAnonymousFunctionOperation,
            _ => null,
        };
    }

    private static bool IsEffectFreeSupportedCollectionCallback(
        IInvocationOperation invocation,
        IInvocationOperation? materializer,
        ILocalSymbol? resultLocal
    )
    {
        if (
            materializer == null
            || !TryGetSupportedCollectionCallback(
                invocation,
                materializer,
                resultLocal,
                out var callback
            )
        )
            return false;

        var operatorName = (invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod).Name;

        // These keep the entities in their result — a dictionary, a lookup, groupings, a
        // flattened sequence — so whoever holds it may load the navigation. The callback read is
        // still analysed; the call itself stays an escape regardless of what the callback returns.
        if (
            operatorName
            is "ToDictionary"
                or "ToLookup"
                or "GroupBy"
                or "SelectMany"
                or "DistinctBy"
        )
        {
            return false;
        }

        // Select, Min and Max hand their callback's result back to the caller, so an
        // entity-returning callback is a projection boundary rather than a scalar read.
        if (operatorName is "Select" or "Min" or "Max" && !IsProvablyScalarSelectResult(callback))
        {
            return false;
        }

        return IsEffectFreeCallback(callback);
    }

    private static bool IsEffectFreeCallback(IAnonymousFunctionOperation callback)
    {
        var parameter = callback.Symbol.Parameters[0];
        foreach (var descendant in callback.Descendants())
        {
            if (
                descendant is IAnonymousFunctionOperation nested
                && !ReferenceEquals(nested, callback)
                && nested.ReferencesParameter(parameter)
            )
            {
                return false;
            }

            if (descendant is IInvocationOperation call && IsDirectParameterCall(call, parameter))
            {
                return false;
            }

            if (
                descendant is IAssignmentOperation assignment
                && (
                    IsParameterRooted(assignment.Target, parameter)
                    || IsDirectParameterReference(assignment.Value, parameter)
                )
            )
            {
                return false;
            }

            if (
                descendant is IVariableDeclaratorOperation declarator
                && declarator.Initializer != null
                && IsDirectParameterReference(declarator.Initializer.Value, parameter)
            )
            {
                return false;
            }

            if (
                descendant is IReturnOperation returnOperation
                && returnOperation.ReturnedValue != null
                && IsDirectParameterReference(returnOperation.ReturnedValue, parameter)
            )
            {
                return false;
            }
        }

        return !IsDirectParameterReference(callback.Body, parameter);
    }

    private static bool IsProvablyScalarSelectResult(IAnonymousFunctionOperation callback)
    {
        var type = callback.Symbol.ReturnType;
        return type?.IsValueType == true || type?.SpecialType == SpecialType.System_String;
    }

    private static bool IsDirectParameterCall(
        IInvocationOperation invocation,
        IParameterSymbol parameter
    )
    {
        if (IsDirectParameterReference(invocation.Instance, parameter))
            return true;

        foreach (var argument in invocation.Arguments)
        {
            if (IsDirectParameterReference(argument.Value, parameter))
                return true;
        }

        return false;
    }

    private static bool IsDirectParameterReference(
        IOperation? operation,
        IParameterSymbol parameter
    )
    {
        return operation?.UnwrapConversions() is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);
    }

    private static bool IsParameterRooted(IOperation operation, IParameterSymbol parameter)
    {
        for (var current = operation.UnwrapConversions(); current != null; )
        {
            if (current is IParameterReferenceOperation reference)
                return SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);

            current = current switch
            {
                IPropertyReferenceOperation property => property.Instance?.UnwrapConversions(),
                IConditionalAccessOperation conditional =>
                    conditional.Operation.UnwrapConversions(),
                _ => null,
            };
        }

        return false;
    }

    /// <summary>
    /// Query-comprehension syntax wraps its lowered method chain in an
    /// <see cref="ITranslatedQueryOperation"/>. Peeling it lets one chain walk serve both
    /// <c>from o in db.Orders select o</c> and the <c>db.Orders.Select(o =&gt; o)</c> it lowers to.
    /// </summary>
    private static IOperation UnwrapTranslatedQuery(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        while (current is ITranslatedQueryOperation translated)
            current = translated.Operation.UnwrapConversions();

        return current;
    }

    /// <summary>
    /// <c>Select(x =&gt; x)</c> reshapes nothing: it yields the very instances its source produced.
    /// LINQ query syntax lowers every trailing <c>select x</c> to exactly this projection, so
    /// refusing to see through it would hide the missing eager load behind ordinary
    /// query-comprehension syntax. Only an inline single-parameter lambda whose body is that same
    /// parameter qualifies, and its return type must be the parameter's own type — an upcast or any
    /// other selector is a real projection and stays out of scope.
    /// </summary>
    private static bool IsIdentityProjection(
        IInvocationOperation invocation,
        INamedTypeSymbol? linqType
    )
    {
        if (linqType == null)
            return false;

        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (
            method.Name != "Select"
            || !SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                linqType
            )
        )
        {
            return false;
        }

        var selector = invocation
            .Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)
            ?.Value;
        if (
            TryGetInlineAnonymousFunction(selector) is not { } callback
            || callback.Symbol.Parameters.Length != 1
        )
        {
            return false;
        }

        var parameter = callback.Symbol.Parameters[0];
        return SymbolEqualityComparer.Default.Equals(parameter.Type, callback.Symbol.ReturnType)
            && TryGetCallbackReturnedValue(callback) is { } returned
            && IsDirectParameterReference(returned, parameter);
    }

    /// <summary>
    /// The single value an expression-bodied or single-<c>return</c> callback yields, or
    /// <see langword="null"/> when the body does anything else.
    /// </summary>
    private static IOperation? TryGetCallbackReturnedValue(IAnonymousFunctionOperation callback)
    {
        if (callback.Body is not { } body || body.Operations.Length != 1)
            return null;

        return body.Operations[0] is IReturnOperation { ReturnedValue: { } value } ? value : null;
    }

    private static bool IsExactShapePreservingQueryStep(IInvocationOperation invocation)
    {
        var compilation = invocation.SemanticModel?.Compilation;
        if (compilation == null)
            return false;

        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var containingType = method.ContainingType.OriginalDefinition;
        var queryable = compilation.GetTypeByMetadataName("System.Linq.Queryable");
        if (SymbolEqualityComparer.Default.Equals(containingType, queryable))
        {
            return ShapePreservingQueryableOperators.Contains(method.Name)
                || IsIdentityProjection(invocation, queryable);
        }

        var entityFramework = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
        );
        if (SymbolEqualityComparer.Default.Equals(containingType, entityFramework))
            return ShapePreservingEntityFrameworkOperators.Contains(method.Name);

        var relational = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions"
        );
        if (SymbolEqualityComparer.Default.Equals(containingType, relational))
        {
            return method.Name is "AsSplitQuery" or "AsSingleQuery"
                || IsExactFromSqlQueryRoot(method, compilation);
        }

        return false;
    }

    private static bool IsExactFromSqlQueryRoot(IMethodSymbol method, Compilation compilation)
    {
        if (
            method.Name is not ("FromSql" or "FromSqlRaw" or "FromSqlInterpolated")
            || method.Parameters.Length is < 2 or > 3
        )
        {
            return false;
        }

        var dbSet = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");
        if (
            !SymbolEqualityComparer.Default.Equals(
                method.Parameters[0].Type.OriginalDefinition,
                dbSet
            )
        )
        {
            return false;
        }

        var formattableString = compilation.GetTypeByMetadataName("System.FormattableString");
        return method.Name switch
        {
            "FromSql" or "FromSqlInterpolated" => method.Parameters.Length == 2
                && SymbolEqualityComparer.Default.Equals(
                    method.Parameters[1].Type,
                    formattableString
                ),
            "FromSqlRaw" => method.Parameters.Length == 3
                && method.Parameters[1].Type.SpecialType == SpecialType.System_String
                && method.Parameters[2].IsParams,
            _ => false,
        };
    }

    private static bool IsExactToHashSetMaterializer(
        IInvocationOperation invocation,
        Compilation compilation
    )
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (
            SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                enumerable
            )
        )
        {
            return method.Name == "ToHashSet"
                && method.Parameters.Length is 1 or 2
                && IsIEnumerableSourceParameter(method.Parameters[0], compilation);
        }

        var entityFramework = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
        );
        if (
            !SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                entityFramework
            )
            || method.Name != "ToHashSetAsync"
            || !method.Parameters[0].Type.IsIQueryable()
        )
        {
            return false;
        }

        var cancellationToken = compilation.GetTypeByMetadataName(
            "System.Threading.CancellationToken"
        );
        var comparer = compilation.GetTypeByMetadataName(
            "System.Collections.Generic.IEqualityComparer`1"
        );
        return method.Parameters.Length == 2
                && SymbolEqualityComparer.Default.Equals(
                    method.Parameters[1].Type,
                    cancellationToken
                )
            || method.Parameters.Length == 3
                && SymbolEqualityComparer.Default.Equals(
                    method.Parameters[1].Type.OriginalDefinition,
                    comparer
                )
                && SymbolEqualityComparer.Default.Equals(
                    method.Parameters[2].Type,
                    cancellationToken
                );
    }

    private static bool IsExactQueryableElementAtMaterializer(
        IInvocationOperation invocation,
        Compilation compilation
    )
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var queryable = compilation.GetTypeByMetadataName("System.Linq.Queryable");
        if (
            SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                queryable
            )
        )
        {
            return method.Name is "ElementAt" or "ElementAtOrDefault"
                && method.Parameters.Length == 2
                && method.Parameters[0].Type.IsIQueryable()
                && method.Parameters[1].Type.SpecialType == SpecialType.System_Int32;
        }

        var entityFramework = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
        );
        var cancellationToken = compilation.GetTypeByMetadataName(
            "System.Threading.CancellationToken"
        );
        return SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                entityFramework
            )
            && method.Name is "ElementAtAsync" or "ElementAtOrDefaultAsync"
            && method.Parameters.Length == 3
            && method.Parameters[0].Type.IsIQueryable()
            && method.Parameters[1].Type.SpecialType == SpecialType.System_Int32
            && SymbolEqualityComparer.Default.Equals(method.Parameters[2].Type, cancellationToken);
    }

    private static bool IsIEnumerableSourceParameter(
        IParameterSymbol parameter,
        Compilation compilation
    )
    {
        return SymbolEqualityComparer.Default.Equals(
            parameter.Type.OriginalDefinition,
            compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T)
        );
    }
}
