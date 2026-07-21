using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed partial class ConcurrentDbContextOperationsAnalyzer
{
    private static readonly HashSet<string> QueryAsyncSinkNames = new(StringComparer.Ordinal)
    {
        "AllAsync",
        "AnyAsync",
        "AverageAsync",
        "CountAsync",
        "ExecuteDeleteAsync",
        "ExecuteUpdateAsync",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "ForEachAsync",
        "LastAsync",
        "LastOrDefaultAsync",
        "LoadAsync",
        "LongCountAsync",
        "MaxAsync",
        "MinAsync",
        "SingleAsync",
        "SingleOrDefaultAsync",
        "SumAsync",
        "ToArrayAsync",
        "ToDictionaryAsync",
        "ToHashSetAsync",
        "ToListAsync"
    };

    private static readonly HashSet<string> DatabaseFacadeAsyncSinkNames = new(StringComparer.Ordinal)
    {
        "ExecuteSqlAsync",
        "ExecuteSqlInterpolatedAsync",
        "ExecuteSqlRawAsync"
    };

    private static bool TryClassifyEfAsyncOperation(
        IInvocationOperation invocation,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out EfOperation operation)
    {
        operation = default;

        if (!ReturnsTaskLike(invocation.TargetMethod.ReturnType))
            return false;

        IOperation? source;
        if (IsDbContextAsyncSink(invocation))
        {
            source = invocation.Instance;
            if (source == null ||
                !TryResolveContextOrigin(source, executableRoot, invocation.Syntax.SpanStart, cancellationToken, out var contextOrigin))
            {
                return false;
            }

            operation = new EfOperation(invocation, contextOrigin);
            return true;
        }

        if (IsDbSetFindAsync(invocation))
        {
            source = invocation.Instance;
            if (source == null ||
                !TryResolveQueryContext(source, executableRoot, invocation.Syntax.SpanStart, cancellationToken, out var setOrigin))
            {
                return false;
            }

            operation = new EfOperation(invocation, setOrigin);
            return true;
        }

        if (IsDatabaseFacadeAsyncSink(invocation))
        {
            source = GetSemanticInvocationReceiver(invocation);
            if (source == null ||
                !TryResolveDatabaseFacadeContext(source, executableRoot, invocation.Syntax.SpanStart, cancellationToken, out var databaseOrigin))
            {
                return false;
            }

            operation = new EfOperation(invocation, databaseOrigin);
            return true;
        }

        if (!IsQueryableAsyncSink(invocation))
            return false;

        source = GetSemanticInvocationReceiver(invocation);
        if (source == null ||
            !TryResolveQueryContext(source, executableRoot, invocation.Syntax.SpanStart, cancellationToken, out var queryOrigin))
        {
            return false;
        }

        operation = new EfOperation(invocation, queryOrigin);
        return true;
    }

    private static bool IsDbContextAsyncSink(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return method.ContainingType.IsDbContext() &&
               method.Name is "SaveChangesAsync" or "FindAsync";
    }

    private static bool IsDbSetFindAsync(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return method.Name == "FindAsync" && method.ContainingType.IsDbSet();
    }

    private static bool IsQueryableAsyncSink(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!QueryAsyncSinkNames.Contains(method.Name))
            return false;

        var containingType = method.ContainingType;
        var containingNamespace = containingType.ContainingNamespace?.ToString();
        if (containingNamespace != "Microsoft.EntityFrameworkCore")
            return false;

        if (containingType.Name is not (
                "EntityFrameworkQueryableExtensions" or
                "RelationalQueryableExtensions"))
        {
            return false;
        }

        var receiverType = GetSemanticInvocationReceiver(invocation)?.Type;
        return receiverType.IsIQueryable() || receiverType.IsDbSet();
    }

    private static bool IsDatabaseFacadeAsyncSink(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return DatabaseFacadeAsyncSinkNames.Contains(method.Name) &&
               method.ContainingType.Name == "RelationalDatabaseFacadeExtensions" &&
               method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool ReturnsTaskLike(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return type.Name is "Task" or "ValueTask" &&
               type.ContainingNamespace?.ToString() == "System.Threading.Tasks";
    }

    private static bool TryResolveQueryContext(
        IOperation source,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        out ContextOrigin origin)
    {
        return TryResolveQueryContext(
            source,
            executableRoot,
            beforePosition,
            cancellationToken,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            out origin);
    }

    private static bool TryResolveQueryContext(
        IOperation source,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visitedLocals,
        out ContextOrigin origin)
    {
        source = source.UnwrapConversions();

        switch (source)
        {
            case ILocalReferenceOperation localReference:
                if (!visitedLocals.Add(localReference.Local) ||
                    !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition,
                        out var assignedValue,
                        cancellationToken))
                {
                    origin = default;
                    return false;
                }

                return TryResolveQueryContext(
                    assignedValue,
                    executableRoot,
                    localReference.Syntax.SpanStart,
                    cancellationToken,
                    visitedLocals,
                    out origin);

            case IPropertyReferenceOperation propertyReference
                when propertyReference.Property.Type.IsDbSet() &&
                     IsSourceVisibleAutoProperty(propertyReference.Property) &&
                     propertyReference.Instance != null:
                return TryResolveContextOrigin(
                    propertyReference.Instance,
                    executableRoot,
                    beforePosition,
                    cancellationToken,
                    out origin);

            case IFieldReferenceOperation fieldReference
                when fieldReference.Field.Type.IsDbSet() &&
                     fieldReference.Field.IsReadOnly &&
                     fieldReference.Instance != null:
                return TryResolveContextOrigin(
                    fieldReference.Instance,
                    executableRoot,
                    beforePosition,
                    cancellationToken,
                    out origin);

            case IInvocationOperation queryInvocation:
                if (IsDbContextSetInvocation(queryInvocation) &&
                    queryInvocation.Instance != null)
                {
                    return TryResolveContextOrigin(
                        queryInvocation.Instance,
                        executableRoot,
                        beforePosition,
                        cancellationToken,
                        out origin);
                }

                if (IsTransparentQueryInvocation(queryInvocation))
                {
                    var receiver = GetSemanticInvocationReceiver(queryInvocation);
                    if (receiver != null)
                    {
                        return TryResolveQueryContext(
                            receiver,
                            executableRoot,
                            queryInvocation.Syntax.SpanStart,
                            cancellationToken,
                            visitedLocals,
                            out origin);
                    }
                }

                break;
        }

        origin = default;
        return false;
    }

    private static bool TryResolveContextOrigin(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        out ContextOrigin origin)
    {
        return TryResolveContextOrigin(
            expression,
            executableRoot,
            beforePosition,
            cancellationToken,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            out origin);
    }

    private static bool TryResolveContextOrigin(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visitedLocals,
        out ContextOrigin origin)
    {
        expression = expression.UnwrapConversions();

        switch (expression)
        {
            case IParameterReferenceOperation parameterReference
                when parameterReference.Parameter.Type.IsDbContext():
                origin = new ContextOrigin(parameterReference.Parameter, parameterReference.Parameter.Name);
                return true;

            case ILocalReferenceOperation localReference
                when localReference.Local.Type.IsDbContext():
                if (!visitedLocals.Add(localReference.Local) ||
                    !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition,
                        out var assignedValue,
                        cancellationToken))
                {
                    origin = default;
                    return false;
                }

                if (assignedValue.UnwrapConversions() is IObjectCreationOperation creation &&
                    creation.Type.IsDbContext())
                {
                    origin = new ContextOrigin(localReference.Local, localReference.Local.Name);
                    return true;
                }

                return TryResolveContextOrigin(
                    assignedValue,
                    executableRoot,
                    localReference.Syntax.SpanStart,
                    cancellationToken,
                    visitedLocals,
                    out origin);

            case IFieldReferenceOperation fieldReference
                when fieldReference.Field.Type.IsDbContext() &&
                     fieldReference.Field.IsReadOnly:
                origin = new ContextOrigin(fieldReference.Field, fieldReference.Field.Name);
                return true;

            case IPropertyReferenceOperation propertyReference
                when propertyReference.Property.Type.IsDbContext() &&
                     IsStableAutoProperty(propertyReference.Property):
                origin = new ContextOrigin(propertyReference.Property, propertyReference.Property.Name);
                return true;

            case IInstanceReferenceOperation instanceReference
                when instanceReference.Type.IsDbContext():
                origin = new ContextOrigin(instanceReference.Type!, "this");
                return true;
        }

        origin = default;
        return false;
    }

    private static bool TryResolveDatabaseFacadeContext(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        out ContextOrigin origin)
    {
        return TryResolveDatabaseFacadeContext(
            expression,
            executableRoot,
            beforePosition,
            cancellationToken,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            out origin);
    }

    private static bool TryResolveDatabaseFacadeContext(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visitedLocals,
        out ContextOrigin origin)
    {
        expression = expression.UnwrapConversions();

        if (expression is ILocalReferenceOperation localReference &&
            visitedLocals.Add(localReference.Local) &&
            LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                localReference.Local,
                beforePosition,
                out var assignedValue,
                cancellationToken))
        {
            return TryResolveDatabaseFacadeContext(
                assignedValue,
                executableRoot,
                localReference.Syntax.SpanStart,
                cancellationToken,
                visitedLocals,
                out origin);
        }

        if (expression is IPropertyReferenceOperation propertyReference &&
            propertyReference.Property.Name == "Database" &&
            propertyReference.Property.Type.Name == "DatabaseFacade" &&
            propertyReference.Property.Type.ContainingNamespace?.ToString() ==
            "Microsoft.EntityFrameworkCore.Infrastructure" &&
            propertyReference.Instance != null)
        {
            return TryResolveContextOrigin(
                propertyReference.Instance,
                executableRoot,
                beforePosition,
                cancellationToken,
                out origin);
        }

        origin = default;
        return false;
    }

    private static bool IsDbContextSetInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Set" &&
               invocation.TargetMethod.ContainingType.IsDbContext() &&
               invocation.Type.IsDbSet();
    }

    private static IOperation? GetSemanticInvocationReceiver(IInvocationOperation invocation)
    {
        if (invocation.Instance != null)
            return invocation.Instance.UnwrapConversions();

        if (invocation.TargetMethod.IsExtensionMethod)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter?.Ordinal == 0)
                    return argument.Value.UnwrapConversions();
            }
        }

        return null;
    }

    private static bool IsTransparentQueryInvocation(IInvocationOperation invocation)
    {
        if (!(invocation.Type.IsIQueryable() || invocation.Type.IsDbSet()))
            return false;

        var containingType = invocation.TargetMethod.ContainingType;
        var containingNamespace = containingType.ContainingNamespace?.ToString();
        return (containingType.Name == "Queryable" && containingNamespace == "System.Linq") ||
               (containingNamespace == "Microsoft.EntityFrameworkCore" &&
                containingType.Name is (
                    "EntityFrameworkQueryableExtensions" or
                    "RelationalQueryableExtensions"));
    }

    private static bool IsStableAutoProperty(IPropertySymbol property)
    {
        foreach (var syntaxReference in property.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax declaration ||
                declaration.ExpressionBody != null ||
                declaration.AccessorList == null)
            {
                continue;
            }

            var hasGetter = false;
            var hasMutableSetter = false;
            foreach (var accessor in declaration.AccessorList.Accessors)
            {
                if (accessor.Body != null || accessor.ExpressionBody != null)
                    return false;

                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                    hasGetter = true;
                else if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
                    hasMutableSetter = true;
            }

            if (hasGetter && !hasMutableSetter)
                return true;
        }

        return false;
    }

    private static bool IsSourceVisibleAutoProperty(IPropertySymbol property)
    {
        foreach (var syntaxReference in property.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax declaration ||
                declaration.ExpressionBody != null ||
                declaration.AccessorList == null)
            {
                continue;
            }

            var hasGetter = false;
            var isAutoProperty = true;
            foreach (var accessor in declaration.AccessorList.Accessors)
            {
                if (accessor.Body != null || accessor.ExpressionBody != null)
                {
                    isAutoProperty = false;
                    break;
                }

                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                    hasGetter = true;
            }

            if (hasGetter && isAutoProperty)
                return true;
        }

        return false;
    }

    private readonly struct ContextOrigin
    {
        public ContextOrigin(ISymbol symbol, string displayName)
        {
            Symbol = symbol;
            DisplayName = displayName;
        }

        public ISymbol Symbol { get; }

        public string DisplayName { get; }
    }

    private readonly struct EfOperation
    {
        public EfOperation(IInvocationOperation invocation, ContextOrigin origin)
        {
            Invocation = invocation;
            Origin = origin;
        }

        public IInvocationOperation Invocation { get; }

        public ContextOrigin Origin { get; }
    }
}
