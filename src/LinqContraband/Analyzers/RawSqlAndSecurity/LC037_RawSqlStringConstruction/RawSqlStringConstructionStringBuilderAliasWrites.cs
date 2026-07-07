using System;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool HasNonGuaranteedWriteAfterLatestGuaranteed(
        ILocalReferenceOperation localReference,
        IOperation executableRoot)
    {
        var referenceStart = localReference.Syntax.SpanStart;
        var latestGuaranteedWriteStart = -1;
        TryResolveGuaranteedLocalValue(localReference.Local, localReference, executableRoot, out _, out _, out latestGuaranteedWriteStart);

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, localReference.Local) ||
                        declarator.Initializer == null ||
                        declarator.Syntax.SpanStart <= latestGuaranteedWriteStart ||
                        !IsWriteBeforeReference(declarator, referenceStart) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        IsGuaranteedBeforeReference(declarator, executableRoot))
                        continue;

                    return true;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, localReference.Local) &&
                assignment.Syntax.SpanStart > latestGuaranteedWriteStart &&
                IsWriteBeforeReference(assignment, referenceStart) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                !IsGuaranteedBeforeReference(assignment, executableRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveGuaranteedLocalValue(
        ILocalSymbol local,
        IOperation reference,
        IOperation executableRoot,
        out IOperation value,
        out int writeEnd,
        out int writeStart)
    {
        value = null!;
        writeEnd = -1;
        writeStart = -1;

        var referenceStart = reference.Syntax.SpanStart;
        var latestWriteStart = -1;
        IOperation? resolvedValue = null;
        var resolvedWriteEnd = -1;

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
                        !IsResetGuaranteedBeforeReference(declarator, executableRoot, referenceStart))
                        continue;

                    TrackWrite(declarator, declarator.Initializer.Value);
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                IsWriteBeforeReference(assignment, referenceStart) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                IsResetGuaranteedBeforeReference(assignment, executableRoot, referenceStart))
            {
                TrackWrite(assignment, assignment.Value);
            }
        }

        if (resolvedValue == null)
            return false;

        value = resolvedValue;
        writeEnd = resolvedWriteEnd;
        writeStart = latestWriteStart;
        return true;

        void TrackWrite(IOperation writeOperation, IOperation writtenValue)
        {
            var writeStart = writeOperation.Syntax.SpanStart;
            if (writeStart <= latestWriteStart)
                return;

            latestWriteStart = writeStart;
            resolvedValue = writtenValue;
            resolvedWriteEnd = writeOperation.Syntax.Span.End;
        }
    }
}
