using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC024_GroupByNonTranslatable;

public sealed partial class GroupByNonTranslatableAnalyzer
{
    private static void CheckOperationForNonTranslatableAccess(
        IOperation operation,
        IParameterSymbol groupParam,
        OperationAnalysisContext context)
    {
        foreach (var invocation in GetAllOperations(operation).OfType<IInvocationOperation>())
        {
            if (IsTranslatableGroupAccess(invocation, groupParam))
                continue;

            if (!invocation.ReferencesParameter(groupParam))
                continue;

            ReportNonTranslatableAccess(context, invocation.Syntax, invocation.TargetMethod.Name);
            return;
        }

        foreach (var descendant in GetAllOperations(operation))
        {
            if (descendant is IParameterReferenceOperation paramRef &&
                SymbolEqualityComparer.Default.Equals(paramRef.Parameter, groupParam))
            {
                var usage = paramRef.Parent;

                while (usage is IConversionOperation)
                    usage = usage.Parent;

                if (usage is IPropertyReferenceOperation propRef && propRef.Property.Name == "Key")
                    continue;

                if (usage is IArgumentOperation argOp && argOp.Parent is IInvocationOperation aggInvocation)
                {
                    if (IsTranslatableGroupAccess(aggInvocation, groupParam))
                        continue;

                    ReportNonTranslatableAccess(context, aggInvocation.Syntax, aggInvocation.TargetMethod.Name);
                    return;
                }

                if (usage is IInvocationOperation directInvocation)
                {
                    if (IsTranslatableGroupAccess(directInvocation, groupParam))
                        continue;

                    ReportNonTranslatableAccess(context, directInvocation.Syntax, directInvocation.TargetMethod.Name);
                    return;
                }

                ReportNonTranslatableAccess(context, paramRef.Syntax, "direct access");
                return;
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<IOperation> GetAllOperations(IOperation root)
    {
        yield return root;
        foreach (var child in root.ChildOperations)
        {
            foreach (var descendant in GetAllOperations(child))
                yield return descendant;
        }
    }

    private static void ReportNonTranslatableAccess(OperationAnalysisContext context, SyntaxNode syntax, string accessName)
    {
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, syntax.GetLocation(), accessName));
    }
}
