using System.Collections.Concurrent;
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

        public void RegisterQueryFilter(IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.Name != "HasQueryFilter")
                return;

            if (TryGetEntityTypeBuilderEntity(invocation.GetInvocationReceiverType(), out var entityType))
            {
                queryFilteredEntities.TryAdd(entityType, 0);
                return;
            }

            // modelBuilder.Entity(typeof(User)).HasQueryFilter(...): the non-generic builder
            // carries no type argument; recover the entity from the Entity(Type) call's typeof.
            if (invocation.GetInvocationReceiverType() is INamedTypeSymbol receiverType &&
                !receiverType.IsGenericType &&
                IsEntityTypeBuilder(receiverType) &&
                invocation.GetInvocationReceiver() is IInvocationOperation entityCall &&
                entityCall.TargetMethod.Name == "Entity" &&
                entityCall.Arguments.Length >= 1 &&
                entityCall.Arguments[0].Value.UnwrapConversions() is ITypeOfOperation typeOf &&
                typeOf.TypeOperand is INamedTypeSymbol namedOperand)
            {
                queryFilteredEntities.TryAdd(namedOperand, 0);
            }
        }

        /// <summary>
        /// True when the entity type - or a base type, since EF declares filters on the
        /// hierarchy root and propagates them down - has a visible global query filter.
        /// Find's change-tracker hit bypasses query filters, so the FirstOrDefault-to-Find
        /// advice is wrong there. Without full-scan permission an unseen filter cannot be
        /// ruled out, but the rule keeps reporting - the same trade-off the convention-key
        /// fallback already makes.
        /// </summary>
        public bool HasQueryFilter(ITypeSymbol entityType, CancellationToken cancellationToken)
        {
            if (HasRegisteredQueryFilter(entityType))
                return true;

            if (!allowFullScan)
                return false;

            EnsureFullyScanned(cancellationToken);
            return HasRegisteredQueryFilter(entityType);
        }

        private bool HasRegisteredQueryFilter(ITypeSymbol entityType)
        {
            for (ITypeSymbol? current = entityType; current != null; current = current.BaseType)
            {
                if (queryFilteredEntities.ContainsKey(current))
                    return true;
            }

            return false;
        }
    }
}
