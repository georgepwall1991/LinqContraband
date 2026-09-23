using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization;

public sealed partial class UnboundedQueryMaterializationAnalyzer
{
    private static QuerySourceResolution ResolveQuerySource(IInvocationOperation invocation)
    {
        var foundDbSet = false;
        var foundBounding = false;
        string? dbSetName = null;
        var current = invocation.GetInvocationReceiver();
        var executableRoot = invocation.FindOwningExecutableRoot();
        var visitedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is ITranslatedQueryOperation translatedQuery)
            {
                current = translatedQuery.Operation;
            }
            else if (current is IInvocationOperation prevInvocation)
            {
                var prevMethod = prevInvocation.TargetMethod;
                var receiver = prevInvocation.GetInvocationReceiver();

                if (IsDbContextSetInvocation(prevInvocation))
                {
                    foundDbSet = true;
                    dbSetName = "DbSet";
                    break;
                }

                // "db.Users.ToList().Where(...).ToList()" loads the table at the inner ToList(), which
                // is reported itself. The outer call copies a list that is already in memory.
                if (prevInvocation.IsQueryExecutingMaterializer())
                    break;

                if (IsAggregateMethod(prevMethod.Name) ||
                    IsBoundingMethod(prevMethod.Name) && IsServerSide(receiver) ||
                    IsKeyLookup(prevInvocation))
                {
                    foundBounding = true;
                    break;
                }

                // A project's own IQueryable helper (paging, specifications) may apply the bound itself.
                if (!IsLinqOrEfCoreOperator(prevMethod) && prevInvocation.Type.IsIQueryable())
                    break;

                current = receiver;
            }
            else if (current is ILocalReferenceOperation localReference)
            {
                if (executableRoot == null ||
                    !visitedLocals.Add(localReference.Local) ||
                    !TryResolveSingleAssignedValue(
                        executableRoot,
                        localReference.Local,
                        invocation.Syntax.SpanStart,
                        out var localValue))
                {
                    break;
                }

                current = localValue;
            }
            else if (current is IPropertyReferenceOperation propRef)
            {
                if (propRef.Type.IsDbSet())
                {
                    foundDbSet = true;
                    dbSetName = propRef.Property.Name;
                }

                break;
            }
            else if (current is IFieldReferenceOperation fieldRef)
            {
                if (fieldRef.Type.IsDbSet())
                {
                    foundDbSet = true;
                    dbSetName = fieldRef.Field.Name;
                }

                break;
            }
            else
            {
                if (current.Type?.IsDbSet() == true)
                {
                    foundDbSet = true;
                    dbSetName = current.Type.Name;
                }

                break;
            }
        }

        return new QuerySourceResolution(foundDbSet, foundBounding, dbSetName);
    }

    /// <summary>
    /// A bound only limits the database query while the source is still a query. After <c>AsEnumerable()</c>,
    /// <c>Take</c> trims rows that were already loaded.
    /// </summary>
    private static bool IsServerSide(IOperation? receiver)
    {
        return receiver?.UnwrapConversions().Type.IsIQueryable() == true;
    }

    private static bool IsLinqOrEfCoreOperator(IMethodSymbol method)
    {
        var ns = (method.ReducedFrom ?? method).ContainingType?.ContainingNamespace?.ToDisplayString();
        return ns != null &&
               (ns == "System.Linq" || ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>Where(x =&gt; x.Id == id)</c> or <c>Where(x =&gt; ids.Contains(x.Id))</c> on the entity's primary key:
    /// the rows are bounded by the keys supplied.
    /// </summary>
    private static bool IsKeyLookup(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (method.Name != "Where" ||
            method.ContainingType?.Name != "Queryable" ||
            method.ContainingType.ContainingNamespace?.ToDisplayString() != "System.Linq")
        {
            return false;
        }

        var predicate = invocation.Arguments.Length == 2 ? invocation.Arguments[1].Value.UnwrapConversions() : null;
        if (predicate is IDelegateCreationOperation delegateCreation)
            predicate = delegateCreation.Target;

        if (predicate is not IAnonymousFunctionOperation { Symbol.Parameters.Length: 1 } lambda)
            return false;

        var body = lambda.Body.Operations.Length == 1 && lambda.Body.Operations[0] is IReturnOperation { ReturnedValue: { } returned }
            ? returned
            : null;

        return body != null && ConstrainsKey(body, lambda.Symbol.Parameters[0]);
    }

    private static bool ConstrainsKey(IOperation condition, IParameterSymbol row)
    {
        condition = condition.UnwrapConversions();

        switch (condition)
        {
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } and:
                return ConstrainsKey(and.LeftOperand, row) || ConstrainsKey(and.RightOperand, row);

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } equals:
                return IsKeyOf(equals.LeftOperand, row) && !ReferencesParameter(equals.RightOperand, row) ||
                       IsKeyOf(equals.RightOperand, row) && !ReferencesParameter(equals.LeftOperand, row);

            case IInvocationOperation { TargetMethod.Name: "Contains" } contains:
            {
                var instance = contains.Instance;
                var arguments = contains.Arguments;
                IOperation? keys = instance;
                IOperation? value = arguments.Length == 1 ? arguments[0].Value : null;
                if (instance == null && arguments.Length == 2)
                {
                    keys = arguments[0].Value;
                    value = arguments[1].Value;
                }

                return keys != null && value != null && IsKeyOf(value, row) && !ReferencesParameter(keys, row);
            }

            default:
                return false;
        }
    }

    private static bool IsKeyOf(IOperation operation, IParameterSymbol row)
    {
        if (operation.UnwrapConversions() is not IPropertyReferenceOperation
            {
                Instance: IParameterReferenceOperation parameterReference
            } propertyReference ||
            !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, row))
        {
            return false;
        }

        var property = propertyReference.Property;
        return property.Name == "Id" ||
               property.Name == row.Type.Name + "Id" ||
               property.GetAttributes().Any(attribute =>
                   attribute.AttributeClass is { Name: "KeyAttribute" } keyAttribute &&
                   keyAttribute.ContainingNamespace?.ToDisplayString() == "System.ComponentModel.DataAnnotations");
    }

    private static bool ReferencesParameter(IOperation operation, IParameterSymbol parameter)
    {
        return operation.DescendantsAndSelf().Any(descendant =>
            descendant is IParameterReferenceOperation reference &&
            SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter));
    }

    private static bool IsDbContextSetInvocation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return method.Name == "Set" &&
               method.Parameters.Length == 0 &&
               method.ContainingType.IsDbContext() &&
               invocation.Type.IsDbSet();
    }

    private static bool TryResolveSingleAssignedValue(
        IOperation executableRoot,
        ILocalSymbol local,
        int position,
        out IOperation value)
    {
        return LocalAssignmentCache.TryGetSingleAssignedValueBefore(
            executableRoot,
            local,
            position,
            out value);
    }

    private readonly struct QuerySourceResolution
    {
        public QuerySourceResolution(bool foundDbSet, bool foundBounding, string? dbSetName)
        {
            FoundDbSet = foundDbSet;
            FoundBounding = foundBounding;
            DbSetName = dbSetName;
        }

        public bool FoundDbSet { get; }

        public bool FoundBounding { get; }

        public string? DbSetName { get; }
    }
}
