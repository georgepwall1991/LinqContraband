using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
                !TryGetSingleRepeatedInvocation(
                    loop,
                    executableRoot,
                    out var invocation) ||
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

    private static bool TryGetSingleRepeatedInvocation(
        IForEachLoopOperation loop,
        IOperation executableRoot,
        out IInvocationOperation invocation)
    {
        var statement = loop.Body is IBlockOperation { Operations.Length: 1 } block
            ? block.Operations[0]
            : loop.Body;
        if (statement is not IExpressionStatementOperation expressionStatement)
        {
            invocation = null!;
            return false;
        }

        var expression = UnwrapConversionsAndParentheses(expressionStatement.Operation);
        if (expression is ISimpleAssignmentOperation assignment &&
            assignment.Target.UnwrapConversions() is IDiscardOperation &&
            UnwrapConversionsAndParentheses(assignment.Value) is
                IInvocationOperation discardedInvocation)
        {
            invocation = discardedInvocation;
            return true;
        }

        if (LoopHasProvenRepeatedListExecutions(loop) &&
            TryGetStableFrameworkListAdd(
                expression,
                executableRoot,
                out var addInvocation,
                out var accumulator) &&
            addInvocation.Arguments.Length == 1 &&
            UnwrapNonThrowingConversionsAndParentheses(
                addInvocation.Arguments[0].Value) is
                IInvocationOperation addedInvocation &&
            InvocationEvaluationIsDefinitelyNonThrowing(
                addedInvocation,
                executableRoot) &&
            !OperationReferencesAccumulator(
                addedInvocation,
                executableRoot,
                accumulator,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                new HashSet<int>()) &&
            !OperationReferencesAccumulator(
                loop.Collection,
                executableRoot,
                accumulator,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                new HashSet<int>()))
        {
            invocation = addedInvocation;
            return true;
        }

        invocation = null!;
        return false;
    }

    private static bool InvocationEvaluationIsDefinitelyNonThrowing(
        IInvocationOperation invocation,
        IOperation executableRoot)
    {
        var receiver = GetEvaluationReceiver(invocation);
        if (!RequiredCallableArgumentsAreDefinitelyValid(
                invocation,
                receiver,
                executableRoot))
        {
            return false;
        }

        if (receiver != null &&
            !QueryReceiverEvaluationIsDefinitelyNonThrowing(
                receiver,
                executableRoot))
        {
            return false;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (receiver != null &&
                (ReferenceEquals(argument.Value, receiver) ||
                 argument.Value.Syntax.Span.Equals(receiver.Syntax.Span)))
            {
                continue;
            }

            if (!OperationEvaluationIsDefinitelyNonThrowing(
                    argument.Value,
                    executableRoot))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RequiredCallableArgumentsAreDefinitelyValid(
        IInvocationOperation invocation,
        IOperation? receiver,
        IOperation executableRoot)
    {
        if (!DbContextSetNameIsDefinitelyValid(invocation, executableRoot))
            return false;

        foreach (var argument in invocation.Arguments)
        {
            if (receiver != null &&
                (ReferenceEquals(argument.Value, receiver) ||
                 argument.Value.Syntax.Span.Equals(receiver.Syntax.Span)))
            {
                continue;
            }

            if ((IsRequiredCallableParameter(argument.Parameter) ||
                 IsRequiredQueryArgument(invocation, argument.Parameter) ||
                 IsRequiredTerminalArgument(invocation, argument.Parameter) ||
                 IsRequiredRawSqlParametersArgument(
                     invocation,
                     argument.Parameter)) &&
                !OperationIsDefinitelyNonNull(
                    argument.Value,
                    executableRoot,
                    invocation.Syntax.SpanStart,
                    new HashSet<ILocalSymbol>(
                        SymbolEqualityComparer.Default)))
            {
                return false;
            }

            if (IsFindKeyValuesArgument(invocation, argument.Parameter) &&
                !OperationIsDefinitelyNonEmptyArray(
                    argument.Value,
                    executableRoot,
                    invocation.Syntax.SpanStart,
                    new HashSet<ILocalSymbol>(
                        SymbolEqualityComparer.Default)))
            {
                return false;
            }

            if (IsRequiredSqlArgument(invocation, argument.Parameter) &&
                !OperationIsDefinitelyNonEmptySql(
                    argument.Value,
                    executableRoot,
                    invocation.Syntax.SpanStart,
                    new HashSet<ILocalSymbol>(
                        SymbolEqualityComparer.Default)))
            {
                return false;
            }

            if (IsCancellationTokenParameter(argument.Parameter) &&
                OperationIsDefinitelyCancelledToken(
                    argument.Value,
                    executableRoot,
                    invocation.Syntax.SpanStart,
                    new HashSet<ILocalSymbol>(
                        SymbolEqualityComparer.Default)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRequiredCallableParameter(
        IParameterSymbol? parameter)
    {
        if (parameter == null || parameter.HasExplicitDefaultValue)
            return false;

        if (parameter.Type.TypeKind == TypeKind.Delegate)
            return true;

        return parameter.Type is INamedTypeSymbol
               {
                   Name: "Expression",
                   Arity: 1,
                   TypeArguments.Length: 1
               } expression &&
               expression.ContainingNamespace?.ToString() ==
               "System.Linq.Expressions" &&
               expression.TypeArguments[0].TypeKind == TypeKind.Delegate;
    }

    private static bool IsRequiredQueryArgument(
        IInvocationOperation invocation,
        IParameterSymbol? parameter)
    {
        if (parameter == null ||
            parameter.HasExplicitDefaultValue ||
            !IsTransparentQueryInvocation(invocation))
        {
            return false;
        }

        return parameter.Type.IsIQueryable() ||
               parameter.Type.SpecialType == SpecialType.System_String ||
               parameter.Type is INamedTypeSymbol
               {
                   Name: "IEnumerable",
                   Arity: 1
               } enumerable &&
               enumerable.ContainingNamespace?.ToString() ==
               "System.Collections.Generic" ||
               parameter.Type is INamedTypeSymbol
               {
                   Name: "FormattableString",
                   Arity: 0
               } formattableString &&
               formattableString.ContainingNamespace?.ToString() == "System";
    }

    private static bool IsRequiredTerminalArgument(
        IInvocationOperation invocation,
        IParameterSymbol? parameter)
    {
        if (parameter == null || parameter.HasExplicitDefaultValue)
            return false;

        if (IsRequiredSqlArgument(invocation, parameter))
            return true;

        return IsFindKeyValuesArgument(invocation, parameter);
    }

    private static bool IsFindKeyValuesArgument(
        IInvocationOperation invocation,
        IParameterSymbol? parameter)
    {
        return parameter != null &&
               invocation.TargetMethod.Name == "FindAsync" &&
               (IsDbContextAsyncSink(invocation) ||
                IsDbSetFindAsync(invocation)) &&
               parameter.Name == "keyValues" &&
               parameter.Type is IArrayTypeSymbol
               {
                   ElementType.SpecialType: SpecialType.System_Object
               };
    }

    private static bool IsRequiredRawSqlParametersArgument(
        IInvocationOperation invocation,
        IParameterSymbol? parameter)
    {
        if (parameter == null ||
            parameter.Name != "parameters" ||
            !IsObjectSequenceType(parameter.Type))
        {
            return false;
        }

        return invocation.TargetMethod.Name == "ExecuteSqlRawAsync" &&
                   IsDatabaseFacadeAsyncSink(invocation) ||
               invocation.TargetMethod.Name == "FromSqlRaw" &&
                   IsTransparentQueryInvocation(invocation);
    }

    private static bool IsObjectSequenceType(ITypeSymbol type)
    {
        return type is IArrayTypeSymbol
        {
            ElementType.SpecialType: SpecialType.System_Object
        } ||
               type is INamedTypeSymbol
               {
                   Name: "IEnumerable",
                   Arity: 1,
               } enumerable &&
               enumerable.TypeArguments.Length == 1 &&
               enumerable.TypeArguments[0].SpecialType ==
                   SpecialType.System_Object &&
               enumerable.ContainingNamespace?.ToString() ==
               "System.Collections.Generic";
    }

    private static bool IsRequiredSqlArgument(
        IInvocationOperation invocation,
        IParameterSymbol? parameter)
    {
        if (parameter == null ||
            parameter.HasExplicitDefaultValue ||
            parameter.Name != "sql" ||
            !IsSqlArgumentType(parameter.Type))
        {
            return false;
        }

        return IsDatabaseFacadeAsyncSink(invocation) ||
               IsTransparentQueryInvocation(invocation) &&
               invocation.TargetMethod.Name is (
                   "FromSql" or
                   "FromSqlRaw" or
                   "FromSqlInterpolated");
    }

    private static bool IsSqlArgumentType(ITypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_String ||
               type is INamedTypeSymbol
               {
                   Name: "FormattableString",
                   Arity: 0
               } formattableString &&
               formattableString.ContainingNamespace?.ToString() == "System";
    }

    private static bool OperationIsDefinitelyNonEmptySql(
        IOperation operation,
        IOperation executableRoot,
        int beforePosition,
        ISet<ILocalSymbol> visitedLocals)
    {
        operation = UnwrapNonThrowingConversionsAndParentheses(operation);
        if (operation.ConstantValue is
            {
                HasValue: true,
                Value: string sql
            })
        {
            return sql.Any(character => !char.IsWhiteSpace(character));
        }

        if (operation.Syntax is InterpolatedStringExpressionSyntax
            interpolatedString &&
            interpolatedString.Contents
                .OfType<InterpolatedStringTextSyntax>()
                .Any(text => text.TextToken.ValueText
                    .Any(character => !char.IsWhiteSpace(character))))
        {
            return true;
        }

        if (operation is ILocalReferenceOperation localReference &&
            visitedLocals.Add(localReference.Local) &&
            LocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                beforePosition) &&
            LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                localReference.Local,
                beforePosition,
                out var assignedValue))
        {
            return OperationIsDefinitelyNonEmptySql(
                assignedValue,
                executableRoot,
                localReference.Syntax.SpanStart,
                visitedLocals);
        }

        return false;
    }

    private static bool OperationIsDefinitelyNonEmptyArray(
        IOperation operation,
        IOperation executableRoot,
        int beforePosition,
        ISet<ILocalSymbol> visitedLocals)
    {
        operation = UnwrapNonThrowingConversionsAndParentheses(operation);
        if (operation is IArrayCreationOperation arrayCreation)
        {
            if (arrayCreation.Initializer != null)
            {
                return arrayCreation.Initializer.ElementValues.Length > 0 &&
                       arrayCreation.Initializer.ElementValues.All(element =>
                           OperationIsDefinitelyNonNull(
                               element,
                               executableRoot,
                               beforePosition,
                               new HashSet<ILocalSymbol>(
                                   SymbolEqualityComparer.Default)));
            }

            return false;
        }

        if (operation is ILocalReferenceOperation localReference &&
            visitedLocals.Add(localReference.Local) &&
            LocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                beforePosition) &&
            LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                localReference.Local,
                beforePosition,
                out var assignedValue))
        {
            return OperationIsDefinitelyNonEmptyArray(
                assignedValue,
                executableRoot,
                localReference.Syntax.SpanStart,
                visitedLocals);
        }

        return false;
    }

    private static bool IsCancellationTokenParameter(
        IParameterSymbol? parameter)
    {
        return parameter?.Type is INamedTypeSymbol
        {
            Name: "CancellationToken",
            Arity: 0
        } token &&
               token.ContainingNamespace?.ToString() == "System.Threading";
    }

    private static bool OperationIsDefinitelyCancelledToken(
        IOperation operation,
        IOperation executableRoot,
        int beforePosition,
        ISet<ILocalSymbol> visitedLocals)
    {
        operation = UnwrapNonThrowingConversionsAndParentheses(operation);
        if (operation is IObjectCreationOperation
            {
                Type:
                {
                    Name: "CancellationToken",
                    ContainingNamespace: { } tokenNamespace
                },
                Arguments.Length: 1
            } creation &&
            tokenNamespace.ToString() == "System.Threading" &&
            creation.Arguments[0].Value.ConstantValue is
            {
                HasValue: true,
                Value: true
            })
        {
            return true;
        }

        if (operation is ILocalReferenceOperation localReference &&
            visitedLocals.Add(localReference.Local) &&
            LocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                beforePosition) &&
            LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                localReference.Local,
                beforePosition,
                out var assignedValue))
        {
            return OperationIsDefinitelyCancelledToken(
                assignedValue,
                executableRoot,
                localReference.Syntax.SpanStart,
                visitedLocals);
        }

        return false;
    }

    private static bool OperationIsDefinitelyNonNull(
        IOperation operation,
        IOperation executableRoot,
        int beforePosition,
        ISet<ILocalSymbol> visitedLocals)
    {
        operation = UnwrapNonThrowingConversionsAndParentheses(operation);
        if (operation.Type?.IsValueType == true &&
            !IsNullableValueType(operation.Type))
        {
            return true;
        }

        if (operation.ConstantValue.HasValue)
            return operation.ConstantValue.Value != null;

        if (operation is IObjectCreationOperation or
            IArrayCreationOperation or
            IAnonymousFunctionOperation or
            IInterpolatedStringOperation or
            IInstanceReferenceOperation or
            ITypeOfOperation)
        {
            return true;
        }

        if (operation is IDelegateCreationOperation delegateCreation)
        {
            var target = UnwrapNonThrowingConversionsAndParentheses(
                delegateCreation.Target);
            return target is IAnonymousFunctionOperation ||
                   target is IMethodReferenceOperation methodReference &&
                   (methodReference.Instance == null ||
                    QueryReceiverEvaluationIsDefinitelyNonThrowing(
                        methodReference.Instance,
                        executableRoot));
        }

        if (operation is IPropertyReferenceOperation propertyReference)
        {
            return StableMemberValueIsDefinitelyNonNull(
                       propertyReference.Property,
                       executableRoot,
                       beforePosition) &&
                   propertyReference.Instance != null &&
                   QueryReceiverEvaluationIsDefinitelyNonThrowing(
                       propertyReference.Instance,
                       executableRoot);
        }

        if (operation is IFieldReferenceOperation fieldReference)
        {
            return StableMemberValueIsDefinitelyNonNull(
                       fieldReference.Field,
                       executableRoot,
                       beforePosition) &&
                   fieldReference.Instance != null &&
                   QueryReceiverEvaluationIsDefinitelyNonThrowing(
                       fieldReference.Instance,
                       executableRoot);
        }

        if (operation is ILocalReferenceOperation localReference &&
            visitedLocals.Add(localReference.Local) &&
            LocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                beforePosition) &&
            LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                localReference.Local,
                beforePosition,
                out var assignedValue))
        {
            return OperationIsDefinitelyNonNull(
                assignedValue,
                executableRoot,
                localReference.Syntax.SpanStart,
                visitedLocals);
        }

        return false;
    }

    private static IOperation? GetEvaluationReceiver(
        IInvocationOperation invocation)
    {
        if (invocation.Instance != null)
            return invocation.Instance;

        if (!invocation.TargetMethod.IsExtensionMethod)
            return null;

        return invocation.Arguments
            .FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)
            ?.Value;
    }

    private static bool QueryReceiverEvaluationIsDefinitelyNonThrowing(
        IOperation receiver,
        IOperation executableRoot)
    {
        receiver = UnwrapNonThrowingConversionsAndParentheses(receiver);
        switch (receiver)
        {
            case IInvocationOperation receiverInvocation:
                return (IsTransparentQueryInvocation(receiverInvocation) ||
                        IsDbContextSetInvocation(receiverInvocation)) &&
                       InvocationEvaluationIsDefinitelyNonThrowing(
                           receiverInvocation,
                           executableRoot);

            case IPropertyReferenceOperation propertyReference:
                return (IsKnownDbContextDatabaseProperty(
                            propertyReference.Property) ||
                        StableMemberValueIsDefinitelyNonNull(
                            propertyReference.Property,
                            executableRoot,
                            propertyReference.Syntax.SpanStart)) &&
                       propertyReference.Instance != null &&
                       QueryReceiverEvaluationIsDefinitelyNonThrowing(
                           propertyReference.Instance,
                           executableRoot);

            case IFieldReferenceOperation fieldReference:
                return StableMemberValueIsDefinitelyNonNull(
                           fieldReference.Field,
                           executableRoot,
                           fieldReference.Syntax.SpanStart) &&
                       fieldReference.Instance != null &&
                       QueryReceiverEvaluationIsDefinitelyNonThrowing(
                           fieldReference.Instance,
                           executableRoot);

            case ILocalReferenceOperation localReference:
                return OperationIsDefinitelyNonNull(
                    localReference,
                    executableRoot,
                    localReference.Syntax.SpanStart,
                    new HashSet<ILocalSymbol>(
                        SymbolEqualityComparer.Default));

            case IParameterReferenceOperation parameterReference:
                return ParameterReferenceIsDefinitelyNonNull(
                    parameterReference,
                    executableRoot);

            case IInstanceReferenceOperation:
                return true;

            default:
                return OperationEvaluationIsDefinitelyNonThrowing(
                    receiver,
                    executableRoot);
        }
    }

    private static bool ParameterReferenceIsDefinitelyNonNull(
        IParameterReferenceOperation parameterReference,
        IOperation executableRoot)
    {
        if (parameterReference.Parameter.NullableAnnotation ==
            NullableAnnotation.None)
            return false;

        var flowState = parameterReference.SemanticModel?
            .GetTypeInfo(parameterReference.Syntax)
            .Nullability.FlowState;
        if (parameterReference.Syntax.Ancestors().Any(ancestor =>
                ancestor.IsKind(
                    SyntaxKind.SuppressNullableWarningExpression)) &&
            !ParameterHasPriorNullExitGuard(
                parameterReference,
                executableRoot))
        {
            return false;
        }

        if (parameterReference.Parameter.NullableAnnotation ==
            NullableAnnotation.NotAnnotated)
        {
            return true;
        }

        return flowState == NullableFlowState.NotNull;
    }

    private static bool ParameterHasPriorNullExitGuard(
        IParameterReferenceOperation parameterReference,
        IOperation executableRoot)
    {
        var semanticModel = parameterReference.SemanticModel;
        if (semanticModel == null)
            return false;

        for (SyntaxNode? current = parameterReference.Syntax;
             current?.Parent != null;
             current = current.Parent)
        {
            if (current.Parent is not BlockSyntax block ||
                current is not StatementSyntax containingStatement)
            {
                continue;
            }

            foreach (var statement in block.Statements)
            {
                if (statement.SpanStart >= containingStatement.SpanStart)
                    break;

                if (statement is IfStatementSyntax ifStatement &&
                    BranchAlwaysExits(ifStatement.Statement) &&
                    ConditionProvesParameterNull(
                        ifStatement.Condition,
                        parameterReference.Parameter,
                        semanticModel) &&
                    !ParameterHasWriteBetween(
                        executableRoot,
                        parameterReference.Parameter,
                        ifStatement.Span.End,
                        parameterReference.Syntax.SpanStart))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ParameterHasWriteBetween(
        IOperation executableRoot,
        IParameterSymbol parameter,
        int afterPosition,
        int beforePosition)
    {
        foreach (var operation in executableRoot.Descendants())
        {
            if (operation.Syntax.SpanStart <= afterPosition ||
                operation.Syntax.SpanStart >= beforePosition ||
                !CanOperationRunBefore(
                    operation,
                    executableRoot,
                    beforePosition))
            {
                continue;
            }

            if (operation is IAssignmentOperation assignment &&
                assignment.Target.ReferencesParameter(parameter))
            {
                return true;
            }

            if (operation is IVariableDeclaratorOperation declarator &&
                declarator.Symbol.RefKind != RefKind.None &&
                declarator.Initializer?.Value.ReferencesParameter(parameter) ==
                true)
            {
                return true;
            }

            if (operation is IArgumentOperation argument &&
                argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                argument.Value.ReferencesParameter(parameter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConditionProvesParameterNull(
        ExpressionSyntax condition,
        IParameterSymbol parameter,
        SemanticModel semanticModel)
    {
        condition = UnwrapParentheses(condition);
        if (condition is IsPatternExpressionSyntax
            {
                Expression: { } expression,
                Pattern: ConstantPatternSyntax
                {
                    Expression: { } constant
                }
            })
        {
            return constant.IsKind(SyntaxKind.NullLiteralExpression) &&
                   ExpressionReferencesParameter(
                       expression,
                       parameter,
                       semanticModel);
        }

        if (condition is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.EqualsExpression) &&
            semanticModel.GetOperation(binary) is
                IBinaryOperation { OperatorMethod: null })
        {
            var left = UnwrapParentheses(binary.Left);
            var right = UnwrapParentheses(binary.Right);
            return left.IsKind(SyntaxKind.NullLiteralExpression) &&
                       ExpressionReferencesParameter(
                           right,
                           parameter,
                           semanticModel) ||
                   right.IsKind(SyntaxKind.NullLiteralExpression) &&
                       ExpressionReferencesParameter(
                           left,
                           parameter,
                           semanticModel);
        }

        return false;
    }

    private static bool ExpressionReferencesParameter(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        SemanticModel semanticModel)
    {
        expression = UnwrapParentheses(expression);
        return SymbolEqualityComparer.Default.Equals(
            semanticModel.GetSymbolInfo(expression).Symbol,
            parameter);
    }

    private static ExpressionSyntax UnwrapParentheses(
        ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    private static bool IsKnownDbContextDatabaseProperty(
        IPropertySymbol property)
    {
        return property.Name == "Database" &&
               property.ContainingType.Name == "DbContext" &&
               property.ContainingNamespace?.ToString() ==
               "Microsoft.EntityFrameworkCore" &&
               property.Type.Name == "DatabaseFacade" &&
               property.Type.ContainingNamespace?.ToString() ==
               "Microsoft.EntityFrameworkCore.Infrastructure";
    }

    private static bool StableMemberValueIsDefinitelyNonNull(
        ISymbol member,
        IOperation executableRoot,
        int beforePosition)
    {
        if (member.IsStatic ||
            member is IFieldSymbol { IsReadOnly: false } ||
            member is IPropertySymbol { SetMethod: not null } ||
            MemberHasConstructorWrite(member, executableRoot))
        {
            return false;
        }

        var semanticModel = executableRoot.SemanticModel;
        if (semanticModel == null)
            return false;

        foreach (var syntaxReference in member.DeclaringSyntaxReferences)
        {
            var declaration = syntaxReference.GetSyntax();
            var initializer = declaration switch
            {
                VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                PropertyDeclarationSyntax property => property.Initializer?.Value,
                _ => null
            };
            if (initializer == null)
                continue;

            var initializerModel =
                semanticModel.Compilation.GetSemanticModel(initializer.SyntaxTree);
            var initializerOperation =
                initializerModel.GetOperation(initializer);
            if (initializerOperation != null &&
                OperationIsDefinitelyNonNull(
                    initializerOperation,
                    executableRoot,
                    beforePosition,
                    new HashSet<ILocalSymbol>(
                        SymbolEqualityComparer.Default)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MemberHasConstructorWrite(
        ISymbol member,
        IOperation executableRoot)
    {
        var semanticModel = executableRoot.SemanticModel;
        if (semanticModel == null || member.ContainingType == null)
            return true;

        foreach (var typeReference in
                 member.ContainingType.DeclaringSyntaxReferences)
        {
            if (typeReference.GetSyntax() is not
                TypeDeclarationSyntax typeDeclaration)
            {
                continue;
            }

            var model = semanticModel.Compilation.GetSemanticModel(
                typeDeclaration.SyntaxTree);
            foreach (var constructor in typeDeclaration.Members
                         .OfType<ConstructorDeclarationSyntax>())
            {
                var bodyOperation = constructor.Body != null
                    ? model.GetOperation(constructor.Body)
                    : constructor.ExpressionBody != null
                        ? model.GetOperation(
                            constructor.ExpressionBody.Expression)
                        : null;
                if (bodyOperation != null &&
                    (OperationWritesStorage(bodyOperation, member) ||
                     bodyOperation.Descendants().Any(operation =>
                         OperationWritesStorage(operation, member))))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool OperationEvaluationIsDefinitelyNonThrowing(
        IOperation operation,
        IOperation executableRoot)
    {
        return !EvaluationOperationCanThrow(operation, executableRoot) &&
               EnumerateOutsideNestedExecutables(operation).All(candidate =>
                   !EvaluationOperationCanThrow(candidate, executableRoot));
    }

    private static bool EvaluationOperationCanThrow(
        IOperation operation,
        IOperation executableRoot)
    {
        if (operation is IArrayCreationOperation { IsImplicit: true })
            return false;

        return CanThrowBeforeTaskEnd(operation, executableRoot);
    }

    private static bool LoopHasProvenRepeatedListExecutions(
        IForEachLoopOperation loop)
    {
        if (loop.IsAsynchronous)
            return false;

        if (loop.SemanticModel == null ||
            loop.Syntax is not ForEachStatementSyntax forEachSyntax ||
            !loop.SemanticModel.GetForEachStatementInfo(forEachSyntax)
                .ElementConversion.IsIdentity)
        {
            return false;
        }

        var collection =
            UnwrapNonThrowingConversionsAndParentheses(loop.Collection);

        return collection is IArrayCreationOperation
               {
                   Initializer: { } initializer
               } &&
               initializer.ElementValues.Length >= 2 &&
               initializer.ElementValues.All(element =>
                   element.ConstantValue.HasValue);
    }

    private static bool TryGetStableFrameworkListAdd(
        IOperation expression,
        IOperation executableRoot,
        out IInvocationOperation invocation,
        out ILocalSymbol accumulator)
    {
        IOperation? receiver;
        if (expression is IInvocationOperation directInvocation)
        {
            invocation = directInvocation;
            receiver = invocation.Instance;
        }
        else if (expression is IConditionalAccessOperation conditionalAccess &&
                 UnwrapConversionsAndParentheses(conditionalAccess.WhenNotNull) is
                     IInvocationOperation conditionalInvocation)
        {
            invocation = conditionalInvocation;
            receiver = conditionalAccess.Operation;
        }
        else
        {
            invocation = null!;
            accumulator = null!;
            return false;
        }

        if (!IsStableFrameworkListAdd(
                invocation,
                receiver,
                executableRoot) ||
            receiver?.UnwrapConversions() is not
                ILocalReferenceOperation listLocal)
        {
            accumulator = null!;
            return false;
        }

        accumulator = listLocal.Local;
        return true;
    }

    private static bool IsStableFrameworkListAdd(
        IInvocationOperation invocation,
        IOperation? receiver,
        IOperation executableRoot)
    {
        var compilation = invocation.SemanticModel?.Compilation;
        if (compilation == null)
            return false;

        var listType = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        var hashSetType = compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1");
        var queueType = compilation.GetTypeByMetadataName("System.Collections.Generic.Queue`1");
        var collectionType = compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1");
        var containingType = invocation.TargetMethod.ContainingType.OriginalDefinition;
        var methodName = invocation.TargetMethod.Name;
        var isSupportedAdd =
            methodName == "Add" &&
            ((listType != null &&
              SymbolEqualityComparer.Default.Equals(containingType, listType)) ||
             (hashSetType != null &&
              SymbolEqualityComparer.Default.Equals(containingType, hashSetType)) ||
             (collectionType != null &&
              SymbolEqualityComparer.Default.Equals(containingType, collectionType)));
        var isSupportedEnqueue =
            methodName == "Enqueue" &&
            queueType != null &&
            SymbolEqualityComparer.Default.Equals(containingType, queueType);
        if ((!isSupportedAdd && !isSupportedEnqueue) ||
            receiver?.UnwrapConversions() is not
                ILocalReferenceOperation listLocal ||
            !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                listLocal.Local,
                invocation.Syntax.SpanStart,
                out var assignedValue) ||
            !LocalHasNoUntrackedWritesBefore(
                executableRoot,
                listLocal.Local,
                invocation.Syntax.SpanStart) ||
            AccumulatorEscapesBefore(
                executableRoot,
                listLocal.Local,
                receiver.Syntax.SpanStart))
        {
            return false;
        }

        assignedValue = UnwrapNonUserDefinedConversions(assignedValue);
        return assignedValue.Type is INamedTypeSymbol assignedType &&
               IsAllowedFrameworkAccumulatorType(
                   assignedType.OriginalDefinition,
                   listType,
                   hashSetType,
                   queueType) &&
               IsSafeAccumulatorConstruction(assignedValue);
    }

    private static bool IsAllowedFrameworkAccumulatorType(
        INamedTypeSymbol assignedDefinition,
        INamedTypeSymbol? listType,
        INamedTypeSymbol? hashSetType,
        INamedTypeSymbol? queueType)
    {
        return (listType != null &&
                SymbolEqualityComparer.Default.Equals(assignedDefinition, listType)) ||
               (hashSetType != null &&
                SymbolEqualityComparer.Default.Equals(assignedDefinition, hashSetType)) ||
               (queueType != null &&
                SymbolEqualityComparer.Default.Equals(assignedDefinition, queueType));
    }

    private static bool IsSafeAccumulatorConstruction(
        IOperation assignedValue)
    {
        if (assignedValue is IObjectCreationOperation creation)
        {
            return creation.Arguments.Length == 0 &&
                   (creation.Initializer == null ||
                    creation.Initializer.Initializers.Length == 0);
        }

        return assignedValue.Kind.ToString() == "CollectionExpression" &&
               !assignedValue.Descendants().Any();
    }

    private static bool AccumulatorEscapesBefore(
        IOperation executableRoot,
        ILocalSymbol accumulator,
        int beforePosition)
    {
        foreach (var localReference in executableRoot.Descendants()
                     .OfType<ILocalReferenceOperation>())
        {
            if (SymbolEqualityComparer.Default.Equals(
                    localReference.Local,
                    accumulator) &&
                !IsSimpleAssignmentTarget(localReference))
            {
                var anonymousFunction =
                    GetContainingAnonymousFunction(localReference);
                if (anonymousFunction != null)
                {
                    if (CanOperationRunBefore(
                            anonymousFunction,
                            executableRoot,
                            beforePosition) &&
                        AnonymousFunctionCanRunOrEscapeBefore(
                            anonymousFunction,
                            executableRoot,
                            beforePosition))
                    {
                        return true;
                    }

                    continue;
                }

                var localFunction =
                    GetContainingLocalFunction(localReference);
                if (localFunction != null)
                {
                    if (LocalFunctionCanRunBefore(
                            localFunction.Symbol,
                            executableRoot,
                            beforePosition,
                            new HashSet<IMethodSymbol>(
                                SymbolEqualityComparer.Default)) ||
                        LocalFunctionEscapesBefore(
                            localFunction.Symbol,
                            executableRoot,
                            beforePosition))
                    {
                        return true;
                    }

                    continue;
                }

                if (localReference.Syntax.SpanStart < beforePosition ||
                    CanOperationRunBefore(
                        localReference,
                        executableRoot,
                        beforePosition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IAnonymousFunctionOperation? GetContainingAnonymousFunction(
        IOperation operation)
    {
        for (var current = operation.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation anonymousFunction)
                return anonymousFunction;

            if (current is ILocalFunctionOperation)
                return null;
        }

        return null;
    }

    private static bool AnonymousFunctionCanRunOrEscapeBefore(
        IAnonymousFunctionOperation anonymousFunction,
        IOperation executableRoot,
        int beforePosition)
    {
        if (!TryGetAnonymousFunctionBindingLocal(
                anonymousFunction,
                out var delegateLocal))
        {
            return true;
        }

        return LocalDelegateCanRunBefore(
                   delegateLocal,
                   executableRoot,
                   beforePosition) ||
               LocalDelegateEscapesBefore(
                   delegateLocal,
                   executableRoot,
                   beforePosition);
    }

    private static bool TryGetAnonymousFunctionBindingLocal(
        IAnonymousFunctionOperation anonymousFunction,
        out ILocalSymbol delegateLocal)
    {
        IOperation binding = anonymousFunction;
        while (binding.Parent is IConversionOperation or
               IDelegateCreationOperation or
               IParenthesizedOperation or
               IVariableInitializerOperation)
        {
            binding = binding.Parent;
        }

        if (binding.Parent is IVariableDeclaratorOperation declarator)
        {
            delegateLocal = declarator.Symbol;
            return true;
        }

        if (binding.Parent is ISimpleAssignmentOperation assignment &&
            assignment.Target.UnwrapConversions() is
                ILocalReferenceOperation targetLocal)
        {
            delegateLocal = targetLocal.Local;
            return true;
        }

        delegateLocal = null!;
        return false;
    }

    private static bool LocalDelegateCanRunBefore(
        ILocalSymbol delegateLocal,
        IOperation executableRoot,
        int beforePosition)
    {
        return executableRoot.Descendants()
            .OfType<IInvocationOperation>()
            .Any(invocation =>
                invocation.TargetMethod.MethodKind ==
                    MethodKind.DelegateInvoke &&
                invocation.Instance?.UnwrapConversions() is
                    ILocalReferenceOperation localReference &&
                SymbolEqualityComparer.Default.Equals(
                    localReference.Local,
                    delegateLocal) &&
                CanOperationRunBefore(
                    invocation,
                    executableRoot,
                    beforePosition));
    }

    private static ILocalFunctionOperation? GetContainingLocalFunction(
        IOperation operation)
    {
        for (var current = operation.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is ILocalFunctionOperation localFunction)
                return localFunction;
        }

        return null;
    }

    private static bool LocalFunctionCanRunBefore(
        IMethodSymbol localFunction,
        IOperation executableRoot,
        int beforePosition,
        ISet<IMethodSymbol> visitedLocalFunctions)
    {
        if (!visitedLocalFunctions.Add(localFunction.OriginalDefinition))
            return false;

        foreach (var invocation in executableRoot.Descendants()
                     .OfType<IInvocationOperation>())
        {
            if (!InvocationTargetsLocalFunction(
                    invocation,
                    localFunction,
                    executableRoot))
            {
                continue;
            }

            var containingLocalFunction =
                GetContainingLocalFunction(invocation);
            if (containingLocalFunction != null)
            {
                if (LocalFunctionCanRunBefore(
                        containingLocalFunction.Symbol,
                        executableRoot,
                        beforePosition,
                        visitedLocalFunctions))
                {
                    return true;
                }

                continue;
            }

            if (invocation.Syntax.SpanStart < beforePosition ||
                CanOperationRunBefore(
                    invocation,
                    executableRoot,
                    beforePosition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LocalFunctionEscapesBefore(
        IMethodSymbol localFunction,
        IOperation executableRoot,
        int beforePosition)
    {
        foreach (var methodReference in executableRoot.Descendants()
                     .OfType<IMethodReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    methodReference.Method.OriginalDefinition,
                    localFunction.OriginalDefinition) ||
                !CanOperationRunBefore(
                    methodReference,
                    executableRoot,
                    beforePosition))
            {
                continue;
            }

            IOperation binding = methodReference;
            while (binding.Parent is IConversionOperation or
                   IDelegateCreationOperation or
                   IParenthesizedOperation or
                   IVariableInitializerOperation)
            {
                binding = binding.Parent;
            }

            if (binding.Parent is IVariableDeclaratorOperation declarator)
            {
                if (LocalDelegateEscapesBefore(
                        declarator.Symbol,
                        executableRoot,
                        beforePosition))
                {
                    return true;
                }

                continue;
            }

            if (binding.Parent is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is
                    ILocalReferenceOperation targetLocal)
            {
                if (LocalDelegateEscapesBefore(
                        targetLocal.Local,
                        executableRoot,
                        beforePosition))
                {
                    return true;
                }

                continue;
            }

            return true;
        }

        return false;
    }

    private static bool LocalDelegateEscapesBefore(
        ILocalSymbol delegateLocal,
        IOperation executableRoot,
        int beforePosition)
    {
        foreach (var localReference in executableRoot.Descendants()
                     .OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    localReference.Local,
                    delegateLocal) ||
                !CanOperationRunBefore(
                    localReference,
                    executableRoot,
                    beforePosition))
            {
                continue;
            }

            IOperation use = localReference;
            while (use.Parent is IConversionOperation or
                   IParenthesizedOperation)
            {
                use = use.Parent;
            }

            if (use.Parent is IInvocationOperation invocation &&
                invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                ReferenceEquals(invocation.Instance, use))
            {
                continue;
            }

            if (!IsSimpleAssignmentTarget(localReference))
                return true;
        }

        return false;
    }

    private static bool InvocationTargetsLocalFunction(
        IInvocationOperation invocation,
        IMethodSymbol localFunction,
        IOperation executableRoot)
    {
        if (SymbolEqualityComparer.Default.Equals(
                invocation.TargetMethod.OriginalDefinition,
                localFunction.OriginalDefinition))
        {
            return true;
        }

        if (invocation.TargetMethod.MethodKind != MethodKind.DelegateInvoke ||
            invocation.Instance?.UnwrapConversions() is not
                ILocalReferenceOperation delegateLocal ||
            !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                delegateLocal.Local,
                invocation.Syntax.SpanStart,
                out var assignedValue))
        {
            return false;
        }

        assignedValue = assignedValue.UnwrapConversions();
        if (assignedValue is IDelegateCreationOperation delegateCreation)
            assignedValue = delegateCreation.Target.UnwrapConversions();

        return assignedValue is IMethodReferenceOperation methodReference &&
               SymbolEqualityComparer.Default.Equals(
                   methodReference.Method.OriginalDefinition,
                   localFunction.OriginalDefinition);
    }

    private static bool IsSimpleAssignmentTarget(
        ILocalReferenceOperation localReference)
    {
        return localReference.Parent is ISimpleAssignmentOperation assignment &&
               ReferenceEquals(assignment.Target, localReference);
    }

    private static bool OperationReferencesAccumulator(
        IOperation operation,
        IOperation executableRoot,
        ILocalSymbol accumulator,
        ISet<ILocalSymbol> visitedAliases,
        ISet<IMethodSymbol> visitedLocalFunctions,
        ISet<int> visitedAnonymousFunctions)
    {
        if (OperationReferencesAccumulatorLocal(
                operation,
                executableRoot,
                accumulator,
                visitedAliases,
                visitedLocalFunctions,
                visitedAnonymousFunctions))
        {
            return true;
        }

        foreach (var localReference in operation.Descendants()
                     .OfType<ILocalReferenceOperation>())
        {
            if (OperationReferencesAccumulatorLocal(
                    localReference,
                    executableRoot,
                    accumulator,
                    visitedAliases,
                    visitedLocalFunctions,
                    visitedAnonymousFunctions))
            {
                return true;
            }
        }

        var invocations = operation is IInvocationOperation rootInvocation
            ? new[] { rootInvocation }.Concat(
                operation.Descendants().OfType<IInvocationOperation>())
            : operation.Descendants().OfType<IInvocationOperation>();
        foreach (var invocation in invocations)
        {
            if (TryGetInvokedNestedExecutable(
                    invocation,
                    executableRoot,
                    visitedLocalFunctions,
                    visitedAnonymousFunctions,
                    out var nestedExecutable) &&
                OperationReferencesAccumulator(
                    nestedExecutable,
                    executableRoot,
                    accumulator,
                    visitedAliases,
                    visitedLocalFunctions,
                    visitedAnonymousFunctions))
            {
                return true;
            }
        }

        return false;
    }

    private static bool OperationReferencesAccumulatorLocal(
        IOperation operation,
        IOperation executableRoot,
        ILocalSymbol accumulator,
        ISet<ILocalSymbol> visitedAliases,
        ISet<IMethodSymbol> visitedLocalFunctions,
        ISet<int> visitedAnonymousFunctions)
    {
        if (operation is not ILocalReferenceOperation localReference)
            return false;

        if (SymbolEqualityComparer.Default.Equals(
                localReference.Local,
                accumulator))
        {
            return true;
        }

        if (!visitedAliases.Add(localReference.Local))
            return false;

        var hasAssignmentBeforeReference = false;
        foreach (var assignment in LocalAssignmentCache.GetAssignments(
                     executableRoot,
                     localReference.Local))
        {
            if (assignment.SpanStart >= localReference.Syntax.SpanStart)
                continue;

            hasAssignmentBeforeReference = true;
            if (OperationReferencesAccumulator(
                    assignment.Value,
                    executableRoot,
                    accumulator,
                    visitedAliases,
                    visitedLocalFunctions,
                    visitedAnonymousFunctions))
            {
                return true;
            }
        }

        return !hasAssignmentBeforeReference &&
               SymbolEqualityComparer.Default.Equals(
                   localReference.Local.Type,
                   accumulator.Type);
    }

    private static IOperation UnwrapNonThrowingConversionsAndParentheses(
        IOperation operation)
    {
        while (operation is IParenthesizedOperation ||
               operation is IConversionOperation currentConversion &&
               currentConversion.OperatorMethod == null &&
               (currentConversion.IsImplicit ||
                currentConversion.Conversion.IsImplicit))
        {
            operation = operation switch
            {
                IConversionOperation conversion => conversion.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => operation
            };
        }

        return operation;
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
