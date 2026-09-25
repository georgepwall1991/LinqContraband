using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
    private static ChainAnalysis AnalyzeQueryChain(IInvocationOperation invocation)
    {
        var result = new ChainAnalysis();
        var current = invocation.GetInvocationReceiver();

        while (current != null)
        {
            current = current.UnwrapConversions();

            switch (current)
            {
                case IInvocationOperation prevInvocation:
                    var method = prevInvocation.TargetMethod;

                    // "db.Orders.ToList().Where(...).ToList()": the inner ToList() runs the EF query and
                    // is reported itself. Everything after it works on a loaded list, not on EF.
                    if (prevInvocation.IsQueryExecutingMaterializer())
                        return result;

                    if (method.Name == "AsNoTracking" || method.Name == "AsNoTrackingWithIdentityResolution")
                        result.HasAsNoTracking = true;
                    if (method.Name == "AsTracking")
                        result.HasAsTracking = true;
                    if (method.Name == "Select")
                        result.HasSelect = true;

                    // An invocation whose return type is a DbSet (e.g. DbContext.Set<T>(), the
                    // generic-repository read path) is itself the EF source. Without this the
                    // walker steps past it to the DbContext receiver and misses the query.
                    if (prevInvocation.Type.IsDbSet())
                    {
                        result.IsEfQuery = true;
                        result.ContextIsLocal = IsLocalContext(prevInvocation.Instance);
                        result.MaterializesNonEntity = MaterializesNonEntity(invocation, prevInvocation.Type, prevInvocation.Instance?.Type);
                        return result;
                    }

                    current = prevInvocation.Instance ??
                              (prevInvocation.Arguments.Length > 0 ? prevInvocation.Arguments[0].Value : null);
                    continue;

                case IPropertyReferenceOperation propRef:
                    if (propRef.Type.IsDbSet())
                    {
                        result.IsEfQuery = true;
                        result.ContextIsLocal = IsLocalContext(propRef.Instance);
                        result.MaterializesNonEntity = MaterializesNonEntity(invocation, propRef.Type, propRef.Instance?.Type);
                    }
                    return result;

                case IFieldReferenceOperation fieldRef:
                    if (fieldRef.Type.IsDbSet())
                    {
                        result.IsEfQuery = true;
                        result.ContextIsLocal = IsLocalContext(fieldRef.Instance);
                        result.MaterializesNonEntity = MaterializesNonEntity(invocation, fieldRef.Type, fieldRef.Instance?.Type);
                    }
                    return result;

                case IParameterReferenceOperation paramRef:
                    if (paramRef.Type.IsDbSet() || paramRef.Type.IsIQueryable())
                        result.IsAmbiguousSource = true;
                    return result;

                case ILocalReferenceOperation localRef:
                    if (localRef.Type.IsDbSet() || localRef.Type.IsIQueryable())
                        result.IsAmbiguousSource = true;
                    return result;

                default:
                    if (current.Type.IsDbSet())
                    {
                        result.IsEfQuery = true;
                        result.MaterializesNonEntity = MaterializesNonEntity(invocation, current.Type, null);
                    }
                    else if (current.Type.IsIQueryable())
                        result.IsAmbiguousSource = true;
                    return result;
            }
        }

        return result;
    }

    // using var db = new AppDbContext(); the context lives and dies in this method, so no caller can save
    // the entities it tracks.
    private static bool IsLocalContext(IOperation? instance) =>
        instance?.UnwrapConversions() is ILocalReferenceOperation;

    // The materializer's element type is an entity when it is the root DbSet's entity, a type
    // derived from it (OfType<Derived>()), a navigation of the root entity or another entity the
    // context exposes as a DbSet (SelectMany(o => o.Lines)). Groupings are judged by their
    // elements, and anonymous types (Join result selectors) keep reporting when a member may hold an
    // entity. Anything else, a DTO or a scalar, is not tracked.
    private static bool MaterializesNonEntity(IInvocationOperation materializer, ITypeSymbol? dbSetType, ITypeSymbol? contextType)
    {
        if (dbSetType is not INamedTypeSymbol { TypeArguments.Length: 1 } dbSet)
            return false;

        var source = materializer.GetInvocationReceiver()?.UnwrapConversions();
        if (source?.Type == null || !IncludePathParser.TryGetCollectionElementType(source.Type, out var elementType))
            return false;

        if (elementType is INamedTypeSymbol { Name: "IGrouping", TypeArguments.Length: 2 } grouping)
            elementType = grouping.TypeArguments[1];

        if (elementType.TypeKind is TypeKind.TypeParameter or TypeKind.Error)
            return false;

        // A Join or GroupJoin result selector such as new { c.Id, PagesRead = ... } that holds only
        // scalars loads no entity, so nothing is tracked.
        if (elementType.IsAnonymousType)
            return !AnonymousTypeCarriesEntities((INamedTypeSymbol)elementType);

        var rootEntity = dbSet.TypeArguments[0];
        for (var type = elementType; type != null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, rootEntity))
                return false;
        }

        // Cast<IAuditable>() or Cast<EntityBase>() still returns the tracked entities.
        for (var type = rootEntity.BaseType; type != null; type = type.BaseType)
        {
            if (type.SpecialType != SpecialType.System_Object && SymbolEqualityComparer.Default.Equals(type, elementType))
                return false;
        }

        if (rootEntity.AllInterfaces.Contains(elementType, SymbolEqualityComparer.Default))
            return false;

        if (IsNavigationOf(rootEntity, elementType))
            return false;

        if (contextType != null)
        {
            for (var type = contextType; type != null; type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    var memberType = member switch
                    {
                        IPropertySymbol property => property.Type,
                        IFieldSymbol field => field.Type,
                        _ => null
                    };

                    if (memberType is INamedTypeSymbol { TypeArguments.Length: 1 } memberSet &&
                        memberSet.IsDbSet() &&
                        SymbolEqualityComparer.Default.Equals(memberSet.TypeArguments[0], elementType))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    // Any member that is a class outside System (or a collection of one) may be an entity, so only
    // scalar, string and System-typed members make an anonymous type entity-free.
    private static bool AnonymousTypeCarriesEntities(INamedTypeSymbol anonymousType)
    {
        foreach (var property in anonymousType.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.Type is INamedTypeSymbol { IsAnonymousType: true } nested
                    ? AnonymousTypeCarriesEntities(nested)
                    : ReachesEntities(property.Type) || property.Type.TypeKind is TypeKind.TypeParameter or TypeKind.Error)
                return true;
        }

        return false;
    }

    private static bool IsNavigationOf(ITypeSymbol entity, ITypeSymbol elementType)
    {
        // Scalars such as Order.Id are properties too, but only classes can be navigations.
        if (elementType.TypeKind != TypeKind.Class || elementType.SpecialType == SpecialType.System_String)
            return false;

        for (var type = entity; type != null; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(property.Type, elementType))
                    return true;

                if (IncludePathParser.TryGetCollectionElementType(property.Type, out var navigationElement) &&
                    SymbolEqualityComparer.Default.Equals(navigationElement, elementType))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
