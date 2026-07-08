using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

public sealed partial class SaveChangesInLoopAnalyzer
{
    private static bool IsSaveReceiverFreshContextDeclaredInsideLoopBody(IOperation saveOperation, ILoopOperation loop)
    {
        return IsSaveReceiverFreshContextDeclaredInsideLoopBody(saveOperation, loop, saveOperation);
    }

    private static bool IsSaveReceiverFreshContextDeclaredInsideLoopBody(
        IOperation saveOperation,
        ILoopOperation loop,
        IOperation executionOperation)
    {
        var receiver = saveOperation switch
        {
            IInvocationOperation invocation => invocation.GetInvocationReceiver(),
            IMethodReferenceOperation methodReference => methodReference.Instance?.UnwrapConversions(),
            _ => null,
        };

        if (receiver is not ILocalReferenceOperation localReference)
            return false;

        var declaration = loop.Body
            .Descendants()
            .OfType<IVariableDeclaratorOperation>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, localReference.Local));

        return declaration?.Initializer?.Value is IObjectCreationOperation objectCreation &&
               objectCreation.Type?.IsDbContext() == true &&
               !IsLocalWrittenBeforeOperation(loop.Body, executionOperation, localReference.Local);
    }

    private static bool IsLocalWrittenBeforeOperation(IOperation scope, IOperation operation, ILocalSymbol local)
    {
        var operationStart = operation.Syntax.SpanStart;

        return scope.Descendants()
                   .OfType<ISimpleAssignmentOperation>()
                   .Any(assignment => assignment.Syntax.SpanStart < operationStart &&
                                      IsLocalReference(assignment.Target, local)) ||
               scope.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart < operationStart &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local));
    }

    private static bool IsLocalReference(IOperation operation, ILocalSymbol local)
    {
        return operation.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, local);
    }
}
