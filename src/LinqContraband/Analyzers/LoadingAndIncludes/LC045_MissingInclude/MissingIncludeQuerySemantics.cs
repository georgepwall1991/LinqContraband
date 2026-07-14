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
            && method.Name is "Where" or "Select" or "Any" or "All"
            && method.Parameters.Length == 2
            && IsIEnumerableSourceParameter(method.Parameters[0], compilation)
        )
        {
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

    private static bool IsProvenMaterializedCollectionSource(
        IOperation? source,
        IInvocationOperation materializer,
        ILocalSymbol? resultLocal,
        Compilation compilation
    )
    {
        source = source?.UnwrapConversions();
        if (
            source is IInvocationOperation directMaterializer
            && directMaterializer.Syntax.SyntaxTree == materializer.Syntax.SyntaxTree
            && directMaterializer.Syntax.Span == materializer.Syntax.Span
        )
        {
            return true;
        }

        if (resultLocal != null && source is ILocalReferenceOperation localReference)
            return SymbolEqualityComparer.Default.Equals(localReference.Local, resultLocal);

        if (source is not IInvocationOperation invocation)
            return false;

        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        return SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                enumerable
            )
            && method.Name == "Where"
            && method.Parameters.Length == 2
            && IsIEnumerableSourceParameter(method.Parameters[0], compilation)
            && TryGetInlineAnonymousFunction(
                invocation
                    .Arguments.FirstOrDefault(argument =>
                        argument.Parameter?.Ordinal == (invocation.Instance == null ? 1 : 0)
                    )
                    ?.Value
            )
                is { } predicate
            && predicate.Symbol.Parameters.Length == 1
            && IsEffectFreeCallback(predicate)
            && IsProvenMaterializedCollectionSource(
                GetQuerySource(invocation),
                materializer,
                resultLocal,
                compilation
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

        if (
            (invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod).Name == "Select"
            && !IsProvablyScalarSelectResult(callback)
        )
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

    private static bool IsExactShapePreservingQueryStep(IInvocationOperation invocation)
    {
        var compilation = invocation.SemanticModel?.Compilation;
        if (compilation == null)
            return false;

        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var containingType = method.ContainingType.OriginalDefinition;
        var queryable = compilation.GetTypeByMetadataName("System.Linq.Queryable");
        if (SymbolEqualityComparer.Default.Equals(containingType, queryable))
            return ShapePreservingQueryableOperators.Contains(method.Name);

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
