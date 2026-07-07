using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

public sealed partial class MissingOrderByAnalyzer
{
    private readonly struct LocalReferenceKey
    {
        public LocalReferenceKey(ILocalSymbol local, int spanStart)
        {
            Local = local;
            SpanStart = spanStart;
        }

        public ILocalSymbol Local { get; }

        public int SpanStart { get; }
    }

    private sealed class LocalReferenceKeyComparer : IEqualityComparer<LocalReferenceKey>
    {
        public static readonly LocalReferenceKeyComparer Instance = new();

        public bool Equals(LocalReferenceKey x, LocalReferenceKey y)
        {
            return x.SpanStart == y.SpanStart &&
                   SymbolEqualityComparer.Default.Equals(x.Local, y.Local);
        }

        public int GetHashCode(LocalReferenceKey obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj.Local) ^ obj.SpanStart;
        }
    }
}
