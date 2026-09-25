using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
    /// <summary>
    /// Opts back in to reports on queries whose entities the method returns. Off by default: the caller the
    /// analyzer cannot see usually changes and saves them (repository getters), so "read-only" is a guess.
    /// </summary>
    internal const string ReportReturnedEntitiesOption = "dotnet_code_quality.LC009.report_returned_entities";

    private static bool ReportsReturnedEntities(AnalyzerOptions options, SyntaxTree tree)
    {
        return options.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue(ReportReturnedEntitiesOption, out var value) &&
               string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the member returns or yields the materialized entities to its caller: the materializer itself, its
    /// result local, or either carried there through LINQ to Objects that keeps the entity
    /// (<c>return users.Where(...).ToList();</c>) or as an element of a returned tuple literal
    /// (<c>return (result, user);</c>).
    /// </summary>
    private static bool MaterializedEntitiesAreReturned(
        IInvocationOperation materializer,
        IOperation root,
        HashSet<ILocalSymbol> entityLocals,
        CancellationToken cancellationToken)
    {
        var entityType = materializer.TargetMethod.TypeArguments.Length > 0
            ? materializer.TargetMethod.TypeArguments[0]
            : null;

        if (ReachesReturn(materializer, entityType))
            return true;

        if (entityLocals.Count == 0)
            return false;

        foreach (var descendant in root.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (descendant is ILocalReferenceOperation localReference &&
                entityLocals.Contains(localReference.Local) &&
                ReachesReturn(localReference, entityType))
                return true;
        }

        return false;
    }

    // Task.Run(() => db.Users.ToList()) returns to the lambda's invoker, and a local function to code in this
    // method, so only a return from the member itself hands the entities to an unseen caller.
    private static bool ReturnsFromMember(IReturnOperation returnOperation)
    {
        for (var current = returnOperation.Parent; current != null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return false;
        }

        return true;
    }

    private static bool ReachesReturn(IOperation value, ITypeSymbol? entityType)
    {
        var current = value;
        while (true)
        {
            var parent = WalkUpThroughWrappers(current.Parent);
            switch (parent)
            {
                case IReturnOperation returnOperation:
                    return ReturnsFromMember(returnOperation);
                // return (result, order, id); hands the entity to the caller as one element of a tuple literal,
                // nested or not. A tuple stored in a local first is not followed.
                case ITupleOperation tuple:
                    current = tuple;
                    continue;
                case IArgumentOperation { Parent: IInvocationOperation linq } argument
                    when entityType != null && IsLinqToObjectsSource(linq, argument) && ContainsType(linq.Type, entityType):
                    current = linq;
                    continue;
                default:
                    return false;
            }
        }
    }
}
