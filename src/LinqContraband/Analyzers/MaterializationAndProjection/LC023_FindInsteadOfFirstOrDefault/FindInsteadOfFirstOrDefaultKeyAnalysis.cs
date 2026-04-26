using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static class FindInsteadOfFirstOrDefaultKeyAnalysis
{
    public static string? TryFindSafePrimaryKey(
        ITypeSymbol entityType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var configuredKey = TryFindConfiguredPrimaryKey(entityType, compilation, cancellationToken);
        if (configuredKey.IsConfigured)
            return configuredKey.PropertyName;

        return entityType.TryFindPrimaryKey();
    }

    private static ConfiguredPrimaryKey TryFindConfiguredPrimaryKey(
        ITypeSymbol entityType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(tree);

            foreach (var invocationSyntax in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText != "HasKey")
                {
                    continue;
                }

                if (semanticModel.GetOperation(invocationSyntax, cancellationToken) is not IInvocationOperation invocation ||
                    invocation.TargetMethod.Name != "HasKey" ||
                    !ReceiverTargetsEntity(invocation.GetInvocationReceiverType(), entityType))
                {
                    continue;
                }

                return AnalyzeKeyArgument(invocation.Arguments.FirstOrDefault()?.Value);
            }
        }

        return ConfiguredPrimaryKey.NotConfigured;
    }

    private static bool ReceiverTargetsEntity(ITypeSymbol? receiverType, ITypeSymbol entityType)
    {
        return receiverType is INamedTypeSymbol namedType &&
               namedType.IsGenericType &&
               namedType.TypeArguments.Length == 1 &&
               IsEntityTypeBuilder(namedType) &&
               SymbolEqualityComparer.Default.Equals(namedType.TypeArguments[0], entityType);
    }

    private static bool IsEntityTypeBuilder(INamedTypeSymbol namedType)
    {
        if (namedType.Name != "EntityTypeBuilder")
            return false;

        var namespaceName = namedType.ContainingNamespace.ToDisplayString();
        return namespaceName is "Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    private static ConfiguredPrimaryKey AnalyzeKeyArgument(IOperation? keyArgument)
    {
        if (keyArgument?.UnwrapConversions() is not IAnonymousFunctionOperation lambda)
            return ConfiguredPrimaryKey.Unsupported;

        var body = lambda.Body.Operations.FirstOrDefault();
        if (body is IReturnOperation returnOperation)
            body = returnOperation.ReturnedValue;

        if (body != null && TryGetLambdaPropertyName(body.UnwrapConversions(), lambda, out var propertyName))
            return ConfiguredPrimaryKey.Single(propertyName);

        return ConfiguredPrimaryKey.Unsupported;
    }

    private static bool TryGetLambdaPropertyName(
        IOperation operation,
        IAnonymousFunctionOperation lambda,
        out string propertyName)
    {
        propertyName = string.Empty;

        if (operation is not IPropertyReferenceOperation propertyReference)
            return false;

        if (propertyReference.Instance?.UnwrapConversions() is not IParameterReferenceOperation parameterReference ||
            !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, lambda.Symbol.Parameters.FirstOrDefault()))
        {
            return false;
        }

        propertyName = propertyReference.Property.Name;
        return true;
    }

    private readonly struct ConfiguredPrimaryKey
    {
        private ConfiguredPrimaryKey(bool isConfigured, string? propertyName)
        {
            IsConfigured = isConfigured;
            PropertyName = propertyName;
        }

        public bool IsConfigured { get; }

        public string? PropertyName { get; }

        public static ConfiguredPrimaryKey NotConfigured => new(false, null);

        public static ConfiguredPrimaryKey Unsupported => new(true, null);

        public static ConfiguredPrimaryKey Single(string propertyName) => new(true, propertyName);
    }
}
