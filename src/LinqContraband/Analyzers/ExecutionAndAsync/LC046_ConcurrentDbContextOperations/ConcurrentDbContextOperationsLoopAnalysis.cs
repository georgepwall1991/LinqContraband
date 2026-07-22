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
                OriginCanChangeInside(operation.Origin, loop.Body) ||
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

    private static bool OriginCanChangeInside(ContextOrigin origin, IOperation body)
    {
        return EnumerateOutsideNestedExecutables(body).Any(candidate =>
            candidate is IAssignmentOperation assignment &&
            ReferencesOrigin(assignment.Target, origin) ||
            candidate is IIncrementOrDecrementOperation increment &&
            ReferencesOrigin(increment.Target, origin) ||
            candidate is IArgumentOperation argument &&
            argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
            ReferencesOrigin(argument.Value, origin) ||
            candidate is IDynamicInvocationOperation dynamicInvocation &&
            ReferencesOrigin(dynamicInvocation, origin));
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
