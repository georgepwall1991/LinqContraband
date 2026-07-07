using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static string? ExtractEntityTypeNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is GenericNameSyntax genericName &&
                genericName.Identifier.Text == "Entity")
            {
                var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (typeArg != null)
                    return typeArg.ToString();
            }

            current = current switch
            {
                InvocationExpressionSyntax inv => inv.Expression,
                MemberAccessExpressionSyntax ma => ma.Expression,
                _ => null
            };
        }

        return null;
    }

    private static void AddNamespaceTypes(
        INamespaceSymbol ns,
        List<INamedTypeSymbol> result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var type in ns.GetTypeMembers())
        {
            AddTypeAndNestedTypes(type, result, cancellationToken);
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            AddNamespaceTypes(childNs, result, cancellationToken);
        }
    }

    private static void AddTypeAndNestedTypes(
        INamedTypeSymbol type,
        List<INamedTypeSymbol> result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        result.Add(type);

        foreach (var nested in type.GetTypeMembers())
        {
            AddTypeAndNestedTypes(nested, result, cancellationToken);
        }
    }

    private static void AddLookupName(
        Dictionary<string, INamedTypeSymbol> lookup,
        string name,
        INamedTypeSymbol type)
    {
        if (name.Length > 0 && !lookup.ContainsKey(name))
            lookup.Add(name, type);
    }
}
