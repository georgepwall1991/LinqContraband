using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

internal static partial class UseFrozenSetForStaticMembershipCachesAnalysis
{
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
