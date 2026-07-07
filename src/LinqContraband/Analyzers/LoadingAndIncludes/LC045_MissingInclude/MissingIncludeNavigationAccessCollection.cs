using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private readonly struct NavigationAccess
    {
        public NavigationAccess(string path, SyntaxNode syntax)
        {
            Path = path;
            Syntax = syntax;
        }

        public string Path { get; }
        public SyntaxNode Syntax { get; }
    }

    private static bool IsSatisfied(NavigationAccess access, Dictionary<string, int> satisfiedPaths)
    {
        foreach (var pair in satisfiedPaths)
        {
            // Lexical ordering is the v1 heuristic: only reads after the assignment count
            // as backed (a conditional assignment before a read is conservatively quiet).
            if (access.Syntax.SpanStart < pair.Value)
                continue;

            var satisfied = pair.Key;
            if (access.Path.Length == satisfied.Length && access.Path == satisfied)
                return true;

            if (access.Path.Length > satisfied.Length &&
                access.Path[satisfied.Length] == '.' &&
                access.Path.StartsWith(satisfied, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<NavigationAccess> CollectInlineAccesses(
        IOperation? start,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes)
    {
        var accesses = new List<NavigationAccess>();
        var current = start;
        var currentEntity = entityType;
        string? path = null;

        while (current is IPropertyReferenceOperation propertyReference &&
               IsPropertyOfEntity(propertyReference.Property, currentEntity) &&
               TryGetNavigationTarget(propertyReference.Property, entityTypes, out var target, out var isCollection))
        {
            if (IsWriteTarget(propertyReference) ||
                (isCollection && IsCollectionMutatorReceiver(propertyReference)))
            {
                break;
            }

            path = path == null ? propertyReference.Property.Name : path + "." + propertyReference.Property.Name;
            accesses.Add(new NavigationAccess(path, propertyReference.Syntax));

            if (isCollection)
                break;

            currentEntity = target;
            current = WalkUpThroughWrappers(propertyReference.Parent);

            // ...?.Address continues the chain on the conditional access's WhenNotNull side —
            // but only when this property is the access's guarded receiver (Operation side).
            // Following a WhenNotNull-side parent would revisit this same property forever.
            if (current is IConditionalAccessOperation chainedAccess &&
                chainedAccess.Operation.UnwrapConversions() == propertyReference)
            {
                current = FindConditionalAccessEntryProperty(chainedAccess);
                continue;
            }

            if (current is IConditionalAccessOperation completedConditionalAccess &&
                TryFindRegroupedConditionalContinuation(completedConditionalAccess, out var continuation))
            {
                current = continuation;
            }
        }

        return accesses;
    }
}
