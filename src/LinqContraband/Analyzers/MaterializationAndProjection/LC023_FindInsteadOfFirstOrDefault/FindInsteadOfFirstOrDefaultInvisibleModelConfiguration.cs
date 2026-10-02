using System;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis
{
    internal sealed partial class PrimaryKeyCache
    {
        private const int SourceContextsNotScanned = 0;
        private const int SourceContextsVisible = 1;
        private const int SourceContextHasInvisibleConfiguration = 2;

        // Written under syncRoot once the first lookup on a DbSet with an unknown context ran.
        private volatile int sourceContextState;

        /// <summary>
        /// The DbSet's context is unknown (a DbSet parameter or field). Any DbContext declared in
        /// this compilation could own it, so stay quiet when one of them has model configuration
        /// the scan cannot read. Contexts declared only in other assemblies remain unseen.
        /// </summary>
        private bool AnySourceContextHasInvisibleModelConfiguration(CancellationToken cancellationToken)
        {
            var state = sourceContextState;
            if (state != SourceContextsNotScanned)
                return state == SourceContextHasInvisibleConfiguration;

            lock (syncRoot)
            {
                state = sourceContextState;
                if (state != SourceContextsNotScanned)
                    return state == SourceContextHasInvisibleConfiguration;

                var result = AnyContextHasInvisibleModelConfiguration(compilation.Assembly.GlobalNamespace, cancellationToken);
                sourceContextState = result ? SourceContextHasInvisibleConfiguration : SourceContextsVisible;
                return result;
            }
        }

        private bool AnyContextHasInvisibleModelConfiguration(
            INamespaceOrTypeSymbol container,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (container is INamespaceSymbol namespaceSymbol)
            {
                foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
                {
                    if (AnyContextHasInvisibleModelConfiguration(nestedNamespace, cancellationToken))
                        return true;
                }
            }

            foreach (var type in container.GetTypeMembers())
            {
                if (type.TypeKind == TypeKind.Class &&
                    type.IsDbContext() &&
                    HasInvisibleModelConfiguration(type, compilation))
                {
                    return true;
                }

                if (AnyContextHasInvisibleModelConfiguration(type, cancellationToken))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// True when the context's model may configure query filters LC023 cannot see: the context
    /// or one of its base types overrides OnModelCreating in another assembly (fullstackhero's
    /// BaseDbContext), or it derives from Finbuckle's multi-tenant contexts. EF Core's own
    /// DbContext and Microsoft's bases such as IdentityDbContext add no query filters, so their
    /// OnModelCreating does not count; overrides in this compilation are read by the scan.
    /// </summary>
    private static bool HasInvisibleModelConfiguration(ITypeSymbol contextType, Compilation compilation)
    {
        for (var current = contextType; current != null; current = current.BaseType)
        {
            if (current.Name == "DbContext" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore")
            {
                return false;
            }

            if (IsFinbuckleSymbol(current))
                return true;

            if (SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, compilation.Assembly) ||
                IsMicrosoftSymbol(current))
            {
                continue;
            }

            foreach (var member in current.GetMembers("OnModelCreating"))
            {
                if (member is IMethodSymbol { IsOverride: true })
                    return true;
            }
        }

        return false;
    }

    private static bool IsEntityFrameworkCoreSymbol(ISymbol? symbol)
    {
        var namespaceName = symbol?.ContainingNamespace?.ToDisplayString();
        return namespaceName != null &&
               (namespaceName == "Microsoft.EntityFrameworkCore" ||
                namespaceName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal));
    }

    private static bool IsFinbuckleSymbol(ISymbol symbol)
    {
        var namespaceName = symbol.ContainingNamespace?.ToDisplayString();
        return namespaceName != null &&
               (namespaceName == "Finbuckle" || namespaceName.StartsWith("Finbuckle.", StringComparison.Ordinal));
    }

    private static bool IsMicrosoftSymbol(ISymbol symbol)
    {
        var namespaceName = symbol.ContainingNamespace?.ToDisplayString();
        return namespaceName != null && namespaceName.StartsWith("Microsoft.", StringComparison.Ordinal);
    }
}
