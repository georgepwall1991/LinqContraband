using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

internal static class UseFrozenSetForStaticMembershipCachesDiagnosticProperties
{
    public const string FixerEligible = "LC033.FixerEligible";
}

internal enum FrozenSetInitializerKind
{
    CollectionInitializer,
    SourceConstructor,
    ToHashSetInvocation
}

internal readonly struct FrozenSetSupport
{
    public FrozenSetSupport(
        INamedTypeSymbol hashSetType,
        INamedTypeSymbol frozenSetType,
        INamedTypeSymbol expressionType,
        ImmutableArray<IMethodSymbol> toFrozenSetMethods)
    {
        HashSetType = hashSetType;
        FrozenSetType = frozenSetType;
        ExpressionType = expressionType;
        ToFrozenSetMethods = toFrozenSetMethods;
    }

    public INamedTypeSymbol HashSetType { get; }
    public INamedTypeSymbol FrozenSetType { get; }
    public INamedTypeSymbol ExpressionType { get; }
    public ImmutableArray<IMethodSymbol> ToFrozenSetMethods { get; }
}

internal static class UseFrozenSetForStaticMembershipCachesAnalysis
{
    public static bool TryGetFrozenSetSupport(Compilation compilation, out FrozenSetSupport support)
    {
        support = default;

        var hashSetType = compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1");
        var frozenSetType = compilation.GetTypeByMetadataName("System.Collections.Frozen.FrozenSet`1");
        var expressionType = compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");

        if (hashSetType is null || frozenSetType is null || expressionType is null)
            return false;

        var toFrozenSetMethods = compilation
            .GetSymbolsWithName(static name => name == "ToFrozenSet", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Where(static method => method.IsExtensionMethod)
            .Where(method => method.ContainingNamespace?.ToDisplayString() == "System.Collections.Frozen")
            .Where(method => method.ReturnType is INamedTypeSymbol returnType &&
                             SymbolEqualityComparer.Default.Equals(returnType.OriginalDefinition, frozenSetType))
            .ToImmutableArray();

        if (toFrozenSetMethods.IsDefaultOrEmpty)
            return false;

        support = new FrozenSetSupport(hashSetType, frozenSetType, expressionType, toFrozenSetMethods);
        return true;
    }

    public static bool IsHashSetType(ITypeSymbol? type, INamedTypeSymbol hashSetType)
    {
        return type is INamedTypeSymbol namedType &&
               SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, hashSetType);
    }

    public static bool IsExpressionType(ITypeSymbol? type, INamedTypeSymbol expressionType)
    {
        return type is INamedTypeSymbol namedType &&
               SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, expressionType);
    }

    public static bool TryClassifyInitializer(
        ExpressionSyntax initializerSyntax,
        SemanticModel semanticModel,
        FrozenSetSupport support,
        CancellationToken cancellationToken,
        out FrozenSetInitializerKind kind)
    {
        kind = default;

        if (semanticModel.GetOperation(initializerSyntax, cancellationToken)?.UnwrapConversions() is not IOperation operation)
            return false;

        switch (operation)
        {
            case IObjectCreationOperation creation when IsHashSetType(creation.Type, support.HashSetType):
                if (IsSupportedCollectionInitializer(creation, support))
                {
                    kind = FrozenSetInitializerKind.CollectionInitializer;
                    return true;
                }

                if (IsSupportedSourceConstructor(creation, support))
                {
                    kind = FrozenSetInitializerKind.SourceConstructor;
                    return true;
                }

                return false;

            case IInvocationOperation invocation when IsSupportedToHashSetInvocation(invocation, semanticModel, support, cancellationToken):
                kind = FrozenSetInitializerKind.ToHashSetInvocation;
                return true;

            default:
                return false;
        }
    }

    private static bool IsSupportedCollectionInitializer(IObjectCreationOperation creation, FrozenSetSupport support)
    {
        if (creation.Initializer is null)
            return false;

        if (creation.Arguments.Length == 0)
            return AllInitializersAreAddCalls(creation.Initializer);

        if (creation.Arguments.Length == 1 &&
            creation.Type is INamedTypeSymbol hashSetType &&
            IsEqualityComparerType(creation.Constructor?.Parameters[0].Type, hashSetType.TypeArguments[0]))
        {
            return AllInitializersAreAddCalls(creation.Initializer);
        }

        return false;
    }

    private static bool IsSupportedSourceConstructor(IObjectCreationOperation creation, FrozenSetSupport support)
    {
        if (creation.Initializer is not null || creation.Constructor is null)
            return false;

        var parameters = creation.Constructor.Parameters;
        if (creation.Type is not INamedTypeSymbol hashSetType)
            return false;

        if (parameters.Length == 1)
            return IsEnumerableType(parameters[0].Type, hashSetType.TypeArguments[0]);

        return parameters.Length == 2 &&
               IsEnumerableType(parameters[0].Type, hashSetType.TypeArguments[0]) &&
               IsEqualityComparerType(parameters[1].Type, hashSetType.TypeArguments[0]);
    }

    private static bool IsSupportedToHashSetInvocation(
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        FrozenSetSupport support,
        CancellationToken cancellationToken)
    {
        if (invocation.TargetMethod.Name != "ToHashSet" ||
            !invocation.TargetMethod.IsExtensionMethod ||
            !invocation.TargetMethod.IsFrameworkMethod() ||
            !IsHashSetType(invocation.Type, support.HashSetType))
        {
            return false;
        }

        if (invocation.Syntax is not InvocationExpressionSyntax invocationSyntax ||
            invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (invocation.Arguments.Length is not (1 or 2))
            return false;

        if (IsStaticTypeOrNamespaceAccess(memberAccess.Expression, semanticModel, cancellationToken))
            return false;

        var parameters = invocation.TargetMethod.Parameters;
        if (parameters.Length == 1)
            return IsEnumerableType(parameters[0].Type, invocation.Type is INamedTypeSymbol named ? named.TypeArguments[0] : null);

        return parameters.Length == 2 &&
               IsEnumerableType(parameters[0].Type, invocation.Type is INamedTypeSymbol type ? type.TypeArguments[0] : null) &&
               IsEqualityComparerType(parameters[1].Type, invocation.Type is INamedTypeSymbol comparerType ? comparerType.TypeArguments[0] : null);
    }

    private static bool IsStaticTypeOrNamespaceAccess(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(receiverExpression, cancellationToken);
        if (IsTypeOrNamespaceSymbol(symbolInfo.Symbol))
            return true;

        return symbolInfo.CandidateSymbols.Any(IsTypeOrNamespaceSymbol);
    }

    private static bool IsTypeOrNamespaceSymbol(ISymbol? symbol)
    {
        return symbol switch
        {
            ITypeSymbol => true,
            INamespaceSymbol => true,
            IAliasSymbol { Target: ITypeSymbol or INamespaceSymbol } => true,
            _ => false
        };
    }

    private static bool AllInitializersAreAddCalls(IObjectOrCollectionInitializerOperation initializer)
    {
        return initializer.Initializers.All(operation =>
            operation is IInvocationOperation invocation &&
            invocation.TargetMethod.Name == "Add" &&
            invocation.Arguments.Length == 1);
    }

    private static bool IsEnumerableType(ITypeSymbol? type, ITypeSymbol? elementType)
    {
        if (type is not INamedTypeSymbol namedType || elementType is null)
            return false;

        if (IsEnumerableInterface(namedType, elementType))
            return true;

        return namedType.AllInterfaces.OfType<INamedTypeSymbol>().Any(interfaceType => IsEnumerableInterface(interfaceType, elementType));
    }

    private static bool IsEnumerableInterface(INamedTypeSymbol type, ITypeSymbol elementType)
    {
        return type.Name == "IEnumerable" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic" &&
               type.TypeArguments.Length == 1 &&
               SymbolEqualityComparer.Default.Equals(type.TypeArguments[0], elementType);
    }

    private static bool IsEqualityComparerType(ITypeSymbol? type, ITypeSymbol? elementType)
    {
        return type is INamedTypeSymbol namedType &&
               elementType is not null &&
               namedType.Name == "IEqualityComparer" &&
               namedType.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic" &&
               namedType.TypeArguments.Length == 1 &&
               SymbolEqualityComparer.Default.Equals(namedType.TypeArguments[0], elementType);
    }
}
