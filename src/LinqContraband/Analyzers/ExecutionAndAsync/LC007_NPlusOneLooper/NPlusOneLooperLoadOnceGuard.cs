using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A load-once guard runs its query at most once however many times the loop goes round:
/// <c>if (x == null) x = query;</c>, <c>if (x is null) { x = query; }</c> or <c>x ??= query;</c>, where <c>x</c> is a
/// local declared before the loop and nothing else writes it inside the loop or from a lambda or local function.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    private static bool IsLoadOnceGuardedInLoop(IInvocationOperation invocation, ILoopOperation loop)
    {
        var statement = FindEnclosingExpressionStatement(invocation);
        if (statement == null || !loop.Syntax.Span.Contains(statement.Syntax.Span))
            return false;

        IAssignmentOperation assignment;
        switch (statement.Operation)
        {
            case ICoalesceAssignmentOperation coalesceAssignment:
                assignment = coalesceAssignment;
                break;
            case ISimpleAssignmentOperation simpleAssignment when IsGuardedByNullCheckOf(statement, simpleAssignment.Target):
                assignment = simpleAssignment;
                break;
            default:
                return false;
        }

        if (assignment.Target is not ILocalReferenceOperation targetReference ||
            !assignment.Value.Syntax.Span.Contains(invocation.Syntax.Span))
        {
            return false;
        }

        var local = targetReference.Local;
        if (local.IsRef ||
            local.DeclaringSyntaxReferences.Length != 1 ||
            loop.Syntax.Span.Contains(local.DeclaringSyntaxReferences[0].Span))
        {
            return false;
        }

        return AssignedValueIsNeverNull(assignment.Value) &&
               !IsInsideTryWithCatchInLoop(statement, loop) &&
               !HasOtherWriteThatCanRearm(local, assignment, loop);
    }

    /// <summary>
    /// The guard only closes if the assignment leaves the local non-null. <c>FirstOrDefault</c>, <c>Find</c> or a
    /// helper that may return null leaves it null when nothing matches, and the query then runs again on the next
    /// iteration.
    /// </summary>
    private static bool AssignedValueIsNeverNull(IOperation value)
    {
        // Step only through what keeps a non-null value non-null: `as` and user-defined conversions can yield null.
        while (true)
        {
            if (value is IConversionOperation { IsTryCast: false, Conversion.IsUserDefined: false } conversion)
                value = conversion.Operand;
            else if (value is IAwaitOperation awaitOperation && IsStandardTaskAwaitable(awaitOperation.Operation.Type))
                value = awaitOperation.Operation;
            else if (value is IInvocationOperation { TargetMethod.Name: "ConfigureAwait", Instance: { } awaited } &&
                     value.Type?.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices")
                value = awaited;
            else
                break;
        }

        if (value is IObjectCreationOperation or IArrayCreationOperation)
            return true;

        if (value is not IInvocationOperation invocation)
            return false;

        var method = invocation.TargetMethod;
        if (method.Name.EndsWith("OrDefault", System.StringComparison.Ordinal) ||
            method.Name.EndsWith("OrDefaultAsync", System.StringComparison.Ordinal) ||
            method.Name is "Find" or "FindAsync")
        {
            return false;
        }

        var resultType = method.ReturnType;
        var annotation = method.ReturnNullableAnnotation;
        if (resultType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } taskType &&
            taskType.Name is "Task" or "ValueTask" &&
            taskType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
        {
            resultType = taskType.TypeArguments[0];
            annotation = taskType.TypeArgumentNullableAnnotations[0];
        }

        if (resultType.IsValueType)
            return resultType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

        if (annotation == NullableAnnotation.Annotated || HasMaybeNullReturn(method))
            return false;

        // A non-nullable reference result is trusted. Without nullable annotations, only LINQ and EF Core
        // executors that return a collection, or an element of an entity set no projection reshaped, are.
        return annotation == NullableAnnotation.NotAnnotated ||
               (IsLinqOrEfCoreMethod(method) &&
                (method.Name.StartsWith("To", System.StringComparison.Ordinal) || IsUnprojectedEntitySetQuery(invocation)));
    }

    private static readonly ImmutableHashSet<string> ElementPreservingOperators = ImmutableHashSet.Create(
        "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Skip", "Take", "SkipWhile",
        "TakeWhile", "SkipLast", "TakeLast", "Distinct", "Reverse", "OfType", "AsQueryable", "AsEnumerable",
        "AsAsyncEnumerable", "AsNoTracking", "AsNoTrackingWithIdentityResolution", "AsTracking", "Include",
        "ThenInclude", "AsSplitQuery", "AsSingleQuery", "IgnoreQueryFilters", "IgnoreAutoIncludes", "TagWith",
        "TagWithCallSite");

    /// <summary>
    /// An entity read from a <c>DbSet</c> is never null, but a projection such as <c>Select(u => u.Manager)</c>
    /// can yield null elements, and so can an in-memory source.
    /// </summary>
    private static bool IsUnprojectedEntitySetQuery(IInvocationOperation executor)
    {
        var source = ExtensionReceiver(executor);
        while (source is IInvocationOperation invocation && IsLinqOrEfCoreMethod(invocation.TargetMethod))
        {
            if (!ElementPreservingOperators.Contains(invocation.TargetMethod.Name))
                return false;

            source = ExtensionReceiver(invocation);
        }

        for (var type = source?.Type; type != null; type = type.BaseType)
        {
            if (type is INamedTypeSymbol { Name: "DbSet", Arity: 1 } dbSet &&
                dbSet.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore")
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation? ExtensionReceiver(IInvocationOperation invocation)
    {
        var receiver = invocation.Instance ??
                       (invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length > 0
                           ? invocation.Arguments[0].Value
                           : null);
        while (receiver is IConversionOperation { Conversion.IsUserDefined: false } conversion)
            receiver = conversion.Operand;
        return receiver;
    }

    /// <summary>
    /// Task, ValueTask and their ConfigureAwait wrappers hand back the task's own result. A custom awaitable's
    /// GetResult can return anything, so it is not stepped through.
    /// </summary>
    private static bool IsStandardTaskAwaitable(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
            return false;

        var ns = named.ContainingNamespace?.ToDisplayString();
        return (ns == "System.Threading.Tasks" && named.Name is "Task" or "ValueTask") ||
               (ns == "System.Runtime.CompilerServices" &&
                named.Name is "ConfiguredTaskAwaitable" or "ConfiguredValueTaskAwaitable");
    }

    private static bool HasMaybeNullReturn(IMethodSymbol method)
    {
        foreach (var attribute in method.GetReturnTypeAttributes())
        {
            if (attribute.AttributeClass is { Name: "MaybeNullAttribute" } attributeClass &&
                attributeClass.ContainingNamespace?.ToDisplayString() == "System.Diagnostics.CodeAnalysis")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLinqOrEfCoreMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToDisplayString();
        return ns is "System.Linq" or "Microsoft.EntityFrameworkCore";
    }

    /// <summary>
    /// A query that throws inside a <c>try</c> whose <c>catch</c> lets the loop go on leaves the local unassigned,
    /// so the next iteration queries again.
    /// </summary>
    private static bool IsInsideTryWithCatchInLoop(IOperation statement, ILoopOperation loop)
    {
        IOperation child = statement;
        for (var current = statement.Parent; current != null && !ReferenceEquals(current, loop); child = current, current = current.Parent)
        {
            if (current is ITryOperation tryOperation &&
                ReferenceEquals(tryOperation.Body, child) &&
                !tryOperation.Catches.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private static IExpressionStatementOperation? FindEnclosingExpressionStatement(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case IExpressionStatementOperation statement:
                    return statement;
                case IAnonymousFunctionOperation or ILocalFunctionOperation or IMethodBodyBaseOperation:
                    return null;
            }
        }

        return null;
    }

    private static bool IsGuardedByNullCheckOf(IExpressionStatementOperation statement, IOperation target)
    {
        if (target is not ILocalReferenceOperation targetReference)
            return false;

        IOperation guarded = statement;
        if (statement.Parent is IBlockOperation block && block.Parent is IConditionalOperation)
            guarded = block;

        return guarded.Parent is IConditionalOperation conditional &&
               ReferenceEquals(conditional.WhenTrue, guarded) &&
               ConditionImpliesNull(conditional.Condition, targetReference.Local);
    }

    private static bool ConditionImpliesNull(IOperation condition, ILocalSymbol local)
    {
        // A user-defined conversion can turn a false comparison into true, so only built-in conversions are peeled.
        while (condition is IConversionOperation { Conversion.IsUserDefined: false } conversion)
            condition = conversion.Operand;

        switch (condition)
        {
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd, OperatorMethod: null } conjunction:
                return ConditionImpliesNull(conjunction.LeftOperand, local) ||
                       ConditionImpliesNull(conjunction.RightOperand, local);
            // A user-defined == can answer true for a non-null value, so only the built-in comparison proves null.
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals, OperatorMethod: null } equals:
                return (IsLocalRead(equals.LeftOperand, local) && IsNullConstant(equals.RightOperand)) ||
                       (IsLocalRead(equals.RightOperand, local) && IsNullConstant(equals.LeftOperand));
            case IIsPatternOperation { Pattern: IConstantPatternOperation constantPattern } isPattern:
                return IsLocalRead(isPattern.Value, local) && IsNullConstant(constantPattern.Value);
            default:
                return false;
        }
    }

    private static bool IsLocalRead(IOperation operation, ILocalSymbol local)
    {
        // A user-defined conversion can map a loaded value to null, so only built-in conversions are peeled.
        while (operation is IConversionOperation { Conversion.IsUserDefined: false } conversion)
            operation = conversion.Operand;

        return operation is ILocalReferenceOperation reference &&
               SymbolEqualityComparer.Default.Equals(reference.Local, local);
    }

    private static bool IsNullConstant(IOperation operation)
    {
        var constant = operation.UnwrapConversions().ConstantValue;
        return constant.HasValue && constant.Value == null;
    }

    /// <summary>
    /// Any other write to the guarded local inside the loop, or from a lambda or local function anywhere in the
    /// method, could reset it between iterations. Writes before the loop in the same body are fine.
    /// </summary>
    private static bool HasOtherWriteThatCanRearm(ILocalSymbol local, IAssignmentOperation guardedAssignment, ILoopOperation loop)
    {
        var root = loop.FindOwningExecutableRoot();
        if (root == null)
            return true;

        foreach (var reference in root.Descendants().OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Local, local) ||
                ReferenceEquals(reference, guardedAssignment.Target))
            {
                continue;
            }

            // A ref alias can write the local under another name, wherever the alias is taken.
            if (IsRefAlias(reference))
                return true;

            if (!IsWriteReference(reference))
                continue;

            if (loop.Syntax.Span.Contains(reference.Syntax.Span) ||
                !ReferenceEquals(reference.FindOwningExecutableRoot(), root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>ref var alias = ref x;</c>, <c>alias = ref x;</c> or <c>return ref x;</c> from a local function or lambda.
    /// </summary>
    private static bool IsRefAlias(ILocalReferenceOperation reference)
    {
        // `ref (flag ? ref x : ref y)` passes the reference through a conditional ref expression.
        IOperation value = reference;
        while (value.Parent is IConditionalOperation { IsRef: true } conditional &&
               !ReferenceEquals(conditional.Condition, value))
        {
            value = conditional;
        }

        return value.Parent switch
        {
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.IsRef: true } } => true,
            ISimpleAssignmentOperation { IsRef: true } refAssignment => ReferenceEquals(refAssignment.Value, value),
            IReturnOperation => reference.FindOwningExecutableRoot() is { } owner && ReturnsByRef(owner),
            // The callee can return the reference by ref or keep it, so the alias can outlive the call.
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } => true,
            _ => false
        };
    }

    private static bool ReturnsByRef(IOperation root)
    {
        return root switch
        {
            ILocalFunctionOperation localFunction => localFunction.Symbol.ReturnsByRef,
            IAnonymousFunctionOperation lambda => lambda.Symbol.ReturnsByRef,
            _ => false
        };
    }

    private static bool IsWriteReference(ILocalReferenceOperation reference)
    {
        if (reference.IsDeclaration)
            return false;

        var parent = reference.Parent;
        return parent switch
        {
            IAssignmentOperation assignment => ReferenceEquals(assignment.Target, reference),
            IIncrementOrDecrementOperation => true,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            ITupleOperation or IDeclarationExpressionOperation => true,
            _ => false
        };
    }
}
