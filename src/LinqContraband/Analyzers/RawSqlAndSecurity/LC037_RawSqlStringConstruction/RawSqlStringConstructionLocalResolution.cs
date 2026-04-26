using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static bool TryResolveLocalValue(ILocalSymbol local, IOperation reference, IOperation? executableRoot, out IOperation value)
    {
        value = null!;

        if (executableRoot == null)
            return false;

        var referenceStart = reference.Syntax.SpanStart;
        var guaranteedWriteStart = -1;
        IOperation? guaranteedValue = null;
        var ambiguousConstructedWriteStart = -1;
        IOperation? ambiguousConstructedValue = null;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) || declarator.Initializer == null)
                        continue;

                    var writeStart = declarator.Syntax.SpanStart;
                    if (writeStart >= referenceStart)
                        continue;

                    TrackWrite(declarator, declarator.Initializer.Value, writeStart);
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
            {
                var writeStart = assignment.Syntax.SpanStart;
                if (writeStart >= referenceStart)
                    continue;

                TrackWrite(assignment, assignment.Value, writeStart);
            }
        }

        if (ambiguousConstructedValue != null && ambiguousConstructedWriteStart > guaranteedWriteStart)
        {
            value = ambiguousConstructedValue;
            return true;
        }

        if (guaranteedValue == null)
            return false;

        value = guaranteedValue;
        return true;

        void TrackWrite(IOperation writeOperation, IOperation writtenValue, int writeStart)
        {
            if (!ReferenceEquals(writeOperation.FindOwningExecutableRoot(), executableRoot))
                return;

            if (IsGuaranteedBeforeReference(writeOperation, executableRoot))
            {
                if (writeStart <= guaranteedWriteStart)
                    return;

                guaranteedWriteStart = writeStart;
                guaranteedValue = writtenValue;

                if (ambiguousConstructedWriteStart <= guaranteedWriteStart)
                {
                    ambiguousConstructedWriteStart = -1;
                    ambiguousConstructedValue = null;
                }

                return;
            }

            if (writeStart > guaranteedWriteStart &&
                writeStart > ambiguousConstructedWriteStart &&
                IsPotentiallyConstructed(writtenValue))
            {
                ambiguousConstructedWriteStart = writeStart;
                ambiguousConstructedValue = writtenValue;
            }
        }
    }

    private static bool IsGuaranteedBeforeReference(IOperation writeOperation, IOperation executableRoot)
    {
        var current = writeOperation.Parent;
        while (current != null && !ReferenceEquals(current, executableRoot))
        {
            if (current is IConditionalOperation or ISwitchOperation or ILoopOperation or ITryOperation)
                return false;

            current = current.Parent;
        }

        return current != null;
    }

    private static bool IsPotentiallyConstructed(IOperation operation)
    {
        return !operation.UnwrapConversions().ConstantValue.HasValue;
    }
}
