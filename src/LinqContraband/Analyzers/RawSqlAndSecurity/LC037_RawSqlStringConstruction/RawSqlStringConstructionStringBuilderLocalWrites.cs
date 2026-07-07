using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool HasOnlyConstantLocalWritesBeforeReference(
        ILocalSymbol local,
        IOperation reference,
        IOperation? executableRoot,
        int depth)
    {
        if (executableRoot == null || depth >= MaxLocalResolutionDepth)
            return false;

        var sawWrite = false;
        var referenceStart = reference.Syntax.SpanStart;
        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                        declarator.Initializer == null ||
                        !IsRelevantWriteForReference(declarator, reference) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        !CanRelevantWriteReachReference(declarator, reference, referenceStart))
                        continue;

                    if (IsStringBuilderAppendArgumentNonConstant(declarator.Initializer.Value, executableRoot, depth + 1))
                        return false;

                    sawWrite = true;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                IsRelevantWriteForReference(assignment, reference) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                CanRelevantWriteReachReference(assignment, reference, referenceStart))
            {
                if (IsStringBuilderAppendArgumentNonConstant(assignment.Value, executableRoot, depth + 1))
                    return false;

                sawWrite = true;
            }

            if (descendant is ICompoundAssignmentOperation compoundAssignment &&
                compoundAssignment.Target.UnwrapConversions() is ILocalReferenceOperation compoundTargetLocal &&
                SymbolEqualityComparer.Default.Equals(compoundTargetLocal.Local, local) &&
                IsRelevantWriteForReference(compoundAssignment, reference) &&
                ReferenceEquals(compoundAssignment.FindOwningExecutableRoot(), executableRoot) &&
                CanRelevantWriteReachReference(compoundAssignment, reference, referenceStart))
            {
                if (IsStringBuilderAppendArgumentNonConstant(GetCompoundAssignmentRightValue(compoundAssignment), executableRoot, depth + 1) ||
                    !HasOnlyConstantLocalWritesBeforeReference(local, compoundAssignment, executableRoot, depth + 1))
                {
                    return false;
                }

                sawWrite = true;
            }
        }

        return sawWrite;
    }

}
