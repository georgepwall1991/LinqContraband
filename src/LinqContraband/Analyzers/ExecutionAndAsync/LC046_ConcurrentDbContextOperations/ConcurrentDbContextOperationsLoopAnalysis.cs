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
        foreach (var argument in invocation.Arguments)
        {
            if (receiver != null &&
                (ReferenceEquals(argument.Value, receiver) ||
                 argument.Value.Syntax.Span.Equals(receiver.Syntax.Span)))
            {
                continue;
            }

            if ((IsRequiredCallableParameter(argument.Parameter) ||
                 IsRequiredQuerySequenceParameter(
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

    private static bool IsRequiredQuerySequenceParameter(
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
               parameter.Type is INamedTypeSymbol
               {
                   Name: "IEnumerable",
                   Arity: 1
               } enumerable &&
               enumerable.ContainingNamespace?.ToString() ==
               "System.Collections.Generic";
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
            IDelegateCreationOperation or
            IInstanceReferenceOperation or
            ITypeOfOperation)
        {
            return true;
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
                return StableMemberValueIsDefinitelyNonNull(
                           propertyReference.Property,
                           executableRoot,
                           propertyReference.Syntax.SpanStart) &&
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

            case IParameterReferenceOperation:
            case IInstanceReferenceOperation:
                return true;

            default:
                return OperationEvaluationIsDefinitelyNonThrowing(
                    receiver,
                    executableRoot);
        }
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
        var listType = invocation.SemanticModel?.Compilation.GetTypeByMetadataName(
            "System.Collections.Generic.List`1");
        if (listType == null ||
            invocation.TargetMethod.Name != "Add" ||
            !SymbolEqualityComparer.Default.Equals(
                invocation.TargetMethod.ContainingType.OriginalDefinition,
                listType) ||
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
               SymbolEqualityComparer.Default.Equals(
                   assignedType.OriginalDefinition,
                   listType) &&
               (assignedValue is IObjectCreationOperation ||
                assignedValue.Kind.ToString() == "CollectionExpression");
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
                var localFunction =
                    GetContainingLocalFunction(localReference);
                if (localFunction != null)
                {
                    if (CanOperationRunBefore(
                            localReference,
                            executableRoot,
                            beforePosition) ||
                        LocalFunctionCanRunBefore(
                            localFunction.Symbol,
                            executableRoot,
                            beforePosition,
                            new HashSet<IMethodSymbol>(
                                SymbolEqualityComparer.Default)))
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
            if (!SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod.OriginalDefinition,
                    localFunction.OriginalDefinition))
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
