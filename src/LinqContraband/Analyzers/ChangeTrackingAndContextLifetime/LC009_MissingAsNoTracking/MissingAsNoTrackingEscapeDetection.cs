using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
    /// <summary>
    /// Diagnostic property set when the materialized entities leave the method. The analyzer
    /// cannot see what the caller or callee does with them, and if that code changes an entity and
    /// saves, <c>AsNoTracking()</c> silently turns the save into a no-op. The rule still reports, but
    /// the fixer is withheld so a one-click or Fix All rewrite cannot introduce a lost update.
    /// </summary>
    internal const string EntitiesEscapeProperty = "EntitiesEscape";

    /// <summary>
    /// True when the materialized value, the result local, or a foreach variable over it is
    /// returned, yielded, passed as an argument, stored somewhere other than a fresh local, or
    /// captured in a collection, tuple or object initializer.
    /// </summary>
    private static bool MaterializedEntitiesEscape(
        IInvocationOperation materializer,
        IOperation root,
        HashSet<ILocalSymbol> entityLocals,
        CancellationToken cancellationToken)
    {
        // The generic materializers (ToList<TSource>, First<TSource>, ToDictionary<TSource, TKey>, ...)
        // name the entity type first.
        var entityType = materializer.TargetMethod.TypeArguments.Length > 0
            ? materializer.TargetMethod.TypeArguments[0]
            : null;

        if (IsEscapingUse(materializer, isMaterializer: true, entityType))
            return true;

        if (entityLocals.Count == 0)
            return false;

        // Loop variables over a navigation of an entity join the set as the walk reaches their loop.
        entityLocals = new HashSet<ILocalSymbol>(entityLocals, SymbolEqualityComparer.Default);

        foreach (var descendant in root.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (descendant is ILocalReferenceOperation localReference &&
                entityLocals.Contains(localReference.Local))
            {
                if (IsEscapingUse(localReference, isMaterializer: false, entityType))
                    return true;

                if (NavigationEscapes(localReference, entityLocals))
                    return true;
            }

            // users.ForEach(u => service.Save(u)): a delegate over the entity list can hand
            // each element to code this analysis does not follow.
            if (descendant is IInvocationOperation invocation &&
                invocation.Instance?.UnwrapConversions() is ILocalReferenceOperation receiver &&
                entityLocals.Contains(receiver.Local) &&
                HasDelegateArgument(invocation))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>order.Lines</c> or <c>tag.Items.Select(i => i.Series)</c> reaches entities loaded with the row. Stored in
    /// another object or handed to other code, they can join a tracked graph, where untracked duplicates of the
    /// same key fail to attach. Scalars read through a navigation (<c>order.Lines.Sum(...)</c>) stay reads.
    /// </summary>
    private static bool NavigationEscapes(ILocalReferenceOperation entity, HashSet<ILocalSymbol> entityLocals)
    {
        IOperation current = entity;
        var navigated = false;
        while (current.Parent is IPropertyReferenceOperation property &&
               property.Instance == current &&
               ReachesEntities(property.Type))
        {
            current = property;
            navigated = true;
        }

        if (!navigated)
            return false;

        var parent = current.Parent;
        while (parent is IConversionOperation or IParenthesizedOperation)
            parent = parent.Parent;

        if (parent is IForEachLoopOperation loop)
        {
            foreach (var local in loop.Locals)
                entityLocals.Add(local);

            return false;
        }

        // The navigation's element type is not the query's entity type, so any reference-typed result counts.
        return IsEscapingUse(current, isMaterializer: false, entityType: null);
    }

    // A class or interface outside System, or a collection of one: a navigation rather than a scalar.
    private static bool ReachesEntities(ITypeSymbol? type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return ReachesEntities(array.ElementType);
            case INamedTypeSymbol named:
                if (named.TypeKind is TypeKind.Class or TypeKind.Interface &&
                    named.SpecialType == SpecialType.None &&
                    !IsInSystemNamespace(named))
                    return true;

                foreach (var typeArgument in named.TypeArguments)
                {
                    if (ReachesEntities(typeArgument))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool IsInSystemNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        while (ns is { IsGlobalNamespace: false, ContainingNamespace.IsGlobalNamespace: false })
            ns = ns.ContainingNamespace;

        return ns is { IsGlobalNamespace: false, Name: "System" };
    }

    private static bool IsEscapingUse(IOperation value, bool isMaterializer, ITypeSymbol? entityType)
    {
        IOperation current = value;
        var parent = value.Parent;

        while (parent is IConversionOperation or IParenthesizedOperation or IAwaitOperation)
        {
            current = parent;
            parent = parent.Parent;
        }

        switch (parent)
        {
            case IArgumentOperation { Parent: IInvocationOperation linq } argument
                when IsLinqToObjectsSource(linq, argument):
                // users.Count(), users.Any(...), users.Select(u => u.Name): a LINQ-to-Objects
                // result that no longer carries the entity is a read. One that still carries
                // it (users.Where(...).ToList()) escapes wherever that result goes.
                return entityType == null
                    ? linq.Type is { IsValueType: false, SpecialType: not SpecialType.System_String }
                    : ContainsType(linq.Type, entityType) && IsEscapingUse(linq, isMaterializer: false, entityType);
            case IReturnOperation:
            case IArgumentOperation:
            case IArrayElementReferenceOperation:
            case ITupleOperation:
            case IArrayInitializerOperation:
            case IAnonymousObjectCreationOperation:
            case IDelegateCreationOperation:
            case IPropertyInitializerOperation:
            case IFieldInitializerOperation:
                return true;
            case ISimpleAssignmentOperation assignment when assignment.Value == current:
                // users = db.Users.ToList(); stores the materializer into its own result local.
                if (isMaterializer && assignment.Target is ILocalReferenceOperation)
                    return false;

                // A store into a field, property, element, or another local keeps the entity
                // reachable from code this analysis does not follow.
                return true;
            case IVariableInitializerOperation:
                // var alias = users; keeps the entities reachable through a local this analysis
                // does not follow. The materializer's own declarator is not an escape.
                return !isMaterializer;
            default:
                return false;
        }
    }

    private static bool IsLinqToObjectsSource(IInvocationOperation invocation, IArgumentOperation argument)
    {
        var method = invocation.TargetMethod;
        return method.IsExtensionMethod &&
               invocation.Arguments.Length > 0 &&
               invocation.Arguments[0] == argument &&
               method.ContainingType?.Name == "Enumerable" &&
               method.ContainingNamespace?.ToDisplayString() == "System.Linq";
    }

    private static bool ContainsType(ITypeSymbol? type, ITypeSymbol target)
    {
        switch (type)
        {
            case null:
                return false;
            case IArrayTypeSymbol array:
                return ContainsType(array.ElementType, target);
            case INamedTypeSymbol named:
                if (SymbolEqualityComparer.Default.Equals(named, target))
                    return true;
                foreach (var typeArgument in named.TypeArguments)
                {
                    if (ContainsType(typeArgument, target))
                        return true;
                }

                return false;
            default:
                // Type parameters and other shapes cannot be proven entity-free.
                return true;
        }
    }

    private static bool HasDelegateArgument(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Value.UnwrapConversions() is IDelegateCreationOperation or IAnonymousFunctionOperation ||
                argument.Value.Type?.TypeKind == TypeKind.Delegate)
                return true;
        }

        return false;
    }
}
