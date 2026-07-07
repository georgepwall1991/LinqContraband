using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

internal readonly struct MutationEntry
{
    public MutationEntry(IOperation operation, Location targetLocation, string propertyName, int spanStart)
    {
        Operation = operation;
        TargetLocation = targetLocation;
        PropertyName = propertyName;
        SpanStart = spanStart;
    }

    public IOperation Operation { get; }
    public Location TargetLocation { get; }
    public string PropertyName { get; }
    public int SpanStart { get; }
}

internal readonly struct ReattachEntry
{
    public ReattachEntry(IOperation operation, ISymbol? contextSymbol, int spanStart, TextSpan span)
    {
        Operation = operation;
        ContextSymbol = contextSymbol;
        SpanStart = spanStart;
        Span = span;
    }

    public IOperation Operation { get; }
    public ISymbol? ContextSymbol { get; }
    public int SpanStart { get; }
    public TextSpan Span { get; }
}

internal readonly struct DetachEntry
{
    public DetachEntry(IOperation operation, ISymbol? contextSymbol, int spanStart, TextSpan span)
    {
        Operation = operation;
        ContextSymbol = contextSymbol;
        SpanStart = spanStart;
        Span = span;
    }

    public IOperation Operation { get; }
    public ISymbol? ContextSymbol { get; }
    public int SpanStart { get; }
    public TextSpan Span { get; }
}

internal readonly struct TrackerClearEntry
{
    public TrackerClearEntry(IOperation operation, ISymbol? contextSymbol, int spanStart)
    {
        Operation = operation;
        ContextSymbol = contextSymbol;
        SpanStart = spanStart;
    }

    public IOperation Operation { get; }
    public ISymbol? ContextSymbol { get; }
    public int SpanStart { get; }
}

internal readonly struct SaveChangesEntry
{
    public SaveChangesEntry(ISymbol? contextSymbol, int spanStart)
    {
        ContextSymbol = contextSymbol;
        SpanStart = spanStart;
    }

    public ISymbol? ContextSymbol { get; }
    public int SpanStart { get; }
}
