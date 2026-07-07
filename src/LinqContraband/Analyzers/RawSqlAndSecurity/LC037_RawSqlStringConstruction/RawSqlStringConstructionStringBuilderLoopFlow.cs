using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool HasNonConstantLoopCarriedLocalWrite(
        ILocalSymbol local,
        IOperation reference,
        IOperation? executableRoot,
        int depth)
    {
        if (executableRoot == null || depth >= MaxLocalResolutionDepth)
            return false;

        if (HasGuaranteedConstantLocalWriteInSameIterationBeforeReference(local, reference, executableRoot, depth + 1) &&
            !HasLatestNonConstantLocalWriteBeforeReference(local, reference, executableRoot, depth + 1))
        {
            return false;
        }

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ISimpleAssignmentOperation assignment ||
                assignment.Target.UnwrapConversions() is not ILocalReferenceOperation targetLocal ||
                !SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) ||
                !ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) ||
                !IsLoopCarriedWriteForReference(assignment, reference) ||
                !CanWriteReachLaterLoopIteration(assignment, reference))
            {
                continue;
            }

            if (IsStringBuilderAppendArgumentNonConstant(assignment.Value, executableRoot, depth + 1))
                return true;
        }

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ICompoundAssignmentOperation compoundAssignment ||
                compoundAssignment.Target.UnwrapConversions() is not ILocalReferenceOperation targetLocal ||
                !SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) ||
                !ReferenceEquals(compoundAssignment.FindOwningExecutableRoot(), executableRoot) ||
                !IsLoopCarriedWriteForReference(compoundAssignment, reference) ||
                !CanWriteReachLaterLoopIteration(compoundAssignment, reference))
            {
                continue;
            }

            if (IsStringBuilderAppendArgumentNonConstant(GetCompoundAssignmentRightValue(compoundAssignment), executableRoot, depth + 1) ||
                !HasOnlyConstantLocalWritesBeforeReference(local, compoundAssignment, executableRoot, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGuaranteedConstantLocalWriteInSameIterationBeforeReference(
        ILocalSymbol local,
        IOperation reference,
        IOperation executableRoot,
        int depth)
    {
        foreach (var loop in reference.Syntax.Ancestors().Where(IsLoopSyntax))
        {
            foreach (var descendant in executableRoot.Descendants())
            {
                if (descendant is IVariableDeclarationOperation declaration)
                {
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                            declarator.Initializer == null ||
                            !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                            !IsWriteBeforeReference(declarator, reference.Syntax.SpanStart) ||
                            !ContainsNode(loop, declarator.Syntax) ||
                            IsStringBuilderAppendArgumentNonConstant(declarator.Initializer.Value, executableRoot, depth + 1))
                        {
                            continue;
                        }

                        if (IsEarlierStatementInSameBlock(declarator.Syntax, reference.Syntax))
                            return true;
                    }
                }

                if (descendant is ISimpleAssignmentOperation assignment &&
                    assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                    SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                    ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                    IsWriteBeforeReference(assignment, reference.Syntax.SpanStart) &&
                    ContainsNode(loop, assignment.Syntax) &&
                    !IsStringBuilderAppendArgumentNonConstant(assignment.Value, executableRoot, depth + 1) &&
                    IsEarlierStatementInSameBlock(assignment.Syntax, reference.Syntax))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
