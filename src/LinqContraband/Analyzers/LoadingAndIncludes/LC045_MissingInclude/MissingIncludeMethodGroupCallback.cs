using System;
using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    /// <summary>
    /// The body a method group runs for each element: <c>orders.Select(ToDto)</c>.
    /// </summary>
    private readonly struct MethodGroupCallback
    {
        public MethodGroupCallback(IOperation root, SyntaxNode body, IParameterSymbol parameter)
        {
            Root = root;
            Body = body;
            Parameter = parameter;
        }

        /// <summary>The local function, or the body of the private method.</summary>
        public IOperation Root { get; }

        public SyntaxNode Body { get; }

        public IParameterSymbol Parameter { get; }
    }

    /// <summary>
    /// A per-element operator over the materialized collection whose callback is a method group:
    /// <c>orders.Select(ToDto)</c>, <c>orders.Where(IsVip)</c>, <c>orders.ForEach(Print)</c>. The
    /// operator calls the method once per element, exactly as it would call
    /// <c>o =&gt; ToDto(o)</c>, so the method's body is the callback's body.
    ///
    /// Only a body that is certain to be the one that runs qualifies: a local function declared
    /// in the same method, or a private method declared in the same file (see
    /// <see cref="FindSameFilePrivateMethodBody"/>).
    /// </summary>
    private static bool TryGetMethodGroupCallback(
        IInvocationOperation invocation,
        IInvocationOperation materializer,
        ILocalSymbol? resultLocal,
        IOperation executableRoot,
        INamedTypeSymbol entityType,
        MissingIncludeFlowCache flowCache,
        CancellationToken cancellationToken,
        out MethodGroupCallback callback
    )
    {
        callback = default;
        if (
            !TryGetSupportedCollectionCallbackValue(
                invocation,
                materializer,
                resultLocal,
                out var callbackValue
            )
        )
        {
            return false;
        }

        var value = callbackValue.UnwrapConversions();
        if (value is IDelegateCreationOperation delegateCreation)
            value = delegateCreation.Target.UnwrapConversions();

        // The element is the only argument. The indexed overloads are not supported in lambda
        // form either, and a generic method's body is written against a type parameter.
        if (
            value is not IMethodReferenceOperation { Method: var method }
            || method.IsGenericMethod
            || method.Parameters.Length != 1
            || method.Parameters[0].RefKind != RefKind.None
            || !IsEntityOrBaseType(method.Parameters[0].Type, entityType)
        )
        {
            return false;
        }

        if (method.MethodKind == MethodKind.LocalFunction)
        {
            foreach (var localFunction in flowCache.GetScope(executableRoot).LocalFunctions)
            {
                if (
                    SymbolEqualityComparer.Default.Equals(localFunction.Symbol, method)
                    && localFunction.Body is { } localBody
                )
                {
                    callback = new MethodGroupCallback(
                        localFunction,
                        localBody.Syntax,
                        method.Parameters[0]
                    );
                    return true;
                }
            }

            return false;
        }

        if (
            FindSameFilePrivateMethodBody(method, invocation, cancellationToken)
                is not IMethodBodyOperation { Parent: null } methodBody
            || (methodBody.BlockBody ?? methodBody.ExpressionBody) is not { } body
        )
        {
            return false;
        }

        callback = new MethodGroupCallback(methodBody, body.Syntax, method.Parameters[0]);
        return true;
    }

    /// <summary>
    /// The navigation reads a method-group callback performs on its element, proven on the
    /// method's own control flow the way a lambda's are on the lambda's.
    /// </summary>
    private static bool TryCollectOriginAwareNavigationAccesses(
        IOperation parentExecutableRoot,
        MethodGroupCallback callback,
        SemanticModel? semanticModel,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        MissingIncludeFlowCache flowCache,
        CancellationToken cancellationToken,
        out List<NavigationAccess> accesses
    )
    {
        accesses = null!;
        ControlFlowGraph graph;
        if (callback.Root is ILocalFunctionOperation localFunction)
        {
            if (
                !TryGetFlowGraph(
                    parentExecutableRoot,
                    semanticModel,
                    flowCache,
                    cancellationToken,
                    out var parentGraph
                )
            )
            {
                return false;
            }

            try
            {
                graph = parentGraph.GetLocalFunctionControlFlowGraph(
                    localFunction.Symbol,
                    cancellationToken
                );
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
        else if (
            !TryGetFlowGraph(
                callback.Root,
                semanticModel,
                flowCache,
                cancellationToken,
                out graph
            )
        )
        {
            return false;
        }

        return TryCollectCallbackNavigationAccesses(
            graph,
            callback.Root,
            callback.Body,
            callback.Parameter,
            entityType,
            entityTypes,
            flowCache,
            cancellationToken,
            out accesses
        );
    }

    /// <summary>
    /// The body of an ordinary private method declared in the same syntax tree as the call. A
    /// private method cannot be overridden, so the body found here is the body that runs, and the
    /// same tree keeps this free: the semantic model already covers it.
    /// </summary>
    private static IOperation? FindSameFilePrivateMethodBody(
        IMethodSymbol targetMethod,
        IOperation callSite,
        CancellationToken cancellationToken
    )
    {
        if (
            targetMethod.MethodKind != MethodKind.Ordinary
            || targetMethod.DeclaredAccessibility != Accessibility.Private
            || targetMethod.IsAbstract
            || targetMethod.PartialImplementationPart != null
            || callSite.SemanticModel is not { } semanticModel
        )
        {
            return null;
        }

        foreach (var reference in targetMethod.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree != callSite.Syntax.SyntaxTree)
                continue;

            var declaration = reference.GetSyntax(cancellationToken);
            if (semanticModel.GetOperation(declaration, cancellationToken) is { } body)
                return body;
        }

        return null;
    }

    private static bool IsEntityOrBaseType(ITypeSymbol type, INamedTypeSymbol entityType)
    {
        for (ITypeSymbol? current = entityType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, type))
                return true;
        }

        return false;
    }
}
