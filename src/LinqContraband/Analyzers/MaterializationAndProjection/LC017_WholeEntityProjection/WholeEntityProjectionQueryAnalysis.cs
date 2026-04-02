using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionAnalyzer
{
    private QueryChainAnalysis AnalyzeQueryChain(IInvocationOperation invocation)
    {
        var result = new QueryChainAnalysis();
        var current = invocation.GetInvocationReceiver();

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation prevInvocation)
            {
                if (prevInvocation.TargetMethod.Name == "Select") result.HasSelect = true;
                current = prevInvocation.GetInvocationReceiver(false);
                continue;
            }

            TryExtractDbSetInfo(current, result);
            break;
        }

        return result;
    }

    private static void TryExtractDbSetInfo(IOperation operation, QueryChainAnalysis result)
    {
        var type = operation switch
        {
            IPropertyReferenceOperation propRef => propRef.Type,
            IFieldReferenceOperation fieldRef => fieldRef.Type,
            _ => operation.Type
        };

        if (type != null && type.IsDbSet())
        {
            result.IsEfQuery = true;
            result.EntityType = GetElementType(type);
        }
    }

    private static ITypeSymbol? GetElementType(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length > 0)
        {
            return namedType.TypeArguments[0];
        }

        return null;
    }

    private static List<IPropertySymbol> GetEntityProperties(ITypeSymbol entityType)
    {
        var properties = new List<IPropertySymbol>();
        var current = entityType;

        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop &&
                    prop.DeclaredAccessibility == Accessibility.Public &&
                    !prop.IsStatic &&
                    prop.GetMethod != null)
                {
                    properties.Add(prop);
                }
            }

            current = current.BaseType;
        }

        return properties;
    }

    private static (ILocalSymbol Symbol, IOperation Declaration)? FindVariableAssignment(IInvocationOperation invocation)
    {
        var parent = invocation.Parent;

        while (parent != null)
        {
            if (parent is IVariableDeclaratorOperation declarator)
            {
                return (declarator.Symbol, declarator);
            }

            if (parent is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation localRef)
            {
                return (localRef.Local, assignment);
            }

            if (parent is IExpressionStatementOperation) break;
            if (parent is IReturnOperation) break;

            parent = parent.Parent;
        }

        return null;
    }
}
