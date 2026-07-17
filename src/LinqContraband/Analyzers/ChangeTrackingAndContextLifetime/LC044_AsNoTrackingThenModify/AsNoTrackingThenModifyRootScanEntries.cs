using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

internal readonly struct MemberPathSegment
{
    public MemberPathSegment(
        ISymbol member,
        ITypeSymbol? receiverType,
        bool isIndexer,
        string? indexKey)
    {
        Member = member;
        ReceiverType = receiverType;
        IsIndexer = isIndexer;
        IndexKey = indexKey;
    }

    public ISymbol Member { get; }
    public ITypeSymbol? ReceiverType { get; }
    public bool IsIndexer { get; }
    public string? IndexKey { get; }
}

internal readonly struct MutationEntry
{
    public MutationEntry(
        IOperation operation,
        Location targetLocation,
        string propertyName,
        ImmutableArray<MemberPathSegment> receiverPath,
        int spanStart)
    {
        Operation = operation;
        TargetLocation = targetLocation;
        PropertyName = propertyName;
        ReceiverPath = receiverPath;
        SpanStart = spanStart;
    }

    public IOperation Operation { get; }
    public Location TargetLocation { get; }
    public string PropertyName { get; }
    public ImmutableArray<MemberPathSegment> ReceiverPath { get; }
    public int SpanStart { get; }
}

internal readonly struct ReattachEntry
{
    public ReattachEntry(
        IOperation operation,
        ISymbol? contextSymbol,
        ImmutableArray<MemberPathSegment> targetPath,
        bool persistsExistingMutation,
        bool coversDescendantPaths,
        int spanStart,
        TextSpan span)
    {
        Operation = operation;
        ContextSymbol = contextSymbol;
        TargetPath = targetPath;
        PersistsExistingMutation = persistsExistingMutation;
        CoversDescendantPaths = coversDescendantPaths;
        SpanStart = spanStart;
        Span = span;
    }

    public IOperation Operation { get; }
    public ISymbol? ContextSymbol { get; }
    public ImmutableArray<MemberPathSegment> TargetPath { get; }
    public bool PersistsExistingMutation { get; }
    public bool CoversDescendantPaths { get; }
    public int SpanStart { get; }
    public TextSpan Span { get; }
}

internal readonly struct DetachEntry
{
    public DetachEntry(
        IOperation operation,
        ISymbol? contextSymbol,
        ImmutableArray<MemberPathSegment> targetPath,
        int spanStart,
        TextSpan span)
    {
        Operation = operation;
        ContextSymbol = contextSymbol;
        TargetPath = targetPath;
        SpanStart = spanStart;
        Span = span;
    }

    public IOperation Operation { get; }
    public ISymbol? ContextSymbol { get; }
    public ImmutableArray<MemberPathSegment> TargetPath { get; }
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
    public SaveChangesEntry(IOperation operation, ISymbol? contextSymbol, int spanStart)
    {
        Operation = operation;
        ContextSymbol = contextSymbol;
        SpanStart = spanStart;
    }

    public IOperation Operation { get; }
    public ISymbol? ContextSymbol { get; }
    public int SpanStart { get; }
}
