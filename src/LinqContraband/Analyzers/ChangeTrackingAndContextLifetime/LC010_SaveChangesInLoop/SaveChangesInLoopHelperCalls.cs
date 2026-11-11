using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Analyzers.LC007_NPlusOneLooper;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// A repository whose <c>Add</c> saves, or a unit of work whose <c>Commit</c> runs per item, hides the save in a
/// helper. LC010 summarizes same-compilation helpers with LC007's helper machinery (three calls deep, interface
/// and virtual calls when every implementation saves) and reports the per-item call.
/// </summary>
public sealed partial class SaveChangesInLoopAnalyzer
{
    private static void AnalyzeHelperCall(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        NPlusOneLooperHelperCache saveHelpers)
    {
        // Cheap gates first: this runs for every invocation in the compilation.
        IAnonymousFunctionOperation? lambda = null;
        if (invocation.FindEnclosingLoop() == null &&
            NPlusOneLooperAnalysis.FindPerElementOperator(invocation, out lambda) == null)
        {
            return;
        }

        var saveMethodName = NPlusOneLooperAnalysis.GetExecutionThroughHelpers(
            invocation,
            saveHelpers,
            context.CancellationToken);
        if (saveMethodName.Length == 0)
            return;

        var loop = FindSaveExecutionLoop(invocation);
        while (loop != null &&
               (loop.RunsAtMostOnce() ||
                invocation.LeavesLoopAfter(loop) ||
                NPlusOneLooperAnalysis.IsBatchLoop(loop, context.CancellationToken)))
            loop = FindEnclosingLoopOutsideSameRoot(invocation, loop);

        if (loop == null && lambda == null)
            NPlusOneLooperAnalysis.FindPerElementOperator(invocation, out lambda);

        var perItemScope = loop?.Syntax ?? lambda?.Syntax;
        if (perItemScope == null || UsesPerItemState(invocation, perItemScope, lambda))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                HelperCallRule,
                invocation.Syntax.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(HelperCallProperty, "true"),
                invocation.TargetMethod.Name,
                saveMethodName));
    }

    /// <summary>
    /// A save on a context the helper declares is the helper's own unit of work; one on a field, property,
    /// parameter or <c>this</c> writes the caller's.
    /// </summary>
    private static string? MatchSaveInHelper(IInvocationOperation call, CancellationToken cancellationToken)
    {
        var method = call.TargetMethod;
        if ((method.Name != "SaveChanges" && method.Name != "SaveChangesAsync") ||
            !method.ContainingType.IsDbContext() ||
            call.Instance?.UnwrapConversions() is ILocalReferenceOperation)
        {
            return null;
        }

        return method.Name;
    }

    /// <summary>
    /// The call works on something that exists once per item, so each item has its own unit of work: the receiver
    /// is created in place, is a local or iteration variable of the loop or lambda, or is the lambda's parameter,
    /// or a context declared per item is passed in.
    /// </summary>
    private static bool UsesPerItemState(
        IInvocationOperation invocation,
        SyntaxNode perItemScope,
        IAnonymousFunctionOperation? lambda)
    {
        var receiver = invocation.GetInvocationReceiver()?.UnwrapConversions();
        if (receiver is IObjectCreationOperation)
            return true;

        if (receiver != null)
        {
            foreach (var operation in receiver.DescendantsAndSelf())
            {
                if (IsPerItemReference(operation, perItemScope, lambda))
                    return true;
            }
        }

        foreach (var argument in invocation.Arguments)
        {
            var value = argument.Value.UnwrapConversions();
            if (value.Type.IsDbContext() &&
                (value is IObjectCreationOperation || IsPerItemReference(value, perItemScope, lambda)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPerItemReference(
        IOperation operation,
        SyntaxNode perItemScope,
        IAnonymousFunctionOperation? lambda)
    {
        switch (operation)
        {
            case IParameterReferenceOperation parameterReference:
                return lambda != null &&
                       SymbolEqualityComparer.Default.Equals(parameterReference.Parameter.ContainingSymbol, lambda.Symbol);
            case ILocalReferenceOperation localReference:
                foreach (var reference in localReference.Local.DeclaringSyntaxReferences)
                {
                    if (reference.SyntaxTree == perItemScope.SyntaxTree && perItemScope.Span.Contains(reference.Span))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }
}
