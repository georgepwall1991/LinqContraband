using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool ShouldScanCurrentAssemblyConfigurations(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var assemblyExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return assemblyExpression != null &&
               IsCurrentAssemblyExpression(
                   assemblyExpression,
                   dbContextType,
                   compilationModel,
                   cancellationToken,
                   new HashSet<ExpressionSyntax>());
    }

    private static bool IsCurrentAssemblyExpression(
        ExpressionSyntax expression,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!visitedExpressions.Add(expression))
            return false;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return IsCurrentAssemblyExpression(
                parenthesized.Expression,
                dbContextType,
                compilationModel,
                cancellationToken,
                visitedExpressions);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "Assembly" &&
            memberAccess.Expression is TypeOfExpressionSyntax typeOfExpression)
        {
            return IsCurrentAssemblyMarker(typeOfExpression, compilationModel, cancellationToken);
        }

        if (IsGetExecutingAssemblyCall(expression, dbContextType, compilationModel, cancellationToken))
            return true;

        if (expression is IdentifierNameSyntax identifier)
        {
            if (TryResolveLocalCurrentAssembly(identifier, dbContextType, compilationModel, cancellationToken, visitedExpressions, out var localIsCurrentAssembly))
                return localIsCurrentAssembly;

            return TryResolveMemberCurrentAssembly(dbContextType, identifier.Identifier.ValueText, compilationModel, cancellationToken, visitedExpressions);
        }

        if (expression is MemberAccessExpressionSyntax thisMemberAccess &&
            thisMemberAccess.Expression is ThisExpressionSyntax)
        {
            return TryResolveMemberCurrentAssembly(
                dbContextType,
                thisMemberAccess.Name.Identifier.ValueText,
                compilationModel,
                cancellationToken,
                visitedExpressions);
        }

        return false;
    }

    private static bool IsCurrentAssemblyMarker(
        TypeOfExpressionSyntax typeOfExpression,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var assemblyMarkerType = compilationModel.FindTypeByName(typeOfExpression.Type.ToString(), cancellationToken);
        return assemblyMarkerType != null &&
               SymbolEqualityComparer.Default.Equals(assemblyMarkerType.ContainingAssembly, compilationModel.Compilation.Assembly);
    }

    private static bool IsGetExecutingAssemblyCall(
        ExpressionSyntax expression,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.ArgumentList.Arguments.Count != 0)
        {
            return false;
        }

        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Name.Identifier.ValueText == "GetExecutingAssembly" &&
               IsAssemblyTypeExpression(memberAccess.Expression, dbContextType, compilationModel, cancellationToken);
    }

    private static bool IsAssemblyTypeExpression(
        ExpressionSyntax expression,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        if (expression.ToString() is "System.Reflection.Assembly" or "global::System.Reflection.Assembly")
            return true;

        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "Assembly" &&
                                               !HasLocalAssemblyValueInScope(identifier, cancellationToken) &&
                                               !HasAssemblyMember(dbContextType) &&
                                               (HasSystemReflectionAssemblyAliasInScope(identifier, compilationModel, cancellationToken) ||
                                                HasSystemReflectionUsing(identifier, compilationModel, cancellationToken)) &&
                                               !HasAssemblyAliasInScope(identifier, compilationModel, cancellationToken) &&
                                               !HasVisibleAssemblyType(dbContextType, compilationModel.Compilation.GetTypeByMetadataName("System.Reflection.Assembly")),
            _ => false
        };
    }

}
