using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis
{
    internal sealed partial class PrimaryKeyCache
    {
        private readonly ConcurrentDictionary<ITypeSymbol, byte> queryFilteredEntities =
            new(SymbolEqualityComparer.Default);

        // Entity<T>().HasQueryFilter(...) inside a generic helper: every entity that satisfies
        // T's constraints may be filtered.
        private readonly ConcurrentDictionary<ITypeParameterSymbol, byte> queryFilteredTypeParameters =
            new(SymbolEqualityComparer.Default);

        // A filter whose entity cannot be named (Entity(type) with a runtime Type, or
        // IMutableEntityType.SetQueryFilter in a loop): any entity may be filtered.
        private volatile bool hasUnresolvedQueryFilter;

        public void RegisterQueryFilter(IInvocationOperation invocation)
        {
            var methodName = invocation.TargetMethod.Name;
            if (methodName == "SetQueryFilter")
            {
                if (IsEntityTypeMetadataMethod(invocation.TargetMethod))
                    hasUnresolvedQueryFilter = true;

                return;
            }

            // Finbuckle's IsMultiTenant() adds a tenant query filter to the entity. A lookalike from
            // any other library does not.
            if (methodName == "IsMultiTenant")
            {
                var original = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
                if (original.ContainingType == null || !IsFinbuckleSymbol(original.ContainingType))
                    return;
            }
            else if (methodName != "HasQueryFilter")
            {
                return;
            }

            if (TryGetEntityTypeBuilderEntity(invocation.GetInvocationReceiverType(), out var entityType))
            {
                RegisterFilteredEntity(entityType);
                return;
            }

            // modelBuilder.Entity(typeof(User)).HasQueryFilter(...): the non-generic builder
            // carries no type argument; recover the entity from the Entity(Type) call's typeof.
            if (invocation.GetInvocationReceiverType() is INamedTypeSymbol receiverType &&
                !receiverType.IsGenericType &&
                IsEntityTypeBuilder(receiverType))
            {
                if (invocation.GetInvocationReceiver() is IInvocationOperation entityCall &&
                    entityCall.TargetMethod.Name == "Entity" &&
                    entityCall.Arguments.Length >= 1 &&
                    entityCall.Arguments[0].Value.UnwrapConversions() is ITypeOfOperation typeOf)
                {
                    RegisterFilteredEntity(typeOf.TypeOperand);
                    return;
                }

                // Entity(type) with a runtime Type, typically a loop over the model's entity
                // types (fullstackhero's AppendGlobalQueryFilter<ISoftDeletable>).
                hasUnresolvedQueryFilter = true;
            }
        }

        /// <summary>
        /// EF Core's <c>SetQueryFilter</c> on entity-type metadata: an <c>IMutableEntityType</c> or
        /// <c>IConventionEntityType</c> member, or an EF Core extension whose first parameter is one. A method of the
        /// same name on any other type does not configure a query filter, so it does not silence the rule.
        /// </summary>
        private static bool IsEntityTypeMetadataMethod(IMethodSymbol method)
        {
            var original = method.ReducedFrom ?? method;
            if (!IsEntityFrameworkCoreSymbol(original.ContainingType))
                return false;

            var receiverType = original.IsExtensionMethod && original.Parameters.Length > 0
                ? original.Parameters[0].Type
                : original.ContainingType;
            return receiverType is INamedTypeSymbol named &&
                   (IsEntityTypeMetadata(named) || named.AllInterfaces.Any(IsEntityTypeMetadata));
        }

        private static bool IsEntityTypeMetadata(INamedTypeSymbol type)
        {
            var namespaceName = type.ContainingNamespace?.ToDisplayString();
            return namespaceName != null &&
                   namespaceName.StartsWith("Microsoft.EntityFrameworkCore.Metadata", System.StringComparison.Ordinal) &&
                   type.Name.EndsWith("EntityType", System.StringComparison.Ordinal);
        }

        private void RegisterFilteredEntity(ITypeSymbol entityType)
        {
            if (entityType is ITypeParameterSymbol typeParameter)
            {
                queryFilteredTypeParameters.TryAdd(typeParameter, 0);
                return;
            }

            queryFilteredEntities.TryAdd(entityType, 0);
        }

        /// <summary>
        /// True when the entity type may carry a global query filter: one registered for it or
        /// a base type (EF declares filters on the hierarchy root and propagates them down), a
        /// generic helper whose constraints it satisfies, a filter loop whose entities cannot
        /// be named, a Finbuckle [MultiTenant] attribute, or a context whose model configuration
        /// lives in an assembly this compilation cannot read. Find's change-tracker hit bypasses
        /// query filters, so the FirstOrDefault-to-Find advice is wrong there.
        /// </summary>
        public bool MayHaveQueryFilter(
            ITypeSymbol entityType,
            ITypeSymbol? contextType,
            CancellationToken cancellationToken)
        {
            EnsureFullyScanned(cancellationToken);
            if (hasUnresolvedQueryFilter ||
                HasRegisteredQueryFilter(entityType) ||
                HasMultiTenantAttribute(entityType))
            {
                return true;
            }

            return contextType != null
                ? HasInvisibleModelConfiguration(contextType, compilation)
                : AnySourceContextHasInvisibleModelConfiguration(cancellationToken);
        }

        private bool HasRegisteredQueryFilter(ITypeSymbol entityType)
        {
            for (ITypeSymbol? current = entityType; current != null; current = current.BaseType)
            {
                if (queryFilteredEntities.ContainsKey(current))
                    return true;
            }

            foreach (var typeParameter in queryFilteredTypeParameters.Keys)
            {
                if (SatisfiesConstraints(entityType, typeParameter))
                    return true;
            }

            return false;
        }

        private static bool SatisfiesConstraints(ITypeSymbol entityType, ITypeParameterSymbol typeParameter)
        {
            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                // A constraint that mentions another type parameter cannot be checked here;
                // assume it holds.
                if (constraint is not INamedTypeSymbol namedConstraint || ContainsTypeParameter(namedConstraint))
                    continue;

                if (!IsAssignableTo(entityType, namedConstraint))
                    return false;
            }

            return true;
        }

        private static bool ContainsTypeParameter(INamedTypeSymbol type)
        {
            foreach (var argument in type.TypeArguments)
            {
                if (argument is ITypeParameterSymbol ||
                    (argument is INamedTypeSymbol namedArgument && ContainsTypeParameter(namedArgument)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAssignableTo(ITypeSymbol type, INamedTypeSymbol target)
        {
            for (ITypeSymbol? current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, target))
                    return true;
            }

            foreach (var implemented in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented, target))
                    return true;
            }

            return false;
        }

        private static bool HasMultiTenantAttribute(ITypeSymbol entityType)
        {
            for (ITypeSymbol? current = entityType; current != null; current = current.BaseType)
            {
                foreach (var attribute in current.GetAttributes())
                {
                    if (attribute.AttributeClass is { Name: "MultiTenantAttribute" } attributeClass &&
                        IsFinbuckleSymbol(attributeClass))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
