using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy;

/// <summary>
/// What one compilation says about retrying execution strategies: which context types are configured with one, whether a
/// configuration exists that cannot be tied to a context type.
/// </summary>
internal sealed class RetryingStrategyModel
{
    public const string EfNamespace = "Microsoft.EntityFrameworkCore";
    public const string StorageNamespace = "Microsoft.EntityFrameworkCore.Storage";

    private static readonly RetryingStrategyModel Empty = new(
        new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default),
        new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default),
        null,
        null);

    // Registrations and DbContextOptionsBuilder<T> chains name one exact (possibly constructed generic) context type.
    private readonly Dictionary<INamedTypeSymbol, Location> _configuredContexts;

    // OnConfiguring overrides belong to the declared type, so they apply to every construction of a generic context.
    private readonly Dictionary<INamedTypeSymbol, Location> _configuredDefinitions;
    private readonly INamedTypeSymbol? _onlyContext;
    private readonly Location? _unattributedLocation;

    private RetryingStrategyModel(
        Dictionary<INamedTypeSymbol, Location> configuredContexts,
        Dictionary<INamedTypeSymbol, Location> configuredDefinitions,
        INamedTypeSymbol? onlyContext,
        Location? unattributedLocation)
    {
        _configuredContexts = configuredContexts;
        _configuredDefinitions = configuredDefinitions;
        _onlyContext = onlyContext;
        _unattributedLocation = unattributedLocation;
    }

    /// <summary>
    /// The location of the retrying configuration that applies to <paramref name="contextType"/>, or null when none
    /// provably does.
    /// </summary>
    public Location? FindConfiguration(ITypeSymbol contextType)
    {
        for (var current = contextType as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (_configuredContexts.TryGetValue(current, out var location) ||
                _configuredDefinitions.TryGetValue(current.OriginalDefinition, out location))
            {
                return location;
            }
        }

        if (_unattributedLocation != null &&
            _onlyContext != null &&
            SymbolEqualityComparer.Default.Equals(contextType.OriginalDefinition, _onlyContext))
        {
            return _unattributedLocation;
        }

        return null;
    }

    public static RetryingStrategyModel Build(Compilation compilation, CancellationToken cancellationToken)
    {
        var configured = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
        var configuredDefinitions = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
        Location? unattributed = null;

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = tree.GetText(cancellationToken).ToString();
            var mentionsRetry = text.IndexOf("EnableRetryOnFailure", StringComparison.Ordinal) >= 0;
            var mentionsStrategy = text.IndexOf("ExecutionStrategy", StringComparison.Ordinal) >= 0;
            if ((!mentionsRetry && !mentionsStrategy) || !compilation.TryGetOwnedSemanticModel(tree, out var semanticModel))
                continue;

            foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = GetInvokedName(invocation);
                if (name is not ("EnableRetryOnFailure" or "ExecutionStrategy") ||
                    semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
                    !IsRetryingConfiguration(operation))
                    continue;

                var location = invocation.GetLocation();
                var contextType = FindConfiguredContext(invocation, semanticModel, cancellationToken, out var isDeclaration);
                if (contextType == null)
                {
                    unattributed ??= location;
                }
                else
                {
                    var target = isDeclaration ? configuredDefinitions : configured;
                    if (!target.ContainsKey(contextType))
                        target.Add(contextType, location);
                }
            }
        }

        if (configured.Count == 0 && configuredDefinitions.Count == 0 && unattributed == null)
            return Empty;

        var onlyContext = unattributed != null ? FindOnlySourceContext(compilation, cancellationToken) : null;
        return new RetryingStrategyModel(configured, configuredDefinitions, onlyContext, unattributed);
    }

    /// <summary>
    /// <c>Execute</c>, <c>ExecuteAsync</c>, <c>ExecuteInTransaction</c> or <c>ExecuteInTransactionAsync</c> on an
    /// <c>IExecutionStrategy</c>, as an interface or class method or as an extension method.
    /// </summary>
    public static bool IsStrategyExecute(IMethodSymbol method)
    {
        if (method.Name is not ("Execute" or "ExecuteAsync" or "ExecuteInTransaction" or "ExecuteInTransactionAsync"))
            return false;

        var original = method.ReducedFrom ?? method;
        if (original.IsExtensionMethod && original.Parameters.Length > 0)
            return IsExecutionStrategyType(original.Parameters[0].Type);

        return IsExecutionStrategyType(method.ContainingType);
    }

    public static bool IsExecutionStrategyType(ITypeSymbol? type)
    {
        if (type == null) return false;
        if (IsExecutionStrategyInterface(type)) return true;

        foreach (var implemented in type.AllInterfaces)
        {
            if (IsExecutionStrategyInterface(implemented))
                return true;
        }

        return false;
    }

    /// <summary>A method declared in source whose declaration mentions an execution strategy, such as a resilient-transaction helper.</summary>
    public static bool IsProjectStrategyWrapper(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in (method.ReducedFrom ?? method).OriginalDefinition.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken).ToString().IndexOf("ExecutionStrategy", StringComparison.Ordinal) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>A strategy's Execute* method, or a project method that wraps one: a delegate passed to it runs under the strategy.</summary>
    public static bool RunsDelegateUnderStrategy(IMethodSymbol method, CancellationToken cancellationToken)
    {
        return IsStrategyExecute(method) || IsProjectStrategyWrapper(method, cancellationToken);
    }

    private static bool IsExecutionStrategyInterface(ITypeSymbol type)
    {
        return type.Name == "IExecutionStrategy" && type.ContainingNamespace?.ToDisplayString() == StorageNamespace;
    }

    private static string? GetInvokedName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
            _ => null
        };
    }

    /// <summary>
    /// <c>EnableRetryOnFailure(...)</c> on a provider options builder, or <c>ExecutionStrategy(...)</c> on a relational
    /// options builder whose factory creates a type derived from EF Core's retrying <c>ExecutionStrategy</c> base class.
    /// A retry count of the constant 0 turns retries off, so that configuration does not count.
    /// </summary>
    private static bool IsRetryingConfiguration(IInvocationOperation operation)
    {
        var method = operation.TargetMethod;
        if (method.Name == "EnableRetryOnFailure")
        {
            return method.ContainingType?.Name.EndsWith("OptionsBuilder", StringComparison.Ordinal) == true &&
                   !HasZeroRetryCount(operation.Arguments);
        }

        if (method.Name != "ExecutionStrategy" || !IsRelationalOptionsBuilder(method.ContainingType))
            return false;

        foreach (var argument in operation.Arguments)
        {
            foreach (var descendant in argument.Value.DescendantsAndSelf())
            {
                if (descendant is IObjectCreationOperation creation &&
                    DerivesFromRetryingStrategy(creation.Type) &&
                    !HasZeroRetryCount(creation.Arguments))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasZeroRetryCount(IEnumerable<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.Name == "maxRetryCount" &&
                argument.Value.ConstantValue is { HasValue: true, Value: 0 })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRelationalOptionsBuilder(INamedTypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.Name == "RelationalDbContextOptionsBuilder" &&
                current.ContainingNamespace?.ToDisplayString() == EfNamespace + ".Infrastructure")
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFromRetryingStrategy(ITypeSymbol? type)
    {
        for (var current = type?.BaseType; current != null; current = current.BaseType)
        {
            if (current.Name == "ExecutionStrategy" && current.ContainingNamespace?.ToDisplayString() == StorageNamespace)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The context type a configuration belongs to: the context of an enclosing <c>AddDbContext&lt;T&gt;</c>-style
    /// registration or a <c>DbContextOptionsBuilder&lt;T&gt;</c> chain (the exact, possibly constructed, type), or the
    /// declaring context of an <c>OnConfiguring</c> override (<paramref name="isDeclaration"/>: every construction).
    /// </summary>
    private static INamedTypeSymbol? FindConfiguredContext(
        InvocationExpressionSyntax configuration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool isDeclaration)
    {
        isDeclaration = false;
        for (var node = configuration.Parent; node != null; node = node.Parent)
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation
                    when semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method:
                {
                    if (method.Name is "AddDbContext" or "AddDbContextPool" or "AddDbContextFactory" or "AddPooledDbContextFactory")
                    {
                        for (var i = method.TypeArguments.Length - 1; i >= 0; i--)
                        {
                            if (method.TypeArguments[i] is INamedTypeSymbol typeArgument && typeArgument.IsDbContext())
                                return typeArgument;
                        }

                        return null;
                    }

                    if (method.ReturnType is INamedTypeSymbol
                        {
                            Name: "DbContextOptionsBuilder", TypeArguments.Length: 1
                        } returnType &&
                        returnType.ContainingNamespace?.ToDisplayString() == EfNamespace &&
                        returnType.TypeArguments[0] is INamedTypeSymbol builderContext &&
                        builderContext.IsDbContext())
                    {
                        return builderContext;
                    }

                    break;
                }

                case MethodDeclarationSyntax methodDeclaration:
                {
                    if (methodDeclaration.Identifier.ValueText == "OnConfiguring" &&
                        methodDeclaration.Modifiers.Any(SyntaxKind.OverrideKeyword) &&
                        semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken)?.ContainingType is { } owner &&
                        owner.IsDbContext())
                    {
                        isDeclaration = true;
                        return owner.OriginalDefinition;
                    }

                    return null;
                }

                case MemberDeclarationSyntax and not GlobalStatementSyntax:
                    return null;
            }
        }

        return null;
    }

    /// <summary>The only non-abstract context type declared in this compilation, or null when there are none or several.</summary>
    private static INamedTypeSymbol? FindOnlySourceContext(Compilation compilation, CancellationToken cancellationToken)
    {
        INamedTypeSymbol? found = null;
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in pending.Pop().GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    pending.Push(ns);
                    continue;
                }

                if (member is not INamedTypeSymbol type)
                    continue;

                pending.Push(type);
                if (type.TypeKind != TypeKind.Class ||
                    type.IsAbstract ||
                    !type.IsDbContext() ||
                    (type.Name == "DbContext" && type.ContainingNamespace?.ToDisplayString() == EfNamespace))
                {
                    continue;
                }

                if (found != null)
                    return null;

                found = type;
            }
        }

        return found;
    }
}
