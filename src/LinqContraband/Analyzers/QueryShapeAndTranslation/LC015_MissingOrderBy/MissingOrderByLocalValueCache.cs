using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByAnalyzer
{
    private sealed class LocalValueCache
    {
        private readonly object syncRoot = new();
        private readonly Dictionary<IOperation, Dictionary<ILocalSymbol, List<LocalWrite>>> writesByRoot = new();

        public bool TryGetLatestValue(
            IOperation executableRoot,
            ILocalSymbol local,
            int referenceStart,
            CancellationToken cancellationToken,
            out IOperation value)
        {
            value = null!;
            var writes = GetWrites(executableRoot, cancellationToken);
            if (!writes.TryGetValue(local, out var localWrites))
                return false;

            var bestWriteStart = -1;

            foreach (var write in localWrites)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (write.ValueStart <= referenceStart && referenceStart < write.ValueEnd)
                    continue;

                if (write.SpanStart >= referenceStart || write.SpanStart <= bestWriteStart)
                    continue;

                bestWriteStart = write.SpanStart;
                value = write.Value;
            }

            return bestWriteStart >= 0;
        }

        private Dictionary<ILocalSymbol, List<LocalWrite>> GetWrites(
            IOperation executableRoot,
            CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                if (!writesByRoot.TryGetValue(executableRoot, out var writes))
                {
                    writes = BuildWrites(executableRoot, cancellationToken);
                    writesByRoot.Add(executableRoot, writes);
                }

                return writes;
            }
        }

        private static Dictionary<ILocalSymbol, List<LocalWrite>> BuildWrites(
            IOperation executableRoot,
            CancellationToken cancellationToken)
        {
            var writes = new Dictionary<ILocalSymbol, List<LocalWrite>>(SymbolEqualityComparer.Default);

            foreach (var descendant in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (descendant is IVariableDeclarationOperation declaration)
                {
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (declarator.Initializer == null)
                            continue;

                        AddWrite(
                            writes,
                            declarator.Symbol,
                            declarator.Syntax.SpanStart,
                            declarator.Initializer.Value);
                    }
                }

                if (descendant is ISimpleAssignmentOperation assignment &&
                    assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal)
                {
                    AddWrite(
                        writes,
                        targetLocal.Local,
                        assignment.Syntax.SpanStart,
                        assignment.Value);
                }
            }

            return writes;
        }

        private static void AddWrite(
            Dictionary<ILocalSymbol, List<LocalWrite>> writes,
            ILocalSymbol local,
            int spanStart,
            IOperation value)
        {
            if (!writes.TryGetValue(local, out var localWrites))
            {
                localWrites = new List<LocalWrite>();
                writes.Add(local, localWrites);
            }

            localWrites.Add(new LocalWrite(spanStart, value));
        }
    }

    private readonly struct LocalWrite
    {
        public LocalWrite(int spanStart, IOperation value)
        {
            SpanStart = spanStart;
            Value = value;
        }

        public int SpanStart { get; }

        public IOperation Value { get; }

        public int ValueStart => Value.Syntax.SpanStart;

        public int ValueEnd => Value.Syntax.Span.End;
    }

}
