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
        if (GetSaveReceiver(saveOperation) is not ILocalReferenceOperation localReference)
            return false;

        return IsFreshContextLocalDeclaredInsideLoopBody(
            localReference.Local,
            loop,
            saveOperation,
            executionOperation);
    }

    private static bool IsSaveReceiverFreshContextForDirectDelegateInvocation(
        IOperation saveOperation,
        ILoopOperation loop,
        IInvocationOperation delegateInvocation)
    {
        return IsSaveReceiverFreshContextDeclaredInsideLoopBody(
                   saveOperation,
                   loop,
                   GetFreshContextExecutionOperation(saveOperation, delegateInvocation)) ||
               IsSaveReceiverFreshContextPassedToDirectDelegateInvocation(
                   saveOperation,
                   loop,
                   delegateInvocation);
    }

    private static bool IsSaveReceiverFreshContextPassedToDirectDelegateInvocation(
        IOperation saveOperation,
        ILoopOperation loop,
        IInvocationOperation delegateInvocation)
    {
        if (GetSaveReceiver(saveOperation) is not IParameterReferenceOperation parameterReference)
            return false;

        var argument = FindDelegateInvocationArgument(delegateInvocation, parameterReference.Parameter.Ordinal);
        if (argument?.Value.UnwrapConversions() is not ILocalReferenceOperation argumentLocal)
            return false;

        return !IsParameterWrittenBeforeOperationInContainingRoot(saveOperation, parameterReference.Parameter) &&
               IsFreshContextLocalDeclaredInsideLoopBody(
                   argumentLocal.Local,
                   loop,
                   saveOperation,
                   delegateInvocation);
    }

    private static bool IsSaveReceiverFreshContextPassedThroughDelegateInvocation(
        IOperation saveOperation,
        ILoopOperation loop,
        IInvocationOperation innerDelegateInvocation,
        IInvocationOperation outerDelegateInvocation)
    {
        if (GetSaveReceiver(saveOperation) is not IParameterReferenceOperation saveParameter)
            return false;

        var innerArgument = FindDelegateInvocationArgument(innerDelegateInvocation, saveParameter.Parameter.Ordinal);
        if (innerArgument?.Value.UnwrapConversions() is not IParameterReferenceOperation wrapperParameter)
            return false;

        var outerArgument = FindDelegateInvocationArgument(outerDelegateInvocation, wrapperParameter.Parameter.Ordinal);
        if (outerArgument?.Value.UnwrapConversions() is not ILocalReferenceOperation outerLocal)
            return false;

        return !IsParameterWrittenBeforeOperationInContainingRoot(saveOperation, saveParameter.Parameter) &&
               !IsParameterWrittenBeforeOperationInContainingRoot(innerDelegateInvocation, wrapperParameter.Parameter) &&
               IsFreshContextLocalDeclaredInsideLoopBody(
                   outerLocal.Local,
                   loop,
                   saveOperation,
                   outerDelegateInvocation);
    }

    private static IArgumentOperation? FindDelegateInvocationArgument(IInvocationOperation delegateInvocation, int parameterOrdinal)
    {
        foreach (var argument in delegateInvocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == parameterOrdinal)
                return argument;
        }

        return parameterOrdinal >= 0 && parameterOrdinal < delegateInvocation.Arguments.Length
            ? delegateInvocation.Arguments[parameterOrdinal]
            : null;
    }

    private static bool IsFreshContextLocalDeclaredInsideLoopBody(
        ILocalSymbol local,
        ILoopOperation loop,
        IOperation saveOperation,
        IOperation executionOperation)
    {
        var declaration = loop.Body
            .Descendants()
            .OfType<IVariableDeclaratorOperation>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, local));

        return declaration?.Initializer?.Value is IObjectCreationOperation objectCreation &&
               objectCreation.Type?.IsDbContext() == true &&
               !IsLocalWrittenBeforeSaveExecution(loop.Body, saveOperation, executionOperation, local);
    }

    private static IOperation? GetSaveReceiver(IOperation saveOperation)
    {
        return saveOperation switch
        {
            IInvocationOperation invocation => invocation.GetInvocationReceiver(),
            IMethodReferenceOperation methodReference => methodReference.Instance?.UnwrapConversions(),
            _ => null,
        };
    }

    private static bool IsLocalWrittenBeforeSaveExecution(
        IOperation scope,
        IOperation saveOperation,
        IOperation executionOperation,
        ILocalSymbol local)
    {
        var saveRoot = saveOperation.FindOwningExecutableRoot();
        if (saveRoot != null &&
            IsLocalWrittenBeforeOperation(
                saveRoot,
                saveOperation,
                local,
                requiredRoot: saveRoot,
                localFunctionLookupScope: scope))
        {
            return true;
        }

        var executionRoot = executionOperation.FindOwningExecutableRoot();
        return !ReferenceEquals(saveOperation, executionOperation) &&
               IsLocalWrittenBeforeOperation(
                   scope,
                   executionOperation,
                   local,
                   saveRoot,
                   executionRoot,
                   localFunctionLookupScope: scope);
    }

    private static bool IsLocalWrittenBeforeOperation(
        IOperation scope,
        IOperation operation,
        ILocalSymbol local,
        IOperation? ignoredRoot = null,
        IOperation? requiredRoot = null,
        IOperation? localFunctionLookupScope = null)
    {
        var operationStart = operation.Syntax.SpanStart;

        return scope.Descendants()
                   .OfType<ISimpleAssignmentOperation>()
                   .Any(assignment => assignment.Syntax.SpanStart < operationStart &&
                                      IsRelevantWriteRoot(assignment, ignoredRoot, requiredRoot) &&
                                      CanReachDestination(assignment, operation) &&
                                      IsLocalReference(assignment.Target, local)) ||
               scope.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart < operationStart &&
                                    IsRelevantWriteRoot(argument, ignoredRoot, requiredRoot) &&
                                    CanReachDestination(argument, operation) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsLocalReference(argument.Value, local)) ||
               IsLocalWrittenByCalledLocalFunctionBeforeOperation(
                   scope,
                   operation,
                   local,
                   ignoredRoot,
                   requiredRoot,
                   localFunctionLookupScope ?? scope);
    }

    private static bool IsParameterWrittenBeforeOperationInContainingRoot(
        IOperation operation,
        IParameterSymbol parameter)
    {
        var root = operation.FindOwningExecutableRoot();
        return root != null &&
               IsParameterWrittenBeforeOperation(
                   root,
                   operation,
                   parameter,
                   requiredRoot: root,
                   localFunctionLookupScope: root);
    }

    private static bool IsParameterWrittenBeforeOperation(
        IOperation scope,
        IOperation operation,
        IParameterSymbol parameter,
        IOperation? ignoredRoot = null,
        IOperation? requiredRoot = null,
        IOperation? localFunctionLookupScope = null)
    {
        var operationStart = operation.Syntax.SpanStart;

        return scope.Descendants()
                   .OfType<ISimpleAssignmentOperation>()
                   .Any(assignment => assignment.Syntax.SpanStart < operationStart &&
                                      IsRelevantWriteRoot(assignment, ignoredRoot, requiredRoot) &&
                                      CanReachDestination(assignment, operation) &&
                                      IsParameterReference(assignment.Target, parameter)) ||
               scope.Descendants()
                   .OfType<IArgumentOperation>()
                   .Any(argument => argument.Syntax.SpanStart < operationStart &&
                                    IsRelevantWriteRoot(argument, ignoredRoot, requiredRoot) &&
                                    CanReachDestination(argument, operation) &&
                                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                    IsParameterReference(argument.Value, parameter)) ||
               IsParameterWrittenByCalledLocalFunctionBeforeOperation(
                   scope,
                   operation,
                   parameter,
                   ignoredRoot,
                   requiredRoot,
                   localFunctionLookupScope ?? scope);
    }

    private static bool IsParameterWrittenByCalledLocalFunctionBeforeOperation(
        IOperation scope,
        IOperation operation,
        IParameterSymbol parameter,
        IOperation? ignoredRoot,
        IOperation? requiredRoot,
        IOperation localFunctionLookupScope)
    {
        var operationStart = operation.Syntax.SpanStart;

        return scope.Descendants()
            .OfType<IInvocationOperation>()
            .Any(invocation => invocation.Syntax.SpanStart < operationStart &&
                               IsRelevantWriteRoot(invocation, ignoredRoot, requiredRoot) &&
                               CanReachDestination(invocation, operation) &&
                               TryFindLocalFunction(localFunctionLookupScope, invocation.TargetMethod, out var localFunction) &&
                               IsParameterWrittenInsideRoot(localFunction, parameter));
    }

    private static bool IsLocalWrittenByCalledLocalFunctionBeforeOperation(
        IOperation scope,
        IOperation operation,
        ILocalSymbol local,
        IOperation? ignoredRoot,
        IOperation? requiredRoot,
        IOperation localFunctionLookupScope)
    {
        var operationStart = operation.Syntax.SpanStart;

        return scope.Descendants()
            .OfType<IInvocationOperation>()
            .Any(invocation => invocation.Syntax.SpanStart < operationStart &&
                               IsRelevantWriteRoot(invocation, ignoredRoot, requiredRoot) &&
                               CanReachDestination(invocation, operation) &&
                               TryFindLocalFunction(localFunctionLookupScope, invocation.TargetMethod, out var localFunction) &&
                               IsLocalWrittenInsideRoot(localFunction, local));
    }

    private static bool IsRelevantWriteRoot(IOperation write, IOperation? ignoredRoot, IOperation? requiredRoot)
    {
        var writeRoot = write.FindOwningExecutableRoot();
        return !ReferenceEquals(writeRoot, ignoredRoot) &&
               (requiredRoot == null || ReferenceEquals(writeRoot, requiredRoot));
    }

    private static bool IsLocalReference(IOperation operation, ILocalSymbol local)
    {
        return operation.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, local);
    }
}
