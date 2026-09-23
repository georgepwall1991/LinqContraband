using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis
{
    private static bool TryGetEntityTypeBuilderEntity(ITypeSymbol? receiverType, out ITypeSymbol entityType)
    {
        entityType = null!;

        if (receiverType is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.TypeArguments.Length != 1 ||
            !IsEntityTypeBuilder(namedType))
        {
            return false;
        }

        entityType = namedType.TypeArguments[0];
        return true;
    }

    private static bool IsEntityTypeBuilder(INamedTypeSymbol namedType)
    {
        if (namedType.Name != "EntityTypeBuilder")
            return false;

        var namespaceName = namedType.ContainingNamespace.ToDisplayString();
        return namespaceName is "Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    /// <summary>
    /// EF Core's class-level key attributes, which take precedence over [Key] and the
    /// convention: [Keyless] has no key for Find, [PrimaryKey(a)] names the key, and a
    /// composite [PrimaryKey(a, b)] needs every key value. EF applies them from the hierarchy
    /// root, so base types count too.
    /// </summary>
    private static ConfiguredPrimaryKey AnalyzeKeyAttributes(ITypeSymbol entityType)
    {
        for (var current = entityType; current != null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null ||
                    attributeClass.ContainingNamespace?.ToDisplayString() != "Microsoft.EntityFrameworkCore")
                {
                    continue;
                }

                if (attributeClass.Name == "KeylessAttribute")
                    return ConfiguredPrimaryKey.Unsupported;

                if (attributeClass.Name != "PrimaryKeyAttribute")
                    continue;

                if (attribute.ConstructorArguments.Length == 2 &&
                    attribute.ConstructorArguments[0].Value is string propertyName &&
                    attribute.ConstructorArguments[1].Kind == TypedConstantKind.Array &&
                    attribute.ConstructorArguments[1].Values.IsEmpty)
                {
                    return ConfiguredPrimaryKey.Single(propertyName);
                }

                return ConfiguredPrimaryKey.Unsupported;
            }
        }

        return ConfiguredPrimaryKey.NotConfigured;
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
