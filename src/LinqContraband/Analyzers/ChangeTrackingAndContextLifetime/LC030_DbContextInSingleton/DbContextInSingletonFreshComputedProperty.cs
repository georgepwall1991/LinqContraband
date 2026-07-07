using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static bool IsFreshComputedProperty(
        IPropertySymbol property,
        PropertyDeclarationSyntax propertyDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (property.SetMethod != null)
        {
            return false;
        }

        if (propertyDeclaration.Initializer != null)
        {
            return false;
        }

        if (propertyDeclaration.ExpressionBody != null)
        {
            return IsFreshContextExpression(propertyDeclaration.ExpressionBody.Expression, semanticModel, cancellationToken);
        }

        var getter = propertyDeclaration.AccessorList?.Accessors
            .FirstOrDefault(accessor => accessor.Kind() == SyntaxKind.GetAccessorDeclaration);
        if (getter?.ExpressionBody != null)
        {
            return IsFreshContextExpression(getter.ExpressionBody.Expression, semanticModel, cancellationToken);
        }

        if (getter?.Body != null)
        {
            var returnStatements = getter.Body.Statements.OfType<ReturnStatementSyntax>().ToArray();
            if (returnStatements.Length == 1 &&
                getter.Body.Statements.Count == 1 &&
                returnStatements[0].Expression is { } returnExpression)
            {
                return IsFreshContextExpression(returnExpression, semanticModel, cancellationToken);
            }
        }

        return false;
    }

    private static bool IsFreshContextExpression(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var operation = semanticModel.GetOperation(expression, cancellationToken);
        return operation switch
        {
            IObjectCreationOperation creation when creation.Type?.IsDbContext() == true => true,
            IInvocationOperation invocation when IsDbContextFactoryCreate(invocation) => true,
            _ => false
        };
    }

    private static bool IsDbContextFactoryCreate(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "CreateDbContext" &&
               invocation.TargetMethod.ReturnType.IsDbContext() &&
               IsDbContextFactory(invocation.TargetMethod.ContainingType);
    }

    private static bool IsDbContextFactory(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return IsDbContextFactoryDefinition(namedType) ||
               namedType.AllInterfaces.Any(IsDbContextFactoryDefinition);
    }

    private static bool IsDbContextFactoryDefinition(INamedTypeSymbol type)
    {
        return type.Name == "IDbContextFactory" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }
}
