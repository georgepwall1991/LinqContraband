using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Which method bodies a call can reach. A statically bound call reaches one. An interface, abstract or virtual call
/// reaches the implementations this compilation declares for the interface or for the receiver's class; LC007
/// follows it only when that type is declared here and it has between one and
/// <see cref="MaxImplementations"/> implementations, and callers then require every one of them to run a query.
/// Implementations in other projects (test fakes, plug-ins) are out of sight and are not counted.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    internal const int MaxImplementations = 8;

    private static ImmutableArray<IMethodSymbol> GetHelperTargets(
        IInvocationOperation invocation,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        return GetHelperTargets(
            invocation.TargetMethod,
            invocation.IsVirtual,
            invocation.Instance?.Type,
            cache,
            cancellationToken);
    }

    private static ImmutableArray<IMethodSymbol> GetHelperTargets(
        IMethodSymbol method,
        bool isVirtual,
        ITypeSymbol? receiverType,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        method = method.ReducedFrom ?? method;
        method = method.OriginalDefinition;
        method = method.PartialImplementationPart ?? method;

        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) || method.IsExtern)
            return ImmutableArray<IMethodSymbol>.Empty;

        if (!isVirtual)
        {
            return method.IsAbstract || method.DeclaringSyntaxReferences.Length == 0
                ? ImmutableArray<IMethodSymbol>.Empty
                : ImmutableArray.Create(method);
        }

        var containingType = method.ContainingType;
        if (containingType == null)
            return ImmutableArray<IMethodSymbol>.Empty;

        return containingType.TypeKind == TypeKind.Interface
            ? GetInterfaceImplementations(method, containingType, cache, cancellationToken)
            : GetOverrides(method, receiverType as INamedTypeSymbol ?? containingType, cache, cancellationToken);
    }

    private static ImmutableArray<IMethodSymbol> GetInterfaceImplementations(
        IMethodSymbol interfaceMethod,
        INamedTypeSymbol @interface,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        if (!IsDeclaredIn(@interface, cache.Compilation))
            return ImmutableArray<IMethodSymbol>.Empty;

        var types = cache.GetSourceTypes(cancellationToken).GetImplementers(@interface);
        if (types.Count == 0 || types.Count > MaxImplementations)
            return ImmutableArray<IMethodSymbol>.Empty;

        var targets = ImmutableArray.CreateBuilder<IMethodSymbol>(types.Count);
        foreach (var type in types)
        {
            var found = false;
            foreach (var constructed in type.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(constructed.OriginalDefinition, @interface))
                    continue;

                foreach (var member in constructed.GetMembers(interfaceMethod.Name))
                {
                    if (!SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, interfaceMethod))
                        continue;

                    // Every construction of the interface the type implements must resolve (IHandler<A>, IHandler<B>).
                    if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation)
                        return ImmutableArray<IMethodSymbol>.Empty;

                    AddTarget(targets, implementation);
                    found = true;
                }
            }

            if (!found)
                return ImmutableArray<IMethodSymbol>.Empty;
        }

        return targets.Count <= MaxImplementations ? targets.ToImmutable() : ImmutableArray<IMethodSymbol>.Empty;
    }

    private static ImmutableArray<IMethodSymbol> GetOverrides(
        IMethodSymbol method,
        INamedTypeSymbol receiverType,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        receiverType = receiverType.OriginalDefinition;
        if (receiverType.TypeKind != TypeKind.Class || !IsDeclaredIn(receiverType, cache.Compilation))
            return ImmutableArray<IMethodSymbol>.Empty;

        var derivedTypes = cache.GetSourceTypes(cancellationToken).GetDerived(receiverType);
        if (derivedTypes.Count >= MaxImplementations)
            return ImmutableArray<IMethodSymbol>.Empty;

        var root = GetVirtualRoot(method);
        var targets = ImmutableArray.CreateBuilder<IMethodSymbol>(derivedTypes.Count + 1);
        if (!receiverType.IsAbstract && !TryAddMostDerivedOverride(targets, receiverType, root))
            return ImmutableArray<IMethodSymbol>.Empty;

        foreach (var type in derivedTypes)
        {
            if (!TryAddMostDerivedOverride(targets, type, root))
                return ImmutableArray<IMethodSymbol>.Empty;
        }

        return targets.ToImmutable();
    }

    private static bool TryAddMostDerivedOverride(
        ImmutableArray<IMethodSymbol>.Builder targets,
        INamedTypeSymbol type,
        IMethodSymbol root)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(root.Name))
            {
                if (member is IMethodSymbol candidate &&
                    SymbolEqualityComparer.Default.Equals(GetVirtualRoot(candidate), root))
                {
                    AddTarget(targets, candidate);
                    return true;
                }
            }
        }

        return false;
    }

    private static IMethodSymbol GetVirtualRoot(IMethodSymbol method)
    {
        while (method.OverriddenMethod != null)
            method = method.OverriddenMethod;

        return method.OriginalDefinition;
    }

    private static void AddTarget(ImmutableArray<IMethodSymbol>.Builder targets, IMethodSymbol implementation)
    {
        implementation = implementation.OriginalDefinition;
        implementation = implementation.PartialImplementationPart ?? implementation;
        foreach (var existing in targets)
        {
            if (SymbolEqualityComparer.Default.Equals(existing, implementation))
                return;
        }

        targets.Add(implementation);
    }

    private static bool IsDeclaredIn(INamedTypeSymbol type, Compilation compilation)
    {
        return type.DeclaringSyntaxReferences.Length > 0 &&
               SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);
    }

    /// <summary>
    /// The execution (by the cache's matcher) that a call performs once per call through the helpers it reaches,
    /// or an empty string. Used by LC010 to find helpers that save.
    /// </summary>
    internal static string GetExecutionThroughHelpers(
        IInvocationOperation call,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        return GetCallExecution(call, MaxHelperDepth, caller: null, cache, cancellationToken);
    }

    /// <summary>
    /// The query method a call runs once per call, through every body it can reach, or
    /// <see cref="NPlusOneLooperHelperCache.NoExecution"/>. An implementation that is the caller itself
    /// (<paramref name="caller"/>, a decorator forwarding to its own interface method, or plain recursion) has no
    /// say.
    /// </summary>
    private static string GetCallExecution(
        IInvocationOperation call,
        int depth,
        IMethodSymbol? caller,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        return GetExecution(GetHelperTargets(call, cache, cancellationToken), depth, caller, cache, cancellationToken);
    }

    private static string GetExecution(
        ImmutableArray<IMethodSymbol> targets,
        int depth,
        IMethodSymbol? caller,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        string? queryMethodName = null;
        foreach (var target in targets)
        {
            if (caller != null && SymbolEqualityComparer.Default.Equals(target, caller))
                continue;

            var execution = GetHelperExecution(target, depth, cache, cancellationToken);
            if (execution.Length == 0)
                return NPlusOneLooperHelperCache.NoExecution;

            queryMethodName ??= execution;
        }

        return queryMethodName ?? NPlusOneLooperHelperCache.NoExecution;
    }
}
