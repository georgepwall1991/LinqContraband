using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy;

/// <summary>
/// Decides whether a method in this compilation only ever runs under an execution strategy: every reference to it in
/// the compilation is a call inside a lambda passed to a strategy (or to a project wrapper around one), the method
/// passed directly as that delegate, or a call from a method that itself only runs under a strategy.
/// </summary>
/// <remarks>
/// Callers are grouped into strongly connected components, so mutual recursion is judged as a unit: a component is
/// protected when it has at least one protected entry and every reference into it from outside the component is
/// protected. Calls inside the component do not count either way. Callers the compilation cannot see (other projects,
/// interface or virtual dispatch) are not considered.
/// </remarks>
internal sealed class StrategyCallers
{
    // Beyond this many caller methods the answer is "not proven", and the rule reports.
    private const int MaxCallerGraphSize = 256;

    private readonly Compilation _compilation;
    private readonly ConcurrentDictionary<ISymbol, bool> _results = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<ISymbol, ImmutableArray<Reference>> _references = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<SyntaxTree, string> _texts = new();

    public StrategyCallers(Compilation compilation)
    {
        _compilation = compilation;
    }

    private enum ReferenceKind
    {
        Unprotected,
        Protected,
        Caller
    }

    private readonly struct Reference
    {
        public Reference(ReferenceKind kind, IMethodSymbol? caller = null)
        {
            Kind = kind;
            Caller = caller;
        }

        public ReferenceKind Kind { get; }
        public IMethodSymbol? Caller { get; }
    }

    public bool RunsOnlyUnderStrategy(IMethodSymbol method, CancellationToken cancellationToken)
    {
        var target = method.OriginalDefinition;
        if (_results.TryGetValue(target, out var cached))
            return cached;

        // Collect the caller graph above the method.
        var nodes = new List<IMethodSymbol>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<IMethodSymbol>();
        seen.Add(target);
        pending.Enqueue(target);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            nodes.Add(node);
            if (nodes.Count > MaxCallerGraphSize)
                return false;

            foreach (var reference in GetReferences(node, cancellationToken))
            {
                if (reference.Kind == ReferenceKind.Caller && seen.Add(reference.Caller!))
                    pending.Enqueue(reference.Caller!);
            }
        }

        // Tarjan emits a component only after every component reachable from it along caller edges, so callers are
        // settled before the methods they call.
        var components = FindComponents(nodes, cancellationToken);
        var componentOf = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
        for (var i = 0; i < components.Count; i++)
        {
            foreach (var member in components[i])
                componentOf[member] = i;
        }

        var protectedComponents = new bool[components.Count];
        for (var i = 0; i < components.Count; i++)
        {
            var hasProtectedEntry = false;
            var allEntriesProtected = true;

            foreach (var member in components[i])
            {
                foreach (var reference in GetReferences(member, cancellationToken))
                {
                    bool isProtected;
                    switch (reference.Kind)
                    {
                        case ReferenceKind.Protected:
                            isProtected = true;
                            break;
                        case ReferenceKind.Caller:
                            var callerComponent = componentOf[reference.Caller!];
                            if (callerComponent == i)
                                continue;
                            isProtected = protectedComponents[callerComponent];
                            break;
                        default:
                            isProtected = false;
                            break;
                    }

                    if (isProtected)
                        hasProtectedEntry = true;
                    else
                        allEntriesProtected = false;
                }
            }

            protectedComponents[i] = hasProtectedEntry && allEntriesProtected;
        }

        foreach (var node in nodes)
            _results.TryAdd(node, protectedComponents[componentOf[node]]);

        return protectedComponents[componentOf[target]];
    }

    private List<List<IMethodSymbol>> FindComponents(List<IMethodSymbol> nodes, CancellationToken cancellationToken)
    {
        var index = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
        var lowLink = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
        var onStack = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var stack = new Stack<IMethodSymbol>();
        var components = new List<List<IMethodSymbol>>();
        var counter = 0;

        foreach (var root in nodes)
        {
            if (index.ContainsKey(root))
                continue;

            // Iterative Tarjan: each frame is a node and the callers still to visit.
            var frames = new Stack<(IMethodSymbol Node, IEnumerator<IMethodSymbol> Callers)>();
            Open(root);

            while (frames.Count > 0)
            {
                var (node, callers) = frames.Peek();
                if (callers.MoveNext())
                {
                    var caller = callers.Current;
                    if (!index.ContainsKey(caller))
                    {
                        Open(caller);
                    }
                    else if (onStack.Contains(caller))
                    {
                        lowLink[node] = Math.Min(lowLink[node], index[caller]);
                    }

                    continue;
                }

                frames.Pop();
                if (frames.Count > 0)
                {
                    var parent = frames.Peek().Node;
                    lowLink[parent] = Math.Min(lowLink[parent], lowLink[node]);
                }

                if (lowLink[node] != index[node])
                    continue;

                var component = new List<IMethodSymbol>();
                IMethodSymbol member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (!SymbolEqualityComparer.Default.Equals(member, node));

                components.Add(component);
            }

            void Open(IMethodSymbol node)
            {
                index[node] = counter;
                lowLink[node] = counter;
                counter++;
                stack.Push(node);
                onStack.Add(node);
                var callers = GetReferences(node, cancellationToken)
                    .Where(reference => reference.Kind == ReferenceKind.Caller)
                    .Select(reference => reference.Caller!)
                    .ToList();
                frames.Push((node, callers.GetEnumerator()));
            }
        }

        return components;
    }

    private ImmutableArray<Reference> GetReferences(IMethodSymbol method, CancellationToken cancellationToken)
    {
        return _references.GetOrAdd(method, m => CollectReferences((IMethodSymbol)m, cancellationToken));
    }

    private ImmutableArray<Reference> CollectReferences(IMethodSymbol method, CancellationToken cancellationToken)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) ||
            method.DeclaringSyntaxReferences.IsEmpty)
        {
            return ImmutableArray.Create(new Reference(ReferenceKind.Unprotected));
        }

        var name = method.Name;
        var references = ImmutableArray.CreateBuilder<Reference>();

        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = _texts.GetOrAdd(tree, t => t.GetText(cancellationToken).ToString());
            if (text.IndexOf(name, StringComparison.Ordinal) < 0 ||
                !_compilation.TryGetOwnedSemanticModel(tree, out var semanticModel))
            {
                continue;
            }

            foreach (var reference in tree.GetRoot(cancellationToken).DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (reference.Identifier.ValueText != name ||
                    IsInsideNameof(reference) ||
                    semanticModel.GetSymbolInfo(reference, cancellationToken).Symbol is not IMethodSymbol referenced ||
                    !SymbolEqualityComparer.Default.Equals((referenced.ReducedFrom ?? referenced).OriginalDefinition, method))
                {
                    continue;
                }

                references.Add(ClassifyReference(reference, semanticModel, cancellationToken));
            }
        }

        return references.ToImmutable();
    }

    private static Reference ClassifyReference(
        SimpleNameSyntax reference,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax expression = reference;
        if (reference.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == reference)
            expression = memberAccess;
        else if (reference.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == reference)
            expression = memberBinding;

        if (expression.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != expression)
        {
            // A method group runs under the strategy only when it is the delegate the strategy is handed. Captured
            // anywhere else (stored, passed on), nothing proves when or where it runs.
            return new Reference(IsStrategyDelegateArgument(expression, semanticModel, cancellationToken)
                ? ReferenceKind.Protected
                : ReferenceKind.Unprotected);
        }

        foreach (var ancestor in invocation.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                    // ((Action)(() => Save()))() runs right here, like inline code: keep walking out.
                    if (IsInvokedInPlace(lambda))
                        continue;

                    return new Reference(IsStrategyDelegateArgument(lambda, semanticModel, cancellationToken) ||
                                         IsLocalOnlyHandedToStrategy(lambda, semanticModel, cancellationToken)
                        ? ReferenceKind.Protected
                        : ReferenceKind.Unprotected);
                case LocalFunctionStatementSyntax or MethodDeclarationSyntax:
                    return semanticModel.GetDeclaredSymbol(ancestor, cancellationToken) is IMethodSymbol caller
                        ? new Reference(ReferenceKind.Caller, caller.OriginalDefinition)
                        : new Reference(ReferenceKind.Unprotected);
                case MemberDeclarationSyntax and not GlobalStatementSyntax:
                    return new Reference(ReferenceKind.Unprotected);
            }
        }

        return new Reference(ReferenceKind.Unprotected);
    }

    /// <summary>True when <paramref name="expression"/> is itself an argument of a strategy's Execute* call or a project wrapper.</summary>
    private static bool IsStrategyDelegateArgument(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return expression.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax } } argument &&
               semanticModel.GetOperation(argument, cancellationToken) is IArgumentOperation
               {
                   Parent: IInvocationOperation call
               } argumentOperation &&
               RetryingStrategyModel.RunsDelegateUnderStrategy(
                   call.TargetMethod,
                   argumentOperation.Parameter,
                   semanticModel.Compilation,
                   cancellationToken);
    }

    /// <summary>True when <paramref name="lambda"/>, through parentheses and casts, is the expression an invocation calls.</summary>
    private static bool IsInvokedInPlace(AnonymousFunctionExpressionSyntax lambda)
    {
        ExpressionSyntax current = lambda;
        while (current.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            current = (ExpressionSyntax)current.Parent;

        return current != lambda &&
               current.Parent is InvocationExpressionSyntax invocation &&
               invocation.Expression == current;
    }

    /// <summary>
    /// True when <paramref name="lambda"/> initializes a local (<c>Action work = () => Save();</c>) that is never
    /// written again and whose every use is the delegate argument of a strategy's Execute* call or a project wrapper,
    /// so the lambda only runs under the strategy.
    /// </summary>
    private static bool IsLocalOnlyHandedToStrategy(
        AnonymousFunctionExpressionSyntax lambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax value = lambda;
        while (value.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            value = (ExpressionSyntax)value.Parent;

        if (value.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } ||
            semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol { RefKind: RefKind.None } local)
        {
            return false;
        }

        var scope = declarator.FirstAncestorOrSelf<BlockSyntax>() as SyntaxNode ??
                    declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (scope == null)
            return false;

        var handedToStrategy = false;
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != local.Name ||
                !SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
            {
                continue;
            }

            if (!IsStrategyDelegateArgument(identifier, semanticModel, cancellationToken))
                return false;

            handedToStrategy = true;
        }

        return handedToStrategy;
    }

    private static bool IsInsideNameof(SyntaxNode node)
    {
        return node.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
    }
}
