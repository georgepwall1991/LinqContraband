using System;
using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A call target whose body this compilation cannot see because it lives in another project of the solution:
/// a method of a referenced project, or an interface or abstract method that no type of this project implements.
/// </summary>
internal readonly struct CrossProjectCallee
{
    /// <summary>A statically bound call: one body.</summary>
    public const char Direct = 'n';

    /// <summary>An interface or abstract method: only its implementations have bodies.</summary>
    public const char Dispatch = 'a';

    /// <summary>A virtual method with a body: that body and every override.</summary>
    public const char VirtualWithBody = 'v';

    public CrossProjectCallee(char kind, string id, string rootId, string name)
    {
        Kind = kind;
        Id = id;
        RootId = rootId;
        Name = name;
    }

    public char Kind { get; }

    /// <summary>The documentation comment ID of the called method's definition, which is the same in every project.</summary>
    public string Id { get; }

    /// <summary>The ID implementations and overrides are recorded under: the interface method or the virtual root.</summary>
    public string RootId { get; }

    public string Name { get; }
}

/// <summary>
/// The pieces of helper tracking that <c>LinqContraband.Scan</c> needs to follow a call from one project into
/// another. A compiler sees a project reference as an assembly without method bodies, so no analyzer can do this
/// during a build; the scanner has every project's facts and joins them.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    internal static bool TryGetCrossProjectCallee(
        IInvocationOperation call,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken,
        out CrossProjectCallee callee)
    {
        return TryGetCrossProjectCallee(
            call.TargetMethod,
            call.IsVirtual,
            call.Instance?.Type,
            cache,
            cancellationToken,
            out callee);
    }

    /// <summary>
    /// The method is one whose bodies are all outside this compilation, and it is declared in this project or in
    /// one that looks like another project of the solution (not the framework and not a NuGet package).
    /// </summary>
    internal static bool TryGetCrossProjectCallee(
        IMethodSymbol method,
        bool isVirtual,
        ITypeSymbol? receiverType,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken,
        out CrossProjectCallee callee)
    {
        callee = default;
        method = method.ReducedFrom ?? method;
        method = method.OriginalDefinition;
        if (method.MethodKind != MethodKind.Ordinary || method.IsExtern || method.ContainingType is not { } containingType)
            return false;

        if (!GetHelperTargets(method, isVirtual, receiverType, cache, cancellationToken).IsEmpty)
            return false;

        char kind;
        if (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, cache.Compilation.Assembly))
        {
            // Declared here: only a call that dispatches to implementations no type of this project provides.
            if (!isVirtual || !method.IsAbstract)
                return false;

            var sourceTypes = cache.GetSourceTypes(cancellationToken);
            var implementations = containingType.TypeKind == TypeKind.Interface
                ? sourceTypes.GetImplementers(containingType).Count
                : sourceTypes.GetDerived(containingType).Count;
            if (implementations != 0)
                return false;

            kind = CrossProjectCallee.Dispatch;
        }
        else
        {
            if (!IsProjectLikeAssembly(method.ContainingAssembly, cache.Compilation))
                return false;

            kind = !isVirtual
                ? CrossProjectCallee.Direct
                : method.IsAbstract ? CrossProjectCallee.Dispatch : CrossProjectCallee.VirtualWithBody;
        }

        var id = method.GetDocumentationCommentId();
        var rootId = kind == CrossProjectCallee.Direct ? id : GetVirtualRoot(method).GetDocumentationCommentId();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(rootId))
            return false;

        callee = new CrossProjectCallee(kind, id!, rootId!, method.Name);
        return true;
    }

    /// <summary>
    /// An assembly that can be another project of the same solution: not this compilation's own, not part of the
    /// framework, and not restored from a NuGet package folder.
    /// </summary>
    internal static bool IsProjectLikeAssembly(IAssemblySymbol? assembly, Compilation compilation)
    {
        if (assembly == null || SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
            return false;

        var name = assembly.Name;
        if (name == "System" || name == "mscorlib" || name == "netstandard" ||
            name.StartsWith("System.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.", StringComparison.Ordinal))
        {
            return false;
        }

        if (compilation.GetMetadataReference(assembly) is PortableExecutableReference { FilePath: { Length: > 0 } path })
        {
            path = path.Replace('\\', '/');
            if (path.IndexOf("/.nuget/packages/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/NuGetFallbackFolder/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/packs/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/dotnet/shared/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The ID a method's facts are recorded under: its definition's documentation comment ID.</summary>
    internal static string? GetCrossProjectId(IMethodSymbol method)
    {
        return (method.ReducedFrom ?? method).OriginalDefinition.GetDocumentationCommentId();
    }

    /// <summary>The definition of the method at the root of an override chain.</summary>
    internal static IMethodSymbol GetCrossProjectRoot(IMethodSymbol method)
    {
        return GetVirtualRoot(method);
    }

    /// <summary>
    /// What a source method executes once per call, by the cache's matcher, through the helpers of this project.
    /// </summary>
    internal static string GetMethodExecution(
        IMethodSymbol method,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        method = method.OriginalDefinition;
        method = method.PartialImplementationPart ?? method;
        return GetHelperExecution(method, MaxHelperDepth, cache, cancellationToken);
    }

    /// <summary>
    /// The cross-project calls a source method makes once per call, directly or through helpers of this project
    /// that it calls statically, and whether the method consults a cache or remembered state before the first of
    /// them. These are the edges the scanner follows when the method itself runs no query.
    /// </summary>
    internal static bool CollectCrossProjectCalls(
        IMethodSymbol method,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken,
        List<CrossProjectCallee> calls)
    {
        method = method.OriginalDefinition;
        method = method.PartialImplementationPart ?? method;
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        return CollectCrossProjectCalls(method, MaxHelperDepth, cache, cancellationToken, calls, visited);
    }

    private static bool CollectCrossProjectCalls(
        IMethodSymbol method,
        int depth,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken,
        List<CrossProjectCallee> calls,
        HashSet<IMethodSymbol> visited)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!visited.Add(method) || !TryGetHelperBody(method, cache, cancellationToken, out var root))
            return false;

        var memoized = false;
        var firstCallStart = int.MaxValue;
        foreach (var operation in DescendantsInSameBody(root))
        {
            // A call in a loop of this method is a call site of its own.
            if (operation is not IInvocationOperation call || IsInsidePerIterationLoopOf(call, root))
                continue;

            if (TryGetCrossProjectCallee(call, cache, cancellationToken, out var callee))
            {
                AddCall(calls, callee);
                firstCallStart = Math.Min(firstCallStart, call.Syntax.SpanStart);
                continue;
            }

            if (depth <= 1)
                continue;

            var targets = GetHelperTargets(call, cache, cancellationToken);
            if (targets.Length != 1)
                continue;

            var before = calls.Count;
            memoized |= CollectCrossProjectCalls(targets[0], depth - 1, cache, cancellationToken, calls, visited);
            if (calls.Count > before)
                firstCallStart = Math.Min(firstCallStart, call.Syntax.SpanStart);
        }

        return memoized || (firstCallStart != int.MaxValue && IsMemoized(root, firstCallStart));
    }

    private static void AddCall(List<CrossProjectCallee> calls, CrossProjectCallee callee)
    {
        foreach (var existing in calls)
        {
            if (existing.Kind == callee.Kind && existing.Id == callee.Id && existing.RootId == callee.RootId)
                return;
        }

        calls.Add(callee);
    }

    /// <summary>
    /// The loop or per-element operator that would make LC007 report this call if its target ran a query: the same
    /// walk and the same exemptions as a call to a helper of this project.
    /// </summary>
    internal static bool TryGetPerIterationCallSite(
        IInvocationOperation invocation,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken,
        out string loopKind,
        out string? operatorName)
    {
        loopKind = string.Empty;
        operatorName = null;

        if (invocation.FindEnclosingLoop() != null &&
            FindPerIterationLoopForHelperCall(invocation, cache, cancellationToken) is { } loop)
        {
            loopKind = loop.GetLoopKind();
            return true;
        }

        if (FindPerElementOperator(invocation, out _) is not { } perElementOperator)
            return false;

        loopKind = NPlusOneLooperDiagnosticProperties.LambdaLoopKind;
        operatorName = perElementOperator.TargetMethod.Name;
        return true;
    }
}
