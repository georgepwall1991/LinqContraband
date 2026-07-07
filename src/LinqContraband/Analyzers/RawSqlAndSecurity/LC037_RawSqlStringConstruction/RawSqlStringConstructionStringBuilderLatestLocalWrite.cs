using System;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool HasLatestNonConstantLocalWriteBeforeReference(
        ILocalSymbol local,
        IOperation reference,
        IOperation? executableRoot,
        int depth)
    {
        if (executableRoot == null || depth >= MaxLocalResolutionDepth)
            return false;

        var referenceStart = reference.Syntax.SpanStart;
        var latestWriteStart = -1;
        var latestWriteIsNonConstant = false;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                        declarator.Initializer == null ||
                        !IsWriteBeforeReference(declarator, referenceStart) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        !CanOperationReachReference(declarator, referenceStart))
                    {
                        continue;
                    }

                    TrackWrite(
                        declarator,
                        IsStringBuilderAppendArgumentNonConstant(declarator.Initializer.Value, executableRoot, depth + 1));
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                IsWriteBeforeReference(assignment, referenceStart) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                CanOperationReachReference(assignment, referenceStart))
            {
                TrackWrite(
                    assignment,
                    IsStringBuilderAppendArgumentNonConstant(assignment.Value, executableRoot, depth + 1));
            }

            if (descendant is ICompoundAssignmentOperation compoundAssignment &&
                compoundAssignment.Target.UnwrapConversions() is ILocalReferenceOperation compoundTargetLocal &&
                SymbolEqualityComparer.Default.Equals(compoundTargetLocal.Local, local) &&
                IsWriteBeforeReference(compoundAssignment, referenceStart) &&
                ReferenceEquals(compoundAssignment.FindOwningExecutableRoot(), executableRoot) &&
                CanOperationReachReference(compoundAssignment, referenceStart))
            {
                TrackWrite(
                    compoundAssignment,
                    IsStringBuilderAppendArgumentNonConstant(GetCompoundAssignmentRightValue(compoundAssignment), executableRoot, depth + 1) ||
                    !HasOnlyConstantLocalWritesBeforeReference(local, compoundAssignment, executableRoot, depth + 1));
            }
        }

        return latestWriteIsNonConstant;

        void TrackWrite(IOperation writeOperation, bool isNonConstant)
        {
            var writeStart = writeOperation.Syntax.SpanStart;
            if (writeStart <= latestWriteStart)
                return;

            latestWriteStart = writeStart;
            latestWriteIsNonConstant = isNonConstant;
        }
    }

    private static IOperation GetCompoundAssignmentRightValue(ICompoundAssignmentOperation compoundAssignment)
    {
        if (compoundAssignment.Syntax is AssignmentExpressionSyntax assignmentSyntax)
        {
            var right = assignmentSyntax.Right;
            var rightOperation = compoundAssignment
                .Descendants()
                .FirstOrDefault(operation => ReferenceEquals(operation.Syntax, right));

            if (rightOperation != null)
                return rightOperation;
        }

        return compoundAssignment.Value;
    }
}
