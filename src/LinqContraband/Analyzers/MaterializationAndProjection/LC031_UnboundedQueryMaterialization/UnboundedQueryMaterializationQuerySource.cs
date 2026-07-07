using System.Collections.Generic;
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

                if (IsBoundingMethod(prevMethod.Name) || IsAggregateMethod(prevMethod.Name))
                {
                    foundBounding = true;
                    break;
                }

                if (IsDbContextSetInvocation(prevInvocation))
                {
                    foundDbSet = true;
                    dbSetName = "DbSet";
                    break;
                }

                current = prevInvocation.GetInvocationReceiver();
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
