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
    /// provably does. A registration or <c>DbContextOptionsBuilder&lt;T&gt;</c> chain configures only its exact type;
    /// an <c>OnConfiguring</c> override also runs for every derived context that does not replace it.
    /// </summary>
    public Location? FindConfiguration(ITypeSymbol contextType, Compilation compilation, CancellationToken cancellationToken)
    {
        if (contextType is INamedTypeSymbol named && _configuredContexts.TryGetValue(named, out var exact))
            return exact;

        for (var current = contextType as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (_configuredDefinitions.TryGetValue(current.OriginalDefinition, out var location))
                return location;

            // An override that never calls base.OnConfiguring replaces every inherited configuration.
            if (ReplacesInheritedOnConfiguring(current, compilation, cancellationToken))
                break;
        }

        if (_unattributedLocation != null &&
            _onlyContext != null &&
            SymbolEqualityComparer.Default.Equals(contextType.OriginalDefinition, _onlyContext))
        {
            return _unattributedLocation;
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="type"/> declares an <c>OnConfiguring</c> override in source whose body never calls
    /// <c>base.OnConfiguring(...)</c> on the method it overrides, so the configuration of its base types does not run
    /// for it. A <c>base.OnConfiguring</c> call that binds to another overload does not count.
    /// </summary>
    private static bool ReplacesInheritedOnConfiguring(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var member in type.GetMembers("OnConfiguring"))
        {
            if (member is not IMethodSymbol { IsOverride: true } method || method.DeclaringSyntaxReferences.Length == 0)
                continue;

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax(cancellationToken);
                compilation.TryGetOwnedSemanticModel(syntax.SyntaxTree, out var semanticModel);
                var callsBase = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(
                    invocation => invocation.Expression is MemberAccessExpressionSyntax
                    {
                        Expression: BaseExpressionSyntax,
                        Name.Identifier.ValueText: "OnConfiguring"
                    } &&
                    (semanticModel == null ||
                     method.OverriddenMethod == null ||
                     semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol called ||
                     SymbolEqualityComparer.Default.Equals(called.OriginalDefinition, method.OverriddenMethod.OriginalDefinition)));
                if (callsBase)
                    return false;
            }

            return true;
        }

        return false;
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

    /// <summary>
    /// True when a delegate bound to <paramref name="parameter"/> of <paramref name="method"/> runs under an execution
    /// strategy: <paramref name="parameter"/> is the <c>operation</c> or <c>verifySucceeded</c> delegate of a strategy's
    /// Execute* method, or a source method hands that parameter (or a lambda that invokes it) to one.
    /// </summary>
    public static bool RunsDelegateUnderStrategy(
        IMethodSymbol method,
        IParameterSymbol? parameter,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (parameter == null)
            return false;

        if (IsStrategyExecute(method))
            return IsStrategyDelegateParameter(method, parameter);

        return IsStrategyWrapperParameter(method, parameter, compilation, cancellationToken);
    }

    /// <summary>
    /// The strategy runs its <c>operation</c> and <c>verifySucceeded</c> delegates. <c>state</c> (and the
    /// <c>context</c> of <c>ExecuteInTransaction</c>) is only passed through, so a delegate given as state runs
    /// wherever the operation later invokes it.
    /// </summary>
    private static bool IsStrategyDelegateParameter(IMethodSymbol method, IParameterSymbol parameter)
    {
        if (parameter.Name is "operation" or "verifySucceeded")
            return true;

        if (parameter.Name is "state" or "context" ||
            parameter.Type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke } ||
            invoke.Parameters.Length == 0)
        {
            return false;
        }

        // Unknown parameter names: the strategy's own delegates take the DbContext or the state first.
        var first = invoke.Parameters[0].Type;
        if (first.IsDbContext())
            return true;

        foreach (var other in method.Parameters)
        {
            if (other.Name == "state" && SymbolEqualityComparer.Default.Equals(other.Type, first))
                return true;
        }

        return false;
    }

    private static bool IsStrategyWrapperParameter(
        IMethodSymbol method,
        IParameterSymbol parameter,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        // Map the parameter onto the unreduced, unconstructed declaration.
        var declaration = (method.ReducedFrom ?? method).OriginalDefinition;
        var ordinal = method.ReducedFrom != null ? parameter.Ordinal + 1 : parameter.Ordinal;
        if (ordinal >= declaration.Parameters.Length)
            return false;

        var target = declaration.Parameters[ordinal];
        if (target.Type.TypeKind != TypeKind.Delegate)
            return false;

        foreach (var reference in declaration.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(cancellationToken);
            if (!compilation.TryGetOwnedSemanticModel(syntax.SyntaxTree, out var semanticModel) ||
                semanticModel.GetOperation(syntax, cancellationToken) is not { } body)
            {
                continue;
            }

            if (EveryUseForwardsToStrategy(body, target))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the parameter is used at least once and every use hands it to a strategy's Execute* call: passed as
    /// the delegate argument, or invoked directly inside a lambda that is itself that delegate argument. Any other
    /// use (invoked outside the strategy, stored, returned, passed elsewhere, captured by some other lambda or local
    /// function) means the delegate can run outside the strategy.
    /// </summary>
    private static bool EveryUseForwardsToStrategy(IOperation body, IParameterSymbol parameter)
    {
        var forwarded = false;
        foreach (var operation in body.Descendants())
        {
            if (operation is not IParameterReferenceOperation reference ||
                !SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter))
            {
                continue;
            }

            if (IsStrategyDelegateArgument(reference))
            {
                forwarded = true;
                continue;
            }

            IOperation invoked = reference;
            while (invoked.Parent is IConversionOperation)
                invoked = invoked.Parent;

            if (invoked.Parent is IInvocationOperation { TargetMethod.MethodKind: MethodKind.DelegateInvoke } invocation &&
                ReferenceEquals(invocation.Instance, invoked) &&
                EnclosingFunction(invocation) is IAnonymousFunctionOperation lambda &&
                IsStrategyDelegateArgument(lambda))
            {
                forwarded = true;
                continue;
            }

            return false;
        }

        return forwarded;
    }

    private static bool IsStrategyDelegateArgument(IOperation value)
    {
        while (value.Parent is IDelegateCreationOperation or IConversionOperation)
            value = value.Parent;

        return value.Parent is IArgumentOperation { Parent: IInvocationOperation execute, Parameter: { } parameter } &&
               IsStrategyExecute(execute.TargetMethod) &&
               IsStrategyDelegateParameter(execute.TargetMethod, parameter);
    }

    private static IOperation? EnclosingFunction(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return current;
        }

        return null;
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
            return IsProviderOptionsBuilder(method.ContainingType, operation.Instance?.Type) &&
                   !HasZeroRetryCount(operation.Arguments);
        }

        if (method.Name != "ExecutionStrategy" || !IsProviderOptionsBuilder(method.ContainingType, operation.Instance?.Type))
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

    /// <summary>
    /// An EF Core provider options builder: the method's type or the receiver's type derives from
    /// <c>RelationalDbContextOptionsBuilder&lt;,&gt;</c> (SQL Server, Azure SQL, Npgsql, Pomelo, Oracle, SQLite), or is a
    /// <c>*DbContextOptionsBuilder</c> declared in a <c>Microsoft.EntityFrameworkCore</c> namespace.
    /// </summary>
    private static bool IsProviderOptionsBuilder(INamedTypeSymbol? containingType, ITypeSymbol? receiverType)
    {
        return IsRelationalOptionsBuilder(containingType) ||
               IsRelationalOptionsBuilder(receiverType as INamedTypeSymbol) ||
               IsEfCoreProviderBuilder(containingType) ||
               IsEfCoreProviderBuilder(receiverType as INamedTypeSymbol);
    }

    private static bool IsEfCoreProviderBuilder(INamedTypeSymbol? type)
    {
        // EF Core's own DbContextOptionsBuilder has no retry settings; provider builders add a prefix (SqlServer...).
        if (type == null ||
            type.Name == "DbContextOptionsBuilder" ||
            !type.Name.EndsWith("DbContextOptionsBuilder", StringComparison.Ordinal))
            return false;

        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns == EfNamespace || ns?.StartsWith(EfNamespace + ".", StringComparison.Ordinal) == true;
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
