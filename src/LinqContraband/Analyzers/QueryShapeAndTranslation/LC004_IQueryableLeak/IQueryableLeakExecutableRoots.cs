using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

internal sealed partial class IQueryableLeakCompilationState
{
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
}
