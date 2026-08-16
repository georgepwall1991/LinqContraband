using Microsoft.CodeAnalysis;

namespace LinqContraband.Extensions;

internal static class SyntaxTreeOwnershipExtensions
{
    public static bool ContainsSyntaxTree(this Compilation compilation, SyntaxTree syntaxTree)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (ReferenceEquals(tree, syntaxTree))
            {
                return true;
            }
        }

        return false;
    }
}
