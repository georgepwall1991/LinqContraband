using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

internal sealed partial class IQueryableLeakCompilationState
{
    private bool IsEnumerableMethod(IMethodSymbol method)
    {
        return _linqEnumerableType != null &&
               SymbolEqualityComparer.Default.Equals(method.ContainingType, _linqEnumerableType);
    }

    private bool IsQueryableMethod(IMethodSymbol method)
    {
        return _linqQueryableType != null &&
               SymbolEqualityComparer.Default.Equals(method.ContainingType, _linqQueryableType);
    }

    private bool IsIEnumerableParameterType(ITypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type, _enumerableType) ||
               TryGetConstructedInterface(type, _enumerableGenericType, out var enumerableInterface) &&
               SymbolEqualityComparer.Default.Equals(type, enumerableInterface);
    }

    private bool IsIQueryableType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return SymbolEqualityComparer.Default.Equals(type, _queryableType) ||
               TryGetConstructedInterface(type, _queryableGenericType, out _) ||
               type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _queryableType));
    }

    private bool IsIEnumerableLike(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (SymbolEqualityComparer.Default.Equals(type, _enumerableType))
            return true;

        return TryGetConstructedInterface(type, _enumerableGenericType, out _) ||
               type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _enumerableType));
    }

    private bool TryGetConstructedInterface(ITypeSymbol? type, INamedTypeSymbol? interfaceType, out INamedTypeSymbol match)
    {
        match = null!;
        if (type == null || interfaceType == null)
            return false;

        if (type is INamedTypeSymbol namedType &&
            SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, interfaceType))
        {
            match = namedType;
            return true;
        }

        foreach (var currentInterface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(currentInterface.OriginalDefinition, interfaceType))
            {
                match = currentInterface;
                return true;
            }
        }

        return false;
    }

    private bool TryGetExecutableRoot(IMethodSymbol method, out IOperation executableRoot)
    {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
            var semanticModel = _compilation.GetSemanticModel(syntax.SyntaxTree);

            switch (syntax)
            {
                case MethodDeclarationSyntax methodDeclaration when methodDeclaration.Body != null:
                    var methodBody = semanticModel.GetOperation(methodDeclaration.Body);
                    if (methodBody != null)
                    {
                        executableRoot = methodBody;
                        return true;
                    }

                    break;

                case MethodDeclarationSyntax methodDeclaration when methodDeclaration.ExpressionBody != null:
                    var methodExpressionBody = semanticModel.GetOperation(methodDeclaration.ExpressionBody.Expression);
                    if (methodExpressionBody != null)
                    {
                        executableRoot = methodExpressionBody;
                        return true;
                    }

                    break;

                case LocalFunctionStatementSyntax localFunction when localFunction.Body != null:
                    var localFunctionBody = semanticModel.GetOperation(localFunction.Body);
                    if (localFunctionBody != null)
                    {
                        executableRoot = localFunctionBody;
                        return true;
                    }

                    break;

                case LocalFunctionStatementSyntax localFunction when localFunction.ExpressionBody != null:
                    var localFunctionExpressionBody = semanticModel.GetOperation(localFunction.ExpressionBody.Expression);
                    if (localFunctionExpressionBody != null)
                    {
                        executableRoot = localFunctionExpressionBody;
                        return true;
                    }

                    break;
            }
        }

        executableRoot = null!;
        return false;
    }

    private IEnumerable<IOperation> EnumerateOperations(IOperation executableRoot)
    {
        yield return executableRoot;

        foreach (var operation in executableRoot.Descendants())
        {
            if (IsInsideNestedExecutable(operation, executableRoot))
                continue;

            yield return operation;
        }
    }

    private static bool IsInsideNestedExecutable(IOperation operation, IOperation executableRoot)
    {
        var current = operation.Parent;
        while (current != null && !ReferenceEquals(current, executableRoot))
        {
            if (current is ILocalFunctionOperation or IAnonymousFunctionOperation)
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static IMethodSymbol GetOriginalTargetMethod(IMethodSymbol method)
    {
        return method.ReducedFrom ?? method;
    }
}
