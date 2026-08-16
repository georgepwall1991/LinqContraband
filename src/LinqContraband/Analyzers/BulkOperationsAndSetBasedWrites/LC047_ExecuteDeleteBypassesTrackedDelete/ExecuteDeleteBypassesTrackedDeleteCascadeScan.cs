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
        foreach (var member in contextType.GetMembers("OnModelCreating"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method)
                continue;

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
                    continue;

                var model = compilation.GetSemanticModel(declaration.SyntaxTree);
                var operation = model.GetOperation(declaration, cancellationToken);
                if (operation == null)
                    continue;

                foreach (var child in EnumerateOperations(operation))
                {
                    if (child is not IInvocationOperation invocation ||
                        invocation.TargetMethod.Name != "OnDelete")
                    {
                        continue;
                    }

                    if (!IsClientCascadeBehavior(invocation))
                        continue;

                    if (!TryGetRelationshipPrincipal(invocation, operation, cancellationToken, out var principal))
                        continue;

                    clientCascadePrincipals.Add(new TypePair(contextType, principal));
                }
            }
        }
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
