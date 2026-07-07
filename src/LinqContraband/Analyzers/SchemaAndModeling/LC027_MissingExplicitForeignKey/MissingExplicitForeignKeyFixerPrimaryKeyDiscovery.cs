using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyFixer
{
    private static IPropertySymbol? TryFindConventionPrimaryKey(INamedTypeSymbol entityType)
    {
        var pkName = entityType.TryFindPrimaryKey();
        return pkName == null
            ? null
            : entityType.GetMembers(pkName).OfType<IPropertySymbol>().FirstOrDefault();
    }

    private static IPropertySymbol? TryFindConfiguredPrimaryKey(
        INamedTypeSymbol entityType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var tree in semanticModel.Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = tree == semanticModel.SyntaxTree
                ? semanticModel
                : semanticModel.Compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);

            foreach (var invocationSyntax in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText != "HasKey" ||
                    model.GetOperation(invocationSyntax, cancellationToken) is not IInvocationOperation invocation ||
                    !TryGetEntityTypeBuilderEntity(invocation.GetInvocationReceiverType(), out var configuredEntity) ||
                    !SymbolEqualityComparer.Default.Equals(configuredEntity, entityType) ||
                    invocation.Arguments.FirstOrDefault()?.Value.UnwrapConversions() is not IAnonymousFunctionOperation lambda)
                {
                    continue;
                }

                var body = lambda.Body.Operations.FirstOrDefault();
                if (body is IReturnOperation returnOperation)
                    body = returnOperation.ReturnedValue;

                if (body?.UnwrapConversions() is IPropertyReferenceOperation propertyReference &&
                    propertyReference.Instance?.UnwrapConversions() is IParameterReferenceOperation parameterReference &&
                    SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, lambda.Symbol.Parameters.FirstOrDefault()))
                {
                    return propertyReference.Property;
                }
            }
        }

        return null;
    }

    private static bool TryGetEntityTypeBuilderEntity(ITypeSymbol? receiverType, out ITypeSymbol entityType)
    {
        entityType = null!;

        if (receiverType is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.TypeArguments.Length != 1 ||
            namedType.Name != "EntityTypeBuilder")
        {
            return false;
        }

        var namespaceName = namedType.ContainingNamespace?.ToString();
        if (namespaceName is not ("Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Metadata.Builders"))
            return false;

        entityType = namedType.TypeArguments[0];
        return true;
    }
}
