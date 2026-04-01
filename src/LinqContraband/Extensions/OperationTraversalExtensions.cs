using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

public static partial class AnalysisExtensions
{
    public static bool IsInsideLoop(this IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is ILoopOperation) return true;
            current = current.Parent;
        }

        return false;
    }

    public static bool IsInsideAsyncForEach(this IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IForEachLoopOperation forEach && forEach.IsAsynchronous)
                return true;
            current = current.Parent;
        }

        return false;
    }

    public static ILoopOperation? FindEnclosingLoop(this IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is ILoopOperation loop)
                return loop;

            current = current.Parent;
        }

        return null;
    }

    public static string GetLoopKind(this ILoopOperation loop)
    {
        return loop.Syntax switch
        {
            ForEachStatementSyntax forEach when forEach.AwaitKeyword != default => "await foreach",
            ForEachStatementSyntax => "foreach",
            ForStatementSyntax => "for",
            WhileStatementSyntax => "while",
            DoStatementSyntax => "do",
            _ => "loop"
        };
    }

    public static IOperation? FindOwningExecutableRoot(this IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
            if (current is IMethodBodyOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                return current;

            current = current.Parent;
        }

        return null;
    }

    public static bool SharesOwningExecutableRoot(this IOperation operation, IOperation other)
    {
        var left = operation.FindOwningExecutableRoot();
        var right = other.FindOwningExecutableRoot();

        return left != null && right != null && ReferenceEquals(left, right);
    }
}
