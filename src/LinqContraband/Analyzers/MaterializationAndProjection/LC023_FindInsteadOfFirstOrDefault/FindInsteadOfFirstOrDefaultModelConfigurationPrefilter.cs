using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis
{
    private const int PrefilterChunkLength = 4096;

    // Longest needle ("HasQueryFilter") minus one: a match that starts in the last
    // PrefilterChunkOverlap characters of a chunk is read again at the start of the next one.
    private const int PrefilterChunkOverlap = 13;

    private static readonly object MayConfigure = new();
    private static readonly object CannotConfigure = new();

    // Keyed by tree, not by compilation: the IDE reuses the SyntaxTree of every unchanged
    // document in each new compilation snapshot, so after the first scan only edited trees
    // are searched again.
    private static readonly ConditionalWeakTable<SyntaxTree, object> PrefilterResults = new();

    private static bool MayContainModelConfiguration(SyntaxTree tree, CancellationToken cancellationToken)
    {
        if (PrefilterResults.TryGetValue(tree, out var cached))
            return ReferenceEquals(cached, MayConfigure);

        var result = MayContainModelConfiguration(tree.GetText(cancellationToken)) ? MayConfigure : CannotConfigure;
        return ReferenceEquals(PrefilterResults.GetValue(tree, _ => result), MayConfigure);
    }

    /// <summary>
    /// Cheap text check for the EF calls the key scan registers: HasKey, HasNoKey and
    /// HasQueryFilter. A match in a comment or string only costs a semantic scan of that
    /// tree; a tree without any of the names cannot contain one of those invocations.
    /// </summary>
    internal static bool MayContainModelConfiguration(SourceText text)
    {
        var buffer = new char[PrefilterChunkLength + PrefilterChunkOverlap];
        var position = 0;
        while (position < text.Length)
        {
            var count = Math.Min(buffer.Length, text.Length - position);
            text.CopyTo(position, buffer, 0, count);
            if (ChunkContainsModelConfiguration(buffer, count))
                return true;

            if (position + count >= text.Length)
                return false;

            position += count - PrefilterChunkOverlap;
        }

        return false;
    }

    private static bool ChunkContainsModelConfiguration(char[] buffer, int count)
    {
        for (var i = 0; i + 6 <= count; i++)
        {
            if (buffer[i] != 'H' || buffer[i + 1] != 'a' || buffer[i + 2] != 's')
                continue;

            if (Matches(buffer, count, i + 3, "Key") ||
                Matches(buffer, count, i + 3, "NoKey") ||
                Matches(buffer, count, i + 3, "QueryFilter"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(char[] buffer, int count, int start, string value)
    {
        if (start + value.Length > count)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            if (buffer[start + i] != value[i])
                return false;
        }

        return true;
    }
}
