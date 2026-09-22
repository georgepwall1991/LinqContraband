using Microsoft.CodeAnalysis;

namespace LinqContraband.Extensions;

public static partial class AnalysisExtensions
{
    /// <summary>
    /// Gets the semantic model for a declaration's syntax tree when that tree belongs to
    /// <paramref name="compilation"/>. In IDE workspaces a project reference is a compilation
    /// reference, so symbols from a referenced project keep source declarations whose trees
    /// belong to another compilation, and <see cref="Compilation.GetSemanticModel"/> throws for
    /// them. Callers treat those declarations like metadata, exactly as a command-line build
    /// (where project references are assemblies) already does.
    /// </summary>
    public static bool TryGetOwnedSemanticModel(
        this Compilation compilation,
        SyntaxTree syntaxTree,
        out SemanticModel semanticModel)
    {
        if (!compilation.ContainsSyntaxTree(syntaxTree))
        {
            semanticModel = null!;
            return false;
        }

        semanticModel = compilation.GetSemanticModel(syntaxTree);
        return true;
    }
}
