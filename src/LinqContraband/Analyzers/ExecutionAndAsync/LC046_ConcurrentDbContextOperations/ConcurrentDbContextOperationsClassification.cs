using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed partial class ConcurrentDbContextOperationsAnalyzer
{
    private static readonly HashSet<string> QueryAsyncSinkNames = new(StringComparer.Ordinal)
    {
        "AllAsync",
        "AnyAsync",
        "AverageAsync",
        "ContainsAsync",
        "CountAsync",
        "ElementAtAsync",
        "ElementAtOrDefaultAsync",
        "ExecuteDeleteAsync",
        "ExecuteUpdateAsync",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "ForEachAsync",
        "LastAsync",
        "LastOrDefaultAsync",
        "LoadAsync",
        "LongCountAsync",
        "MaxAsync",
        "MinAsync",
        "SingleAsync",
        "SingleOrDefaultAsync",
        "SumAsync",
        "ToArrayAsync",
        "ToDictionaryAsync",
        "ToHashSetAsync",
        "ToListAsync"
    };

    private static readonly HashSet<string> DatabaseFacadeAsyncSinkNames = new(StringComparer.Ordinal)
    {
        "ExecuteSqlAsync",
        "ExecuteSqlInterpolatedAsync",
        "ExecuteSqlRawAsync"
    };

    private static bool TryClassifyEfAsyncOperation(
        IInvocationOperation invocation,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out EfOperation operation)
    {
        operation = default;

        if (!ReturnsTaskLike(invocation.TargetMethod.ReturnType))
            return false;

        // An EF call whose required arguments are invalid faults before it starts any
        // work, so it cannot overlap another operation. This proof is shared with the
        // loop gate so the two paths cannot disagree about what "starts" means.
        if (RequiredArgumentIsDefinitelyInvalid(invocation, executableRoot))
            return false;

        IOperation? source;
        if (IsDbContextAsyncSink(invocation))
        {
            source = invocation.Instance;
            if (source == null ||
                !TryResolveContextOrigin(source, executableRoot, invocation.Syntax.SpanStart, cancellationToken, out var contextOrigin))
            {
                return false;
            }

            operation = new EfOperation(invocation, contextOrigin);
            return true;
        }

        if (IsDbSetFindAsync(invocation))
        {
            source = invocation.Instance;
            if (source == null ||
                !TryResolveQueryContext(source, executableRoot, invocation.Syntax.SpanStart, cancellationToken, out var setOrigin))
            {
                return false;
            }

            operation = new EfOperation(invocation, setOrigin);
            return true;
        }

        if (IsDatabaseFacadeAsyncSink(invocation))
        {
            source = GetSemanticInvocationReceiver(invocation);
            if (source == null ||
                !TryResolveDatabaseFacadeContext(source, executableRoot, invocation.Syntax.SpanStart, cancellationToken, out var databaseOrigin))
            {
                return false;
            }

            operation = new EfOperation(invocation, databaseOrigin);
            return true;
        }

        if (!IsQueryableAsyncSink(invocation))
            return false;

        source = GetSemanticInvocationReceiver(invocation);
        if (source == null ||
            !TryResolveQueryContext(source, executableRoot, invocation.Syntax.SpanStart, cancellationToken, out var queryOrigin))
        {
            return false;
        }

        operation = new EfOperation(invocation, queryOrigin);
        return true;
    }

    private static bool TryClassifyDirectLocalFunctionEfTask(
        IInvocationOperation invocation,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out EfOperation operation)
    {
        operation = default;
        if (invocation.TargetMethod.MethodKind != MethodKind.LocalFunction)
            return false;

        var localFunction = executableRoot.Descendants()
            .OfType<ILocalFunctionOperation>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(
                candidate.Symbol.OriginalDefinition,
                invocation.TargetMethod.OriginalDefinition));
        if (localFunction?.Body == null ||
            localFunction.Symbol.Parameters.Length > 2 ||
            localFunction.Body.Operations.Length != 1 ||
            localFunction.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } returnedValue } ||
            returnedValue.UnwrapConversions() is not IInvocationOperation returnedInvocation ||
            returnedInvocation.TargetMethod.MethodKind == MethodKind.LocalFunction ||
            !TryClassifyEfAsyncOperation(
                returnedInvocation,
                localFunction.Body,
                cancellationToken,
                out var returnedOperation))
        {
            return false;
        }

        if (localFunction.Symbol.Parameters.Length > 0)
        {
            if (invocation.Arguments.Any(argument =>
                    !OperationEvaluationIsDefinitelyNonThrowing(
                        argument.Value,
                        executableRoot)))
            {
                return false;
            }

            if (BoundCancellationTokenArgumentIsDefinitelyCancelled(
                    invocation,
                    returnedInvocation,
                    executableRoot))
            {
                return false;
            }

            var contextParameters = localFunction.Symbol.Parameters
                .Where(parameter => parameter.Type.IsDbContext())
                .ToArray();
            if (contextParameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(
                    returnedOperation.Origin.Symbol,
                    contextParameters[0]))
            {
                var contextParameter = contextParameters[0];
                if (!ParameterIsUsedOnlyByInvocationReceiver(
                        contextParameter,
                        returnedInvocation) ||
                    ContextReceiverHasExplicitConversion(
                        contextParameter,
                        returnedInvocation) ||
                    !ReturnedInvocationInputsAreDefinitelyNonThrowing(
                        returnedInvocation,
                        localFunction.Body) ||
                    localFunction.Symbol.Parameters.Any(parameter =>
                        !SymbolEqualityComparer.Default.Equals(
                            parameter,
                            contextParameter) &&
                        (!ParameterIsUsedOnlyByNonThrowingArguments(
                             parameter,
                             returnedInvocation,
                             localFunction.Body) ||
                         !CancellationTokenParameterIsUsedDirectly(
                             parameter,
                             returnedInvocation))) ||
                    BoundRequiredArgumentIsDefinitelyInvalid(
                        invocation,
                        returnedInvocation,
                        executableRoot,
                        localFunction.Body))
                {
                    return false;
                }

                var argument = invocation.Arguments.FirstOrDefault(candidate =>
                    SymbolEqualityComparer.Default.Equals(
                        candidate.Parameter?.OriginalDefinition,
                        contextParameter.OriginalDefinition));
                if (argument == null ||
                    argument.IsImplicit ||
                    invocation.Arguments.Any(candidate =>
                        !candidate.IsImplicit &&
                        candidate.Value.Syntax.SpanStart <
                        argument.Value.Syntax.SpanStart) ||
                    !TryResolveContextOrigin(
                        argument.Value,
                        executableRoot,
                        invocation.Syntax.SpanStart,
                        cancellationToken,
                        out var argumentOrigin))
                {
                    return false;
                }

                operation = new EfOperation(invocation, argumentOrigin);
                return true;
            }

            if (!ReturnedInvocationInputsAreDefinitelyNonThrowing(
                    returnedInvocation,
                    localFunction.Body) ||
                localFunction.Symbol.Parameters.Any(candidate =>
                    !ParameterIsUsedOnlyByNonThrowingArguments(
                        candidate,
                        returnedInvocation,
                        localFunction.Body) ||
                    !CancellationTokenParameterIsUsedDirectly(
                        candidate,
                        returnedInvocation)) ||
                BoundRequiredArgumentIsDefinitelyInvalid(
                    invocation,
                    returnedInvocation,
                    executableRoot,
                    localFunction.Body))
            {
                return false;
            }

            if (IsOriginDeclaredInside(returnedOperation.Origin, localFunction.Syntax) ||
                !CapturedParameterOriginsHaveNoWritesBefore(
                    returnedOperation.Origin,
                    executableRoot,
                    invocation.Syntax.SpanStart))
            {
                return false;
            }

            operation = new EfOperation(invocation, returnedOperation.Origin);
            return true;
        }

        if (IsOriginDeclaredInside(returnedOperation.Origin, localFunction.Syntax) ||
            !CapturedParameterOriginsHaveNoWritesBefore(
                returnedOperation.Origin,
                executableRoot,
                invocation.Syntax.SpanStart))
        {
            return false;
        }

        operation = new EfOperation(invocation, returnedOperation.Origin);
        return true;
    }

    private static bool BoundCancellationTokenArgumentIsDefinitelyCancelled(
        IInvocationOperation invocation,
        IInvocationOperation returnedInvocation,
        IOperation executableRoot)
    {
        foreach (var returnedArgument in EnumerateOutsideNestedExecutables(returnedInvocation)
                     .OfType<IArgumentOperation>())
        {
            if (!IsCancellationTokenParameter(returnedArgument.Parameter) ||
                returnedArgument.Value.UnwrapConversions() is not
                    IParameterReferenceOperation parameterReference)
            {
                continue;
            }

            var callArgument = invocation.Arguments.FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.Parameter?.OriginalDefinition,
                    parameterReference.Parameter.OriginalDefinition));
            if (callArgument != null &&
                OperationIsDefinitelyCancelledToken(
                    callArgument.Value,
                    executableRoot,
                    invocation.Syntax.SpanStart,
                    new HashSet<ILocalSymbol>(
                        SymbolEqualityComparer.Default)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BoundRequiredArgumentIsDefinitelyInvalid(
        IInvocationOperation invocation,
        IInvocationOperation returnedInvocation,
        IOperation executableRoot,
        IOperation localFunctionBody)
    {
        foreach (var returnedArgument in EnumerateOutsideNestedExecutables(returnedInvocation)
                     .OfType<IArgumentOperation>())
        {
            if (returnedArgument.Parent is not IInvocationOperation argumentInvocation)
                continue;

            var value = returnedArgument.Value.UnwrapConversions();
            if (value is not IParameterReferenceOperation parameterReference)
            {
                var valueBeforePosition = invocation.Syntax.SpanStart;
                if (value is ILocalReferenceOperation localReference)
                {
                    if (CallArgumentsHaveDeconstructionWrite(
                            invocation,
                            localReference.Local) ||
                        HelperBodyMayWriteLocalBeforeArgument(
                            localFunctionBody,
                            localReference.Local,
                            returnedArgument.Value.Syntax.SpanStart))
                    {
                        return true;
                    }

                    if (TryGetDefinitelyEvaluatedCallArgumentAssignment(
                            invocation,
                            localReference.Local,
                            out var assignment))
                    {
                        value = assignment.Value.UnwrapConversions();
                        valueBeforePosition = assignment.Syntax.SpanStart;
                    }
                }

                var requiredArgumentMustBeProven = RequiredArgumentMustBeProven(
                    argumentInvocation,
                    returnedArgument.Parameter);
                var isCancellationToken = IsCancellationTokenParameter(
                    returnedArgument.Parameter);
                if (requiredArgumentMustBeProven &&
                    !RequiredArgumentValueIsDirect(value) ||
                    isCancellationToken &&
                    !RequiredArgumentValueIsDirect(value) &&
                    ValueReferencesBoundHelperParameter(value, invocation) ||
                    (requiredArgumentMustBeProven || isCancellationToken) &&
                    RequiredArgumentValueIsDefinitelyInvalid(
                        argumentInvocation,
                        returnedArgument.Parameter,
                        value,
                        executableRoot,
                        valueBeforePosition))
                {
                    return true;
                }

                continue;
            }

            var callArgument = invocation.Arguments.FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.Parameter?.OriginalDefinition,
                    parameterReference.Parameter.OriginalDefinition));
            if (callArgument != null &&
                (RequiredArgumentMustBeProven(
                     argumentInvocation,
                     returnedArgument.Parameter) &&
                 !RequiredArgumentValueIsDirect(
                     callArgument.Value.UnwrapConversions()) ||
                 RequiredArgumentValueIsDefinitelyInvalid(
                     argumentInvocation,
                     returnedArgument.Parameter,
                     callArgument.Value,
                     executableRoot,
                     callArgument.Value.Syntax.SpanStart)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CallArgumentsHaveDeconstructionWrite(
        IInvocationOperation invocation,
        ILocalSymbol local)
    {
        return invocation.Arguments
            .Where(argument => !argument.IsImplicit)
            .SelectMany(argument => new[] { argument.Value }
                .Concat(EnumerateOutsideNestedExecutables(argument.Value)))
            .OfType<IDeconstructionAssignmentOperation>()
            .Any(assignment => assignment.Target.ReferencesLocal(local));
    }

    private static bool HelperBodyMayWriteLocalBeforeArgument(
        IOperation localFunctionBody,
        ILocalSymbol local,
        int beforePosition)
    {
        return EnumerateOutsideNestedExecutables(localFunctionBody)
            .Where(operation => operation.Syntax.SpanStart < beforePosition)
            .Any(operation =>
                operation is IAssignmentOperation assignment &&
                assignment.Target.ReferencesLocal(local) ||
                operation is IDynamicInvocationOperation dynamicInvocation &&
                DynamicInvocationMayWriteLocal(dynamicInvocation, local) ||
                operation is IArgumentOperation argument &&
                argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                argument.Value.ReferencesLocal(local));
    }

    private static bool ValueReferencesBoundHelperParameter(
        IOperation value,
        IInvocationOperation invocation)
    {
        return new[] { value }
            .Concat(EnumerateOutsideNestedExecutables(value))
            .OfType<IParameterReferenceOperation>()
            .Any(reference => invocation.TargetMethod.Parameters.Any(parameter =>
                SymbolEqualityComparer.Default.Equals(
                    reference.Parameter.OriginalDefinition,
                    parameter.OriginalDefinition)));
    }

    private static bool TryGetDefinitelyEvaluatedCallArgumentAssignment(
        IInvocationOperation invocation,
        ILocalSymbol local,
        out ISimpleAssignmentOperation assignment)
    {
        assignment = null!;
        foreach (var candidate in invocation.Arguments
                     .Where(argument => !argument.IsImplicit)
                     .SelectMany(argument => new[] { argument.Value }
                         .Concat(EnumerateOutsideNestedExecutables(argument.Value))
                         .OfType<ISimpleAssignmentOperation>())
                     .Where(candidate =>
                         candidate.Target.UnwrapConversions() is
                             ILocalReferenceOperation localReference &&
                         SymbolEqualityComparer.Default.Equals(
                             localReference.Local,
                             local))
                     .OrderBy(candidate => candidate.Syntax.SpanStart))
        {
            if (!AssignmentIsDefinitelyEvaluatedByArgument(candidate))
                continue;

            assignment = candidate;
        }

        return assignment != null;
    }

    private static bool AssignmentIsDefinitelyEvaluatedByArgument(
        ISimpleAssignmentOperation assignment)
    {
        for (var current = assignment.Parent;
             current != null;
             current = current.Parent)
        {
            switch (current)
            {
                case IArgumentOperation:
                    return true;

                case IConversionOperation:
                case IParenthesizedOperation:
                    continue;

                case IBinaryOperation binary
                    when binary.OperatorKind is not (
                        BinaryOperatorKind.ConditionalAnd or
                        BinaryOperatorKind.ConditionalOr):
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool RequiredArgumentMustBeProven(
        IInvocationOperation invocation,
        IParameterSymbol? parameter)
    {
        return IsRequiredCallableParameter(parameter) ||
               IsRequiredQueryArgument(invocation, parameter) ||
               IsRequiredTerminalArgument(invocation, parameter) ||
               IsRequiredRawSqlParametersArgument(invocation, parameter) ||
               IsRequiredSqlArgument(invocation, parameter) ||
               IsDbContextSetNameArgument(invocation, parameter);
    }

    private static bool ReturnedInvocationInputsAreDefinitelyNonThrowing(
        IInvocationOperation returnedInvocation,
        IOperation localFunctionBody)
    {
        var receiver = GetSemanticInvocationReceiver(returnedInvocation);
        return EnumerateOutsideNestedExecutables(returnedInvocation)
            .OfType<IArgumentOperation>()
            .Where(argument =>
                !argument.IsImplicit &&
                (receiver == null ||
                 argument.Value.Syntax.Span != receiver.Syntax.Span))
            .All(argument =>
                OperationEvaluationIsDefinitelyNonThrowing(
                    argument.Value,
                    localFunctionBody) &&
                MethodReferenceReceiversAreDefinitelyNonThrowing(
                    argument.Value,
                    localFunctionBody));
    }

    private static bool MethodReferenceReceiversAreDefinitelyNonThrowing(
        IOperation operation,
        IOperation executableRoot)
    {
        return new[] { operation }
            .Concat(EnumerateOutsideNestedExecutables(operation))
            .OfType<IMethodReferenceOperation>()
            .All(reference =>
                reference.Instance == null ||
                QueryReceiverEvaluationIsDefinitelyNonThrowing(
                    reference.Instance,
                    executableRoot));
    }

    private static bool ParameterIsUsedOnlyByInvocationReceiver(
        IParameterSymbol parameter,
        IInvocationOperation invocation)
    {
        var receiver = GetSemanticInvocationReceiver(invocation);
        return receiver != null &&
               EnumerateOutsideNestedExecutables(invocation)
                   .OfType<IParameterReferenceOperation>()
                   .Where(reference => SymbolEqualityComparer.Default.Equals(
                       reference.Parameter,
                       parameter))
                   .All(reference => receiver.Syntax.Span.Contains(reference.Syntax.Span));
    }

    private static bool ContextReceiverHasExplicitConversion(
        IParameterSymbol parameter,
        IInvocationOperation invocation)
    {
        return EnumerateOutsideNestedExecutables(invocation)
            .OfType<IConversionOperation>()
            .Any(conversion =>
                !conversion.IsImplicit &&
                conversion.Operand.ReferencesParameter(parameter));
    }

    private static bool CancellationTokenParameterIsUsedDirectly(
        IParameterSymbol parameter,
        IInvocationOperation returnedInvocation)
    {
        if (!IsCancellationTokenParameter(parameter))
            return true;

        var parameterReferences = EnumerateOutsideNestedExecutables(returnedInvocation)
            .OfType<IParameterReferenceOperation>()
            .Where(reference => SymbolEqualityComparer.Default.Equals(
                reference.Parameter,
                parameter))
            .ToArray();
        if (parameterReferences.Length == 0)
            return true;

        var parameterArguments = EnumerateOutsideNestedExecutables(returnedInvocation)
            .OfType<IArgumentOperation>()
            .Where(argument => parameterReferences.Any(reference =>
                argument.Value.Syntax.Span.Contains(reference.Syntax.Span)) &&
                !EnumerateOutsideNestedExecutables(argument.Value)
                    .OfType<IArgumentOperation>()
                    .Any(nestedArgument => parameterReferences.Any(reference =>
                        nestedArgument.Value.Syntax.Span.Contains(
                            reference.Syntax.Span))))
            .ToArray();
        return parameterArguments.Length > 0 &&
               parameterReferences.All(reference => parameterArguments.Any(argument =>
                   argument.Value.Syntax.Span.Contains(reference.Syntax.Span))) &&
               parameterArguments.All(argument =>
                   argument.Value.UnwrapConversions() is
                       IParameterReferenceOperation parameterReference &&
                   SymbolEqualityComparer.Default.Equals(
                       parameterReference.Parameter,
                       parameter));
    }

    private static bool ParameterIsUsedOnlyByNonThrowingArguments(
        IParameterSymbol parameter,
        IInvocationOperation returnedInvocation,
        IOperation localFunctionBody)
    {
        var parameterReferences = EnumerateOutsideNestedExecutables(returnedInvocation)
            .OfType<IParameterReferenceOperation>()
            .Where(reference => SymbolEqualityComparer.Default.Equals(
                reference.Parameter,
                parameter))
            .ToArray();
        if (parameterReferences.Length == 0)
            return true;

        var parameterArguments = EnumerateOutsideNestedExecutables(returnedInvocation)
            .OfType<IArgumentOperation>()
            .Where(argument => parameterReferences.Any(reference =>
                argument.Value.Syntax.Span.Contains(reference.Syntax.Span)) &&
                !EnumerateOutsideNestedExecutables(argument.Value)
                    .OfType<IArgumentOperation>()
                    .Any(nestedArgument => parameterReferences.Any(reference =>
                        nestedArgument.Value.Syntax.Span.Contains(
                            reference.Syntax.Span))))
            .ToArray();
        return parameterArguments.Length > 0 &&
               parameterReferences.All(reference => parameterArguments.Any(argument =>
                   argument.Value.Syntax.Span.Contains(reference.Syntax.Span))) &&
               parameterArguments.All(argument =>
                   OperationEvaluationIsDefinitelyNonThrowing(
                       argument.Value,
                       localFunctionBody));
    }

    private static bool CapturedParameterOriginsHaveNoWritesBefore(
        ContextOrigin origin,
        IOperation executableRoot,
        int beforePosition)
    {
        return (origin.Symbol is not IParameterSymbol parameter ||
                ParameterHasNoWritesBefore(
                    executableRoot,
                    parameter,
                    beforePosition)) &&
               (origin.ReceiverSymbol is not IParameterSymbol receiverParameter ||
                ParameterHasNoWritesBefore(
                    executableRoot,
                    receiverParameter,
                    beforePosition));
    }

    private static bool IsDbContextAsyncSink(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return method.Name is ("SaveChangesAsync" or "FindAsync") &&
               IsEfDbContextMethod(method);
    }

    private static bool IsEfDbContextMethod(IMethodSymbol method)
    {
        for (IMethodSymbol? current = method;
             current != null;
             current = current.OverriddenMethod)
        {
            if (current.ContainingType.Name == "DbContext" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDbSetFindAsync(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return method.Name == "FindAsync" && IsEfDbSetMethod(method);
    }

    private static bool IsEfDbSetMethod(IMethodSymbol method)
    {
        for (IMethodSymbol? current = method;
             current != null;
             current = current.OverriddenMethod)
        {
            if (current.ContainingType.Name == "DbSet" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQueryableAsyncSink(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!QueryAsyncSinkNames.Contains(method.Name))
            return false;

        var containingType = method.ContainingType;
        var containingNamespace = containingType.ContainingNamespace?.ToString();
        if (containingNamespace != "Microsoft.EntityFrameworkCore")
            return false;

        if (containingType.Name is not (
                "EntityFrameworkQueryableExtensions" or
                "RelationalQueryableExtensions"))
        {
            return false;
        }

        var receiverType = GetSemanticInvocationReceiver(invocation)?.Type;
        return receiverType.IsIQueryable() || receiverType.IsDbSet();
    }

    private static bool IsDatabaseFacadeAsyncSink(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return DatabaseFacadeAsyncSinkNames.Contains(method.Name) &&
               method.ContainingType.Name == "RelationalDatabaseFacadeExtensions" &&
               method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool ReturnsTaskLike(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return type.Name is "Task" or "ValueTask" &&
               type.ContainingNamespace?.ToString() == "System.Threading.Tasks";
    }

    private static bool TryResolveQueryContext(
        IOperation source,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        out ContextOrigin origin)
    {
        return TryResolveQueryContext(
            source,
            executableRoot,
            beforePosition,
            cancellationToken,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            out origin);
    }

    private static bool TryResolveQueryContext(
        IOperation source,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visitedLocals,
        out ContextOrigin origin)
    {
        source = source.UnwrapConversions();

        switch (source)
        {
            case ILocalReferenceOperation localReference:
                if (!visitedLocals.Add(localReference.Local) ||
                    !LocalHasNoUntrackedWritesBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition) ||
                    !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition,
                        out var assignedValue,
                        cancellationToken))
                {
                    origin = default;
                    return false;
                }

                return TryResolveQueryContext(
                    assignedValue,
                    executableRoot,
                    localReference.Syntax.SpanStart,
                    cancellationToken,
                    visitedLocals,
                    out origin);

            case IPropertyReferenceOperation propertyReference
                when propertyReference.Property.Type.IsDbSet() &&
                     IsSourceVisibleAutoProperty(propertyReference.Property) &&
                     PropertyHasNoWritesBefore(
                         executableRoot,
                         propertyReference.Property,
                         beforePosition) &&
                     propertyReference.Instance != null:
                return TryResolveContextOrigin(
                    propertyReference.Instance,
                    executableRoot,
                    beforePosition,
                    cancellationToken,
                    out origin);

            case IFieldReferenceOperation fieldReference
                when fieldReference.Field.Type.IsDbSet() &&
                     fieldReference.Field.IsReadOnly &&
                     fieldReference.Instance != null:
                return TryResolveContextOrigin(
                    fieldReference.Instance,
                    executableRoot,
                    beforePosition,
                    cancellationToken,
                    out origin);

            case IInvocationOperation queryInvocation:
                if (IsDbContextSetInvocation(queryInvocation) &&
                    queryInvocation.Instance != null)
                {
                    if (RequiredArgumentIsDefinitelyInvalid(
                            queryInvocation,
                            executableRoot))
                    {
                        origin = default;
                        return false;
                    }

                    return TryResolveContextOrigin(
                        queryInvocation.Instance,
                        executableRoot,
                        beforePosition,
                        cancellationToken,
                        out origin);
                }

                if (IsTransparentQueryInvocation(queryInvocation))
                {
                    // Query construction that faults on its own arguments never yields a
                    // query, so no operation built on it can start or overlap.
                    if (RequiredArgumentIsDefinitelyInvalid(
                            queryInvocation,
                            executableRoot))
                    {
                        origin = default;
                        return false;
                    }

                    var receiver = GetSemanticInvocationReceiver(queryInvocation);
                    if (receiver != null)
                    {
                        return TryResolveQueryContext(
                            receiver,
                            executableRoot,
                            queryInvocation.Syntax.SpanStart,
                            cancellationToken,
                            visitedLocals,
                            out origin);
                    }
                }

                break;
        }

        origin = default;
        return false;
    }

    private static bool TryResolveContextOrigin(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        out ContextOrigin origin)
    {
        return TryResolveContextOrigin(
            expression,
            executableRoot,
            beforePosition,
            cancellationToken,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            out origin);
    }

    private static bool TryResolveContextOrigin(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visitedLocals,
        out ContextOrigin origin)
    {
        expression = expression.UnwrapConversions();

        switch (expression)
        {
            case IParameterReferenceOperation parameterReference
                when parameterReference.Parameter.Type.IsDbContext() &&
                     ParameterHasNoWritesBefore(
                         executableRoot,
                         parameterReference.Parameter,
                         beforePosition):
                origin = new ContextOrigin(parameterReference.Parameter, parameterReference.Parameter.Name);
                return true;

            case ILocalReferenceOperation localReference
                when localReference.Local.Type.IsDbContext():
                if (!visitedLocals.Add(localReference.Local) ||
                    !LocalHasNoUntrackedWritesBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition) ||
                    !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition,
                        out var assignedValue,
                        cancellationToken))
                {
                    origin = default;
                    return false;
                }

                if (assignedValue.UnwrapConversions() is IObjectCreationOperation creation &&
                    creation.Type.IsDbContext())
                {
                    origin = new ContextOrigin(localReference.Local, localReference.Local.Name);
                    return true;
                }

                return TryResolveContextOrigin(
                    assignedValue,
                    executableRoot,
                    localReference.Syntax.SpanStart,
                    cancellationToken,
                    visitedLocals,
                    out origin);

            case IFieldReferenceOperation fieldReference
                when fieldReference.Field.Type.IsDbContext() &&
                     fieldReference.Field.IsReadOnly &&
                     StableStorageHasAtMostOneWriteBefore(
                         executableRoot,
                         fieldReference.Field,
                         beforePosition):
                if (fieldReference.Field.IsStatic)
                {
                    origin = new ContextOrigin(fieldReference.Field, fieldReference.Field.Name);
                    return true;
                }

                if (fieldReference.Instance != null &&
                    TryResolveReceiverOrigin(
                        fieldReference.Instance,
                        executableRoot,
                        beforePosition,
                        cancellationToken,
                        new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                        out var fieldReceiver))
                {
                    origin = new ContextOrigin(
                        fieldReference.Field,
                        fieldReceiver,
                        fieldReference.Field.Name);
                    return true;
                }

                break;

            case IPropertyReferenceOperation propertyReference
                when propertyReference.Property.Type.IsDbContext() &&
                     IsStableAutoProperty(propertyReference.Property) &&
                     StableStorageHasAtMostOneWriteBefore(
                         executableRoot,
                         propertyReference.Property,
                         beforePosition):
                if (propertyReference.Property.IsStatic)
                {
                    origin = new ContextOrigin(propertyReference.Property, propertyReference.Property.Name);
                    return true;
                }

                if (propertyReference.Instance != null &&
                    TryResolveReceiverOrigin(
                        propertyReference.Instance,
                        executableRoot,
                        beforePosition,
                        cancellationToken,
                        new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                        out var propertyReceiver))
                {
                    origin = new ContextOrigin(
                        propertyReference.Property,
                        propertyReceiver,
                        propertyReference.Property.Name);
                    return true;
                }

                break;

            case IInstanceReferenceOperation instanceReference
                when instanceReference.Type.IsDbContext():
                origin = new ContextOrigin(instanceReference.Type!, "this");
                return true;
        }

        origin = default;
        return false;
    }

    private static bool ParameterHasNoWritesBefore(
        IOperation executableRoot,
        IParameterSymbol parameter,
        int beforePosition)
    {
        foreach (var operation in executableRoot.Descendants())
        {
            if (!CanOperationRunBefore(operation, executableRoot, beforePosition))
                continue;

            if (operation is IDynamicInvocationOperation dynamicInvocation &&
                DynamicInvocationMayWriteParameter(dynamicInvocation, parameter))
            {
                return false;
            }

            if (operation is IAssignmentOperation assignment &&
                assignment.Target.ReferencesParameter(parameter))
            {
                return false;
            }

            if (operation is IVariableDeclaratorOperation declarator &&
                declarator.Symbol.RefKind != RefKind.None &&
                declarator.Initializer?.Value.ReferencesParameter(parameter) == true)
            {
                return false;
            }

            if (operation is IArgumentOperation argument &&
                argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                argument.Value.ReferencesParameter(parameter))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PropertyHasNoWritesBefore(
        IOperation executableRoot,
        IPropertySymbol property,
        int beforePosition)
    {
        return !executableRoot.Descendants().Any(operation =>
            CanOperationRunBefore(operation, executableRoot, beforePosition) &&
            OperationWritesStorage(operation, property));
    }

    private static bool StableStorageHasAtMostOneWriteBefore(
        IOperation executableRoot,
        ISymbol storage,
        int beforePosition)
    {
        return executableRoot.Descendants().Count(operation =>
            CanOperationRunBefore(operation, executableRoot, beforePosition) &&
            OperationWritesStorage(operation, storage)) <= 1;
    }

    private static bool OperationWritesStorage(
        IOperation operation,
        ISymbol storage)
    {
        if (operation is not IAssignmentOperation assignment)
            return false;

        var target = assignment.Target.UnwrapConversions();
        if (target is IPropertyReferenceOperation propertyReference &&
            SymbolEqualityComparer.Default.Equals(propertyReference.Property, storage))
        {
            return true;
        }

        if (target is IFieldReferenceOperation fieldReference &&
            SymbolEqualityComparer.Default.Equals(fieldReference.Field, storage))
        {
            return true;
        }

        return target.Descendants().Any(candidate =>
            candidate is IPropertyReferenceOperation nestedProperty &&
            SymbolEqualityComparer.Default.Equals(nestedProperty.Property, storage) ||
            candidate is IFieldReferenceOperation nestedField &&
            SymbolEqualityComparer.Default.Equals(nestedField.Field, storage));
    }

    private static bool TryResolveReceiverOrigin(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visitedLocals,
        out ISymbol receiver)
    {
        expression = expression.UnwrapConversions();

        switch (expression)
        {
            case IParameterReferenceOperation parameterReference
                when ParameterHasNoWritesBefore(
                    executableRoot,
                    parameterReference.Parameter,
                    beforePosition):
                receiver = parameterReference.Parameter;
                return true;

            case ILocalReferenceOperation localReference:
                if (!visitedLocals.Add(localReference.Local) ||
                    !LocalHasNoUntrackedWritesBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition) ||
                    !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        localReference.Local,
                        beforePosition,
                        out var assignedValue,
                        cancellationToken))
                {
                    receiver = null!;
                    return false;
                }

                if (assignedValue.UnwrapConversions() is IObjectCreationOperation)
                {
                    receiver = localReference.Local;
                    return true;
                }

                return TryResolveReceiverOrigin(
                    assignedValue,
                    executableRoot,
                    localReference.Syntax.SpanStart,
                    cancellationToken,
                    visitedLocals,
                    out receiver);

            case IInstanceReferenceOperation instanceReference when instanceReference.Type != null:
                receiver = instanceReference.Type;
                return true;
        }

        receiver = null!;
        return false;
    }

    private static bool TryResolveDatabaseFacadeContext(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        out ContextOrigin origin)
    {
        return TryResolveDatabaseFacadeContext(
            expression,
            executableRoot,
            beforePosition,
            cancellationToken,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            out origin);
    }

    private static bool TryResolveDatabaseFacadeContext(
        IOperation expression,
        IOperation executableRoot,
        int beforePosition,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visitedLocals,
        out ContextOrigin origin)
    {
        expression = expression.UnwrapConversions();

        if (expression is ILocalReferenceOperation localReference &&
            visitedLocals.Add(localReference.Local) &&
            LocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                beforePosition) &&
            LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                executableRoot,
                localReference.Local,
                beforePosition,
                out var assignedValue,
                cancellationToken))
        {
            return TryResolveDatabaseFacadeContext(
                assignedValue,
                executableRoot,
                localReference.Syntax.SpanStart,
                cancellationToken,
                visitedLocals,
                out origin);
        }

        if (expression is IPropertyReferenceOperation propertyReference &&
            propertyReference.Property.Name == "Database" &&
            propertyReference.Property.Type.Name == "DatabaseFacade" &&
            propertyReference.Property.Type.ContainingNamespace?.ToString() ==
            "Microsoft.EntityFrameworkCore.Infrastructure" &&
            propertyReference.Instance != null)
        {
            return TryResolveContextOrigin(
                propertyReference.Instance,
                executableRoot,
                beforePosition,
                cancellationToken,
                out origin);
        }

        origin = default;
        return false;
    }

    private static bool IsDbContextSetInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Set" &&
               IsEfDbContextMethod(invocation.TargetMethod) &&
               invocation.Type.IsDbSet();
    }

    /// <summary>
    /// EF Core rejects a null or whitespace set name before constructing a query, so an
    /// operation rooted at such a name never starts and cannot overlap another. Shared by
    /// context-origin resolution and the loop argument gate so the two cannot drift.
    /// </summary>
    /// <summary>
    /// True only when a required argument is *provably* invalid, so the EF call faults
    /// before starting any work and cannot overlap another operation.
    /// </summary>
    /// <remarks>
    /// Deliberately the opposite polarity to the loop gate. The loop gate must positively
    /// prove an operation starts on every iteration, so it requires proof of validity.
    /// Outside loops the default is to report, so only demonstrated invalidity may
    /// suppress; otherwise a required argument supplied by a parameter or field, which
    /// cannot be proven either way, would silently drop a genuine diagnostic.
    /// </remarks>
    private static bool RequiredArgumentIsDefinitelyInvalid(
        IInvocationOperation invocation,
        IOperation executableRoot)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (RequiredArgumentValueIsDefinitelyInvalid(
                    invocation,
                    argument.Parameter,
                    argument.Value,
                    executableRoot,
                    argument.Value.Syntax.SpanStart))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiredArgumentValueIsDefinitelyInvalid(
        IInvocationOperation invocation,
        IParameterSymbol? parameter,
        IOperation argumentValue,
        IOperation executableRoot,
        int beforePosition)
    {
        return RequiredArgumentValueIsDefinitelyInvalid(
            invocation,
            parameter,
            argumentValue,
            executableRoot,
            beforePosition,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool RequiredArgumentValueIsDefinitelyInvalid(
        IInvocationOperation invocation,
        IParameterSymbol? parameter,
        IOperation argumentValue,
        IOperation executableRoot,
        int beforePosition,
        ISet<ILocalSymbol> visitedLocals)
    {
        var isRequired =
            IsRequiredCallableParameter(parameter) ||
            IsRequiredQueryArgument(invocation, parameter) ||
            IsRequiredTerminalArgument(invocation, parameter) ||
            IsRequiredRawSqlParametersArgument(invocation, parameter) ||
            IsRequiredSqlArgument(invocation, parameter) ||
            IsDbContextSetNameArgument(invocation, parameter);
        if (!isRequired && !IsCancellationTokenParameter(parameter))
            return false;

        var value = argumentValue.UnwrapConversions();
        if (value is ISimpleAssignmentOperation assignment)
        {
            return RequiredArgumentValueIsDefinitelyInvalid(
                invocation,
                parameter,
                assignment.Value,
                executableRoot,
                beforePosition,
                visitedLocals);
        }

        if (value is ILocalReferenceOperation localReference &&
            visitedLocals.Add(localReference.Local) &&
            LocalHasNoUntrackedWritesBefore(
                executableRoot,
                localReference.Local,
                beforePosition))
        {
            var assignments = LocalAssignmentCache.GetAssignments(
                    executableRoot,
                    localReference.Local,
                    default)
                .Where(assignment => assignment.SpanStart < beforePosition)
                .ToArray();
            if (assignments.Length == 1)
            {
                return RequiredArgumentValueIsDefinitelyInvalid(
                    invocation,
                    parameter,
                    assignments[0].Value,
                    executableRoot,
                    assignments[0].SpanStart,
                    visitedLocals);
            }
        }

        if (isRequired && value.ConstantValue is { HasValue: true, Value: null })
            return true;

        if (IsFindKeyValuesArgument(invocation, parameter) &&
            ValueIsDefinitelyEmptyArray(value))
        {
            return true;
        }

        if ((IsRequiredSqlArgument(invocation, parameter) ||
             IsDbContextSetNameArgument(invocation, parameter)) &&
            ValueIsDefinitelyBlankString(value))
        {
            return true;
        }

        return IsCancellationTokenParameter(parameter) &&
               OperationIsDefinitelyCancelledToken(
                   argumentValue,
                   executableRoot,
                   beforePosition,
                   new HashSet<ILocalSymbol>(
                       SymbolEqualityComparer.Default));
    }

    private static bool RequiredArgumentValueIsDirect(IOperation value)
    {
        return value is IParameterReferenceOperation or
            ILocalReferenceOperation or
            IFieldReferenceOperation or
            IPropertyReferenceOperation or
            ILiteralOperation or
            IDefaultValueOperation or
            IArrayCreationOperation or
            IAnonymousFunctionOperation or
            IMethodReferenceOperation or
            IDelegateCreationOperation
            {
                Target: IAnonymousFunctionOperation or IMethodReferenceOperation
            };
    }

    private static bool ValueIsDefinitelyEmptyArray(IOperation value)
    {
        value = value.UnwrapConversions();
        if (value is IArrayCreationOperation arrayCreation)
        {
            if (arrayCreation.Initializer != null)
                return arrayCreation.Initializer.ElementValues.Length == 0;

            return arrayCreation.DimensionSizes.Length == 1 &&
                   arrayCreation.DimensionSizes[0].ConstantValue is
                       { HasValue: true, Value: 0 };
        }

        if (value.Kind.ToString() == "CollectionExpression" &&
            !value.Descendants().Any())
        {
            return true;
        }

        return value is IInvocationOperation arrayEmpty &&
               arrayEmpty.TargetMethod is
               {
                   IsStatic: true,
                   Name: "Empty",
                   Arity: 1,
                   ContainingType.SpecialType: SpecialType.System_Array
               };
    }

    private static bool ValueIsDefinitelyBlankString(IOperation value)
    {
        if (value.ConstantValue is { HasValue: true, Value: string text })
            return text.All(char.IsWhiteSpace);

        return value is IFieldReferenceOperation
               {
                   Field:
                   {
                       IsStatic: true,
                       Name: "Empty",
                       ContainingType.SpecialType: SpecialType.System_String
                   }
               };
    }

    private static bool DbContextSetNameIsDefinitelyValid(
        IInvocationOperation invocation,
        IOperation executableRoot)
    {
        if (!IsDbContextSetInvocation(invocation))
            return true;

        foreach (var argument in invocation.Arguments)
        {
            if (!IsDbContextSetNameArgument(invocation, argument.Parameter))
                continue;

            if (!OperationIsDefinitelyNonEmptySql(
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

    private static bool IsDbContextSetNameArgument(
        IInvocationOperation invocation,
        IParameterSymbol? parameter)
    {
        return parameter != null &&
               parameter.Type.SpecialType == SpecialType.System_String &&
               IsDbContextSetInvocation(invocation);
    }

    private static IOperation? GetSemanticInvocationReceiver(IInvocationOperation invocation)
    {
        if (invocation.Instance != null)
            return invocation.Instance.UnwrapConversions();

        if (invocation.TargetMethod.IsExtensionMethod)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter?.Ordinal == 0)
                    return argument.Value.UnwrapConversions();
            }
        }

        return null;
    }

    private static bool IsTransparentQueryInvocation(IInvocationOperation invocation)
    {
        if (!(invocation.Type.IsIQueryable() || invocation.Type.IsDbSet()))
            return false;

        var containingType = invocation.TargetMethod.ContainingType;
        var containingNamespace = containingType.ContainingNamespace?.ToString();
        return (containingType.Name == "Queryable" && containingNamespace == "System.Linq") ||
               (containingNamespace == "Microsoft.EntityFrameworkCore" &&
                containingType.Name is (
                    "EntityFrameworkQueryableExtensions" or
                    "RelationalQueryableExtensions"));
    }

    private static bool IsStableAutoProperty(IPropertySymbol property)
    {
        foreach (var syntaxReference in property.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax declaration ||
                declaration.ExpressionBody != null ||
                declaration.AccessorList == null)
            {
                continue;
            }

            var hasGetter = false;
            var hasMutableSetter = false;
            foreach (var accessor in declaration.AccessorList.Accessors)
            {
                if (accessor.Body != null || accessor.ExpressionBody != null)
                    return false;

                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                    hasGetter = true;
                else if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
                    hasMutableSetter = true;
            }

            if (hasGetter && !hasMutableSetter)
                return true;
        }

        return false;
    }

    private static bool IsSourceVisibleAutoProperty(IPropertySymbol property)
    {
        foreach (var syntaxReference in property.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax declaration ||
                declaration.ExpressionBody != null ||
                declaration.AccessorList == null)
            {
                continue;
            }

            var hasGetter = false;
            var isAutoProperty = true;
            foreach (var accessor in declaration.AccessorList.Accessors)
            {
                if (accessor.Body != null || accessor.ExpressionBody != null)
                {
                    isAutoProperty = false;
                    break;
                }

                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                    hasGetter = true;
            }

            if (hasGetter && isAutoProperty)
                return true;
        }

        return false;
    }

    private readonly struct ContextOrigin
    {
        public ContextOrigin(ISymbol symbol, string displayName)
        {
            Symbol = symbol;
            ReceiverSymbol = null;
            DisplayName = displayName;
        }

        public ContextOrigin(ISymbol symbol, ISymbol receiverSymbol, string displayName)
        {
            Symbol = symbol;
            ReceiverSymbol = receiverSymbol;
            DisplayName = displayName;
        }

        public ISymbol Symbol { get; }

        public ISymbol? ReceiverSymbol { get; }

        public string DisplayName { get; }
    }

    private sealed class ContextOriginComparer : IEqualityComparer<ContextOrigin>
    {
        public static readonly ContextOriginComparer Instance = new();

        public bool Equals(ContextOrigin x, ContextOrigin y)
        {
            return SymbolEqualityComparer.Default.Equals(x.Symbol, y.Symbol) &&
                   SymbolEqualityComparer.Default.Equals(x.ReceiverSymbol, y.ReceiverSymbol);
        }

        public int GetHashCode(ContextOrigin origin)
        {
            unchecked
            {
                var hashCode = SymbolEqualityComparer.Default.GetHashCode(origin.Symbol);
                return (hashCode * 397) ^
                       (origin.ReceiverSymbol == null
                           ? 0
                           : SymbolEqualityComparer.Default.GetHashCode(origin.ReceiverSymbol));
            }
        }
    }

    private readonly struct EfOperation
    {
        public EfOperation(IInvocationOperation invocation, ContextOrigin origin)
        {
            Invocation = invocation;
            Origin = origin;
        }

        public IInvocationOperation Invocation { get; }

        public ContextOrigin Origin { get; }
    }
}
