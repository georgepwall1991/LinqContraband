using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed partial class ConcurrentDbContextOperationsAnalyzer
{
    private static void AnalyzeProvablyRepeatedLoopOperations(
        IOperation executableRoot,
        OperationBlockAnalysisContext context,
        ISet<int> directlyReportedInvocations)
    {
        foreach (var loop in EnumerateOutsideNestedExecutables(executableRoot)
                     .OfType<IForEachLoopOperation>())
        {
            if (!CollectionHasAtLeastTwoElements(loop.Collection) ||
                !TryGetSingleDiscardedInvocation(loop.Body, out var invocation) ||
                !TryClassifyEfAsyncOperation(
                    invocation,
                    executableRoot,
                    context.CancellationToken,
                out var operation) ||
                IsOriginDeclaredInside(operation.Origin, loop.Body.Syntax) ||
                OriginCanChangeInside(operation.Origin, loop.Body, executableRoot) ||
                directlyReportedInvocations.Contains(invocation.Syntax.SpanStart))
            {
                continue;
            }

            var classifiedOperations = EnumerateOutsideNestedExecutables(loop.Body)
                .OfType<IInvocationOperation>()
                .Count(candidate => TryClassifyEfAsyncOperation(
                    candidate,
                    executableRoot,
                    context.CancellationToken,
                    out _));
            if (classifiedOperations != 1)
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    invocation.Syntax.GetLocation(),
                    operation.Origin.DisplayName));
        }
    }

    private static bool OriginCanChangeInside(
        ContextOrigin origin,
        IOperation body,
        IOperation executableRoot)
    {
        return ExecutedOperationsCanChangeOrigin(
            origin,
            body,
            executableRoot,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
            new HashSet<int>());
    }

    private static bool ExecutedOperationsCanChangeOrigin(
        ContextOrigin origin,
        IOperation executedRoot,
        IOperation executableRoot,
        ISet<IMethodSymbol> visitedLocalFunctions,
        ISet<int> visitedAnonymousFunctions)
    {
        var operations = EnumerateOutsideNestedExecutables(executedRoot).ToArray();
        if (operations.Any(candidate => IsOriginMutation(candidate, origin)))
            return true;

        foreach (var invocation in operations.OfType<IInvocationOperation>())
        {
            if (InvokedDelegateCanChangeOrigin(
                    invocation,
                    origin,
                    executableRoot,
                    visitedLocalFunctions,
                    visitedAnonymousFunctions))
            {
                return true;
            }

            if (!TryGetInvokedNestedExecutable(
                    invocation,
                    executableRoot,
                    visitedLocalFunctions,
                    visitedAnonymousFunctions,
                    out var nestedExecutable))
            {
                continue;
            }

            if (ExecutedOperationsCanChangeOrigin(
                    origin,
                    nestedExecutable,
                    executableRoot,
                    visitedLocalFunctions,
                    visitedAnonymousFunctions))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InvokedDelegateCanChangeOrigin(
        IInvocationOperation invocation,
        ContextOrigin origin,
        IOperation executableRoot,
        ISet<IMethodSymbol> visitedLocalFunctions,
        ISet<int> visitedAnonymousFunctions)
    {
        if (invocation.TargetMethod.MethodKind != MethodKind.DelegateInvoke ||
            invocation.Instance?.UnwrapConversions() is not ILocalReferenceOperation delegateLocal)
        {
            return false;
        }

        if (!LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                delegateLocal.Local,
                invocation.Syntax.SpanStart,
                out var assignedValue) ||
            !LocalHasNoUntrackedWritesBefore(
                executableRoot,
                delegateLocal.Local,
                invocation.Syntax.SpanStart))
        {
            return true;
        }

        return PossibleDelegateTargetCanChangeOrigin(
            assignedValue,
            origin,
            executableRoot,
            visitedLocalFunctions,
            visitedAnonymousFunctions);
    }

    private static bool PossibleDelegateTargetCanChangeOrigin(
        IOperation value,
        ContextOrigin origin,
        IOperation executableRoot,
        ISet<IMethodSymbol> visitedLocalFunctions,
        ISet<int> visitedAnonymousFunctions)
    {
        value = value.UnwrapConversions();
        if (value is IDelegateCreationOperation delegateCreation)
        {
            return PossibleDelegateTargetCanChangeOrigin(
                delegateCreation.Target,
                origin,
                executableRoot,
                visitedLocalFunctions,
                visitedAnonymousFunctions);
        }

        if (value is IAnonymousFunctionOperation anonymousFunction)
        {
            return visitedAnonymousFunctions.Add(anonymousFunction.Syntax.SpanStart) &&
                   ExecutedOperationsCanChangeOrigin(
                       origin,
                       anonymousFunction.Body,
                       executableRoot,
                       visitedLocalFunctions,
                       visitedAnonymousFunctions);
        }

        if (value is IMethodReferenceOperation methodReference)
        {
            var localFunction = executableRoot.Descendants()
                .OfType<ILocalFunctionOperation>()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.Symbol.OriginalDefinition,
                    methodReference.Method.OriginalDefinition));
            return localFunction?.Body != null &&
                   visitedLocalFunctions.Add(localFunction.Symbol) &&
                   ExecutedOperationsCanChangeOrigin(
                       origin,
                       localFunction.Body,
                       executableRoot,
                       visitedLocalFunctions,
                       visitedAnonymousFunctions);
        }

        if (value is IConditionalOperation conditional)
        {
            return PossibleDelegateTargetCanChangeOrigin(
                       conditional.WhenTrue,
                       origin,
                       executableRoot,
                       visitedLocalFunctions,
                       visitedAnonymousFunctions) ||
                   conditional.WhenFalse != null &&
                   PossibleDelegateTargetCanChangeOrigin(
                       conditional.WhenFalse,
                       origin,
                       executableRoot,
                       visitedLocalFunctions,
                       visitedAnonymousFunctions);
        }

        if (value is ICoalesceOperation coalesce)
        {
            return PossibleDelegateTargetCanChangeOrigin(
                       coalesce.Value,
                       origin,
                       executableRoot,
                       visitedLocalFunctions,
                       visitedAnonymousFunctions) ||
                   PossibleDelegateTargetCanChangeOrigin(
                       coalesce.WhenNull,
                       origin,
                       executableRoot,
                       visitedLocalFunctions,
                       visitedAnonymousFunctions);
        }

        if (value is ISwitchExpressionOperation switchExpression)
        {
            return switchExpression.Arms.Any(arm => PossibleDelegateTargetCanChangeOrigin(
                arm.Value,
                origin,
                executableRoot,
                visitedLocalFunctions,
                visitedAnonymousFunctions));
        }

        return false;
    }

    private static bool IsOriginMutation(IOperation candidate, ContextOrigin origin)
    {
        return candidate is IAssignmentOperation assignment &&
               ReferencesOrigin(assignment.Target, origin) ||
               candidate is IIncrementOrDecrementOperation increment &&
               ReferencesOrigin(increment.Target, origin) ||
               candidate is IArgumentOperation argument &&
               argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
               ReferencesOrigin(argument.Value, origin) ||
               candidate is IDynamicInvocationOperation dynamicInvocation &&
               ReferencesOrigin(dynamicInvocation, origin);
    }

    private static bool TryGetInvokedNestedExecutable(
        IInvocationOperation invocation,
        IOperation executableRoot,
        ISet<IMethodSymbol> visitedLocalFunctions,
        ISet<int> visitedAnonymousFunctions,
        out IOperation nestedExecutable)
    {
        var localFunction = executableRoot.Descendants()
            .OfType<ILocalFunctionOperation>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(
                candidate.Symbol.OriginalDefinition,
                invocation.TargetMethod.OriginalDefinition));
        if (localFunction?.Body != null && visitedLocalFunctions.Add(localFunction.Symbol))
        {
            nestedExecutable = localFunction.Body;
            return true;
        }

        if (invocation.TargetMethod.MethodKind != MethodKind.DelegateInvoke ||
            invocation.Instance?.UnwrapConversions() is not ILocalReferenceOperation delegateLocal ||
            !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                delegateLocal.Local,
                invocation.Syntax.SpanStart,
                out var assignedValue))
        {
            nestedExecutable = null!;
            return false;
        }

        assignedValue = assignedValue.UnwrapConversions();
        if (assignedValue is IDelegateCreationOperation delegateCreation)
            assignedValue = delegateCreation.Target.UnwrapConversions();

        if (assignedValue is IAnonymousFunctionOperation anonymousFunction &&
            visitedAnonymousFunctions.Add(anonymousFunction.Syntax.SpanStart))
        {
            nestedExecutable = anonymousFunction.Body;
            return true;
        }

        if (assignedValue is IMethodReferenceOperation methodReference)
        {
            localFunction = executableRoot.Descendants()
                .OfType<ILocalFunctionOperation>()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.Symbol.OriginalDefinition,
                    methodReference.Method.OriginalDefinition));
            if (localFunction?.Body != null && visitedLocalFunctions.Add(localFunction.Symbol))
            {
                nestedExecutable = localFunction.Body;
                return true;
            }
        }

        nestedExecutable = null!;
        return false;
    }

    private static bool ReferencesOrigin(IOperation operation, ContextOrigin origin)
    {
        return ReferencesSymbol(operation, origin.Symbol) ||
               origin.ReceiverSymbol != null &&
               ReferencesSymbol(operation, origin.ReceiverSymbol);
    }

    private static bool ReferencesSymbol(IOperation operation, ISymbol symbol)
    {
        return IsSymbolReference(operation, symbol) ||
               operation.Descendants().Any(candidate => IsSymbolReference(candidate, symbol));
    }

    private static bool IsSymbolReference(IOperation operation, ISymbol symbol)
    {
        var referencedSymbol = operation switch
        {
            ILocalReferenceOperation localReference => (ISymbol)localReference.Local,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter,
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null
        };

        return SymbolEqualityComparer.Default.Equals(referencedSymbol, symbol);
    }

    private static bool CollectionHasAtLeastTwoElements(IOperation collection)
    {
        collection = collection.UnwrapConversions();
        return collection is IArrayCreationOperation
        {
            Initializer: { } initializer
        } && initializer.ElementValues.Length >= 2;
    }

    private static bool TryGetSingleDiscardedInvocation(
        IOperation body,
        out IInvocationOperation invocation)
    {
        var statement = body is IBlockOperation { Operations.Length: 1 } block
            ? block.Operations[0]
            : body;
        if (statement is IExpressionStatementOperation expressionStatement &&
            UnwrapConversionsAndParentheses(expressionStatement.Operation) is
                ISimpleAssignmentOperation assignment &&
            assignment.Target.UnwrapConversions() is IDiscardOperation &&
            UnwrapConversionsAndParentheses(assignment.Value) is
                IInvocationOperation discardedInvocation)
        {
            invocation = discardedInvocation;
            return true;
        }

        invocation = null!;
        return false;
    }

    private static IOperation UnwrapConversionsAndParentheses(IOperation operation)
    {
        while (operation is IConversionOperation ||
               operation is IParenthesizedOperation)
        {
            operation = operation switch
            {
                IConversionOperation currentConversion => currentConversion.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => operation
            };
        }

        return operation;
    }
}
