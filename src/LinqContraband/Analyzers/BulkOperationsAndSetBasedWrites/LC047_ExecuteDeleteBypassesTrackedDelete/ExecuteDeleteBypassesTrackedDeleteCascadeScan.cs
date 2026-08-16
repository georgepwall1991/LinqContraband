using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

internal sealed partial class TrackedDeletePipelineEvidence
{
    private void ScanOnModelCreatingCascades(INamedTypeSymbol contextType, CancellationToken cancellationToken)
    {
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var member in contextType.GetMembers("OnModelCreating"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method)
                continue;

            ScanCascadeMethodTree(method, contextType, contextType, visited, 0, cancellationToken);
        }
    }

    private void ScanCascadeMethodTree(
        IMethodSymbol method,
        INamedTypeSymbol helperOwner,
        INamedTypeSymbol evidenceContext,
        HashSet<IMethodSymbol> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 4 || !visited.Add(method))
            return;

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
                continue;

            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            var operation = model.GetOperation(declaration, cancellationToken);
            if (operation == null)
                continue;

            foreach (var child in EnumerateOperations(operation))
            {
                if (child is not IInvocationOperation invocation)
                    continue;

                if (invocation.TargetMethod.Name == "OnDelete" &&
                    IsClientCascadeBehavior(invocation) &&
                    TryGetRelationshipPrincipal(invocation, operation, cancellationToken, out var principal))
                {
                    clientCascadePrincipals.Add(new TypePair(CanonicalContext(evidenceContext), principal));
                }

                ScanAppliedConfigurationInvocation(
                    invocation,
                    operation,
                    evidenceContext,
                    visited,
                    depth,
                    cancellationToken);
                ScanAppliedConfigurationsFromAssembly(
                    invocation,
                    operation,
                    evidenceContext,
                    visited,
                    depth,
                    cancellationToken);

                var target = invocation.TargetMethod.OriginalDefinition;
                if (!IsSameTypeOrBaseHelper(target.ContainingType, helperOwner))
                    continue;

                ScanCascadeMethodTree(target, helperOwner, evidenceContext, visited, depth + 1, cancellationToken);
            }
        }
    }

    private static bool IsSameTypeOrBaseHelper(INamedTypeSymbol? targetType, INamedTypeSymbol helperOwner)
    {
        if (targetType == null)
            return false;

        var targetDefinition = targetType.OriginalDefinition;
        for (var current = helperOwner; current != null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object ||
                (current.Name == "DbContext" &&
                 current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore"))
            {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(targetDefinition, current.OriginalDefinition))
                return true;
        }

        return false;
    }

    private static bool IsClientCascadeBehavior(IInvocationOperation invocation)
    {
        if (invocation.Arguments.Length == 0)
            return false;

        var argument = invocation.Arguments[0].Value.UnwrapConversions();
        ISymbol? symbol = argument switch
        {
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation property => property.Property,
            _ => null
        };

        if (symbol == null)
            return false;

        if (symbol.ContainingType?.Name != "DeleteBehavior" ||
            symbol.ContainingType.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
        {
            return false;
        }

        return symbol.Name is "ClientCascade" or "ClientSetNull";
    }

    private static bool TryGetRelationshipPrincipal(
        IInvocationOperation onDelete,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out INamedTypeSymbol principal)
    {
        principal = null!;
        if (onDelete.GetInvocationReceiverType() is INamedTypeSymbol receiver)
        {
            if (receiver.Name == "ReferenceCollectionBuilder" &&
                receiver.TypeArguments.Length == 2 &&
                receiver.TypeArguments[0] is INamedTypeSymbol collectionPrincipal)
            {
                principal = collectionPrincipal;
                return true;
            }

            if (receiver.Name == "ReferenceReferenceBuilder" && receiver.TypeArguments.Length == 2)
                return TryGetOneToOnePrincipal(onDelete, receiver, out principal);
        }

        var current = onDelete.GetInvocationReceiver();
        IInvocationOperation? hasMany = null;
        IInvocationOperation? hasOne = null;
        IInvocationOperation? entityBuilder = null;

        for (var depth = 0; depth < 12 && current != null; depth++)
        {
            current = current.UnwrapConversions();
            switch (current)
            {
                case IInvocationOperation invocation:
                    switch (invocation.TargetMethod.Name)
                    {
                        case "HasMany":
                            hasMany = invocation;
                            break;
                        case "HasOne":
                            hasOne = invocation;
                            break;
                        case "Entity":
                            entityBuilder = invocation;
                            break;
                    }

                    current = invocation.GetInvocationReceiver();
                    continue;
                case ILocalReferenceOperation local:
                    if (!LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                            executableRoot,
                            local.Local,
                            local.Syntax.SpanStart,
                            out var assigned,
                            cancellationToken))
                    {
                        break;
                    }

                    current = assigned;
                    continue;
                default:
                    break;
            }

            break;
        }

        if (hasMany != null)
            return TryGetEntityBuilderType(hasMany, out principal) || TryGetEntityBuilderType(entityBuilder, out principal);

        if (hasOne != null)
            return TryGetRelatedType(hasOne, out principal);

        return TryGetEntityBuilderType(entityBuilder, out principal);
    }

    private static bool TryGetOneToOnePrincipal(
        IInvocationOperation onDelete,
        INamedTypeSymbol builderType,
        out INamedTypeSymbol principal)
    {
        principal = null!;
        if (builderType.TypeArguments.Length != 2 ||
            builderType.TypeArguments[0] is not INamedTypeSymbol entity ||
            builderType.TypeArguments[1] is not INamedTypeSymbol related)
        {
            return false;
        }

        if (!TryGetForeignKeyDependent(onDelete, out var dependent))
            return false;

        if (SymbolEqualityComparer.Default.Equals(dependent, entity))
        {
            principal = related;
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(dependent, related))
        {
            principal = entity;
            return true;
        }

        return false;
    }

    private static bool TryGetForeignKeyDependent(IInvocationOperation onDelete, out INamedTypeSymbol dependent)
    {
        dependent = null!;
        if (TryGetHasForeignKeyDependent(onDelete, out dependent))
            return true;

        var current = onDelete.GetInvocationReceiver();
        for (var depth = 0; depth < 12 && current != null; depth++)
        {
            current = current.UnwrapConversions();
            if (current is IInvocationOperation invocation)
            {
                if (TryGetHasForeignKeyDependent(invocation, out dependent))
                    return true;

                current = invocation.GetInvocationReceiver();
                continue;
            }

            break;
        }

        for (var parent = onDelete.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is IInvocationOperation invocation &&
                TryGetHasForeignKeyDependent(invocation, out dependent))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetHasForeignKeyDependent(IInvocationOperation invocation, out INamedTypeSymbol dependent)
    {
        dependent = null!;
        if (invocation.TargetMethod.Name != "HasForeignKey")
            return false;

        if (invocation.TargetMethod.TypeArguments.Length == 1 &&
            invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol typeArgument)
        {
            dependent = typeArgument;
            return true;
        }

        return false;
    }

    private static bool TryGetEntityBuilderType(IInvocationOperation? invocation, out INamedTypeSymbol entityType)
    {
        entityType = null!;
        if (invocation == null)
            return false;

        if (invocation.TargetMethod.Name == "Entity" &&
            invocation.TargetMethod.TypeArguments.Length == 1 &&
            invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol named)
        {
            entityType = named;
            return true;
        }

        if (invocation.GetInvocationReceiverType() is INamedTypeSymbol receiver &&
            receiver.Name is "EntityTypeBuilder" &&
            receiver.TypeArguments.Length == 1 &&
            receiver.TypeArguments[0] is INamedTypeSymbol builderEntity)
        {
            entityType = builderEntity;
            return true;
        }

        return false;
    }

    private static bool TryGetRelatedType(IInvocationOperation hasOne, out INamedTypeSymbol related)
    {
        related = null!;
        if (hasOne.TargetMethod.TypeArguments.Length == 1 &&
            hasOne.TargetMethod.TypeArguments[0] is INamedTypeSymbol typeArgument)
        {
            related = typeArgument;
            return true;
        }

        foreach (var argument in hasOne.Arguments)
        {
            var value = argument.Value.UnwrapConversions();
            if (value is not IDelegateCreationOperation creation)
                continue;

            if (creation.Target is not IAnonymousFunctionOperation lambda)
                continue;

            foreach (var operation in EnumerateOperations(lambda))
            {
                if (operation is IPropertyReferenceOperation property &&
                    property.Property.Type is INamedTypeSymbol propertyType &&
                    !propertyType.IsDbContext())
                {
                    related = propertyType;
                    return true;
                }
            }
        }

        return false;
    }
}
