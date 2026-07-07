using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

public sealed partial class SaveChangesInLoopAnalyzer
{
    private static bool IsSaveReceiverFreshContextDeclaredInsideLoopBody(IInvocationOperation invocation, ILoopOperation loop)
    {
        var receiver = invocation.GetInvocationReceiver();
        if (receiver is not ILocalReferenceOperation localReference)
            return false;

        var declaration = loop.Body
            .Descendants()
            .OfType<IVariableDeclaratorOperation>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, localReference.Local));

        return declaration?.Initializer?.Value is IObjectCreationOperation objectCreation &&
               objectCreation.Type?.IsDbContext() == true &&
               !IsLocalWrittenBeforeInvocation(loop.Body, invocation, localReference.Local);
    }

    private static bool IsLocalWrittenBeforeInvocation(IOperation scope, IInvocationOperation invocation, ILocalSymbol local)
    {
        var invocationStart = invocation.Syntax.SpanStart;

        return scope.Descendants()
                   .OfType<ISimpleAssignmentOperation>()
                   .Any(assignment => assignment.Syntax.SpanStart < invocationStart &&
                                      IsLocalReference(assignment.Target, local)) ||
               scope.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart < invocationStart &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local));
    }

    private static bool IsLocalReference(IOperation operation, ILocalSymbol local)
    {
        return operation.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, local);
    }
}
