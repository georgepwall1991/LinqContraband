using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

public static partial class AnalysisExtensions
{
    public static IOperation? GetInvocationReceiver(this IInvocationOperation invocation, bool unwrapConversions = true)
    {
        var receiver = invocation.Instance ??
                       (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

        if (unwrapConversions && receiver != null)
            receiver = receiver.UnwrapConversions();

        return receiver;
    }

    /// <summary>
    /// A call that runs the query it is given and hands back loaded data (a collection or a single row). Anything
    /// chained after it is LINQ to Objects over rows that are already in memory, not part of the EF query, so a
    /// receiver-chain walk that meets one has left the query. <c>AsEnumerable()</c> is not one: it defers.
    /// </summary>
    public static bool IsQueryExecutingMaterializer(this IInvocationOperation invocation)
    {
        return (invocation.TargetMethod.Name is
                   "ToList" or "ToListAsync" or
                   "ToArray" or "ToArrayAsync" or
                   "ToDictionary" or "ToDictionaryAsync" or
                   "ToHashSet" or "ToHashSetAsync" or
                   "ToLookup" or
                   "First" or "FirstOrDefault" or "FirstAsync" or "FirstOrDefaultAsync" or
                   "Single" or "SingleOrDefault" or "SingleAsync" or "SingleOrDefaultAsync" or
                   "Last" or "LastOrDefault" or "LastAsync" or "LastOrDefaultAsync") &&
               !invocation.Type.IsIQueryable();
    }

    public static ITypeSymbol? GetInvocationReceiverType(this IInvocationOperation invocation, bool unwrapConversions = true)
    {
        return invocation.GetInvocationReceiver(unwrapConversions)?.Type;
    }

    public static IOperation UnwrapConversions(this IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
            if (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
                continue;
            }

            if (current is IParenthesizedOperation parenthesized)
            {
                current = parenthesized.Operand;
                continue;
            }

            if (current is IAwaitOperation awaitOp)
            {
                current = awaitOp.Operation;
                continue;
            }

            break;
        }

        return current ?? operation;
    }
}
