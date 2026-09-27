using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy;

/// <summary>
/// Decides whether a method in this compilation only ever runs under an execution strategy: every reference to it in
/// the compilation is a call inside a lambda passed to a strategy (or to a project wrapper around one), the method
/// passed directly as that delegate, or a call from a method that itself only runs under a strategy.
/// </summary>
/// <remarks>
/// This is the least fixed point over same-compilation callers: a caller still being evaluated (recursion) counts as
/// unprotected, so a cycle only proves protection through an entry point that runs under a strategy. Direct
/// self-recursion is ignored. Callers the compilation cannot see (other projects, interface or virtual dispatch) are not
/// considered.
/// </remarks>
internal sealed class StrategyCallers
{
    private readonly Compilation _compilation;
    private readonly ConcurrentDictionary<ISymbol, bool> _cache = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<SyntaxTree, string> _texts = new();

    public StrategyCallers(Compilation compilation)
    {
        _compilation = compilation;
    }

    public bool RunsOnlyUnderStrategy(IMethodSymbol method, CancellationToken cancellationToken)
    {
        return IsProtected(method.OriginalDefinition, new HashSet<ISymbol>(SymbolEqualityComparer.Default), cancellationToken);
    }

    private bool IsProtected(IMethodSymbol method, HashSet<ISymbol> visiting, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(method, out var cached))
            return cached;

        // Still being evaluated further up: not proven yet.
        if (!visiting.Add(method))
            return false;

        var result = AllReferencesProtected(method, visiting, cancellationToken);
        visiting.Remove(method);

        // An inner result may rest on a caller that was still being evaluated, so only settled results are cached.
        if (visiting.Count == 0)
            _cache[method] = result;

        return result;
    }

    private bool AllReferencesProtected(IMethodSymbol method, HashSet<ISymbol> visiting, CancellationToken cancellationToken)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) ||
            method.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        var name = method.Name;
        var protectedReferences = 0;

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

                switch (ClassifyReference(reference, method, semanticModel, visiting, cancellationToken))
                {
                    case ReferenceKind.Protected:
                        protectedReferences++;
                        break;
                    case ReferenceKind.SelfRecursion:
                        break;
                    default:
                        return false;
                }
            }
        }

        return protectedReferences > 0;
    }

    private enum ReferenceKind
    {
        Unprotected,
        Protected,
        SelfRecursion
    }

    private ReferenceKind ClassifyReference(
        SimpleNameSyntax reference,
        IMethodSymbol method,
        SemanticModel semanticModel,
        HashSet<ISymbol> visiting,
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
            return IsStrategyDelegateArgument(expression, semanticModel, cancellationToken)
                ? ReferenceKind.Protected
                : ReferenceKind.Unprotected;
        }

        foreach (var ancestor in invocation.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                    return IsStrategyDelegateArgument(lambda, semanticModel, cancellationToken)
                        ? ReferenceKind.Protected
                        : ReferenceKind.Unprotected;
                case LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax:
                {
                    if (semanticModel.GetDeclaredSymbol(ancestor, cancellationToken) is not IMethodSymbol caller)
                        return ReferenceKind.Unprotected;

                    caller = caller.OriginalDefinition;
                    if (SymbolEqualityComparer.Default.Equals(caller, method))
                        return ReferenceKind.SelfRecursion;

                    return IsProtected(caller, visiting, cancellationToken) ? ReferenceKind.Protected : ReferenceKind.Unprotected;
                }

                case MemberDeclarationSyntax and not GlobalStatementSyntax:
                    return ReferenceKind.Unprotected;
            }
        }

        return ReferenceKind.Unprotected;
    }

    /// <summary>True when <paramref name="expression"/> is itself an argument of a strategy's Execute* call or a project wrapper.</summary>
    private static bool IsStrategyDelegateArgument(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return expression.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } } &&
               semanticModel.GetSymbolInfo(call, cancellationToken).Symbol is IMethodSymbol target &&
               RetryingStrategyModel.RunsDelegateUnderStrategy(target, cancellationToken);
    }

    private static bool IsInsideNameof(SyntaxNode node)
    {
        return node.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
    }
}
