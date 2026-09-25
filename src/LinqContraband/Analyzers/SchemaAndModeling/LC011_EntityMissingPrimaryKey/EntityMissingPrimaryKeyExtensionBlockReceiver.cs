using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    // C# 14 extension blocks, as in Duende IdentityServer:
    //   extension(ModelBuilder modelBuilder) { public void ConfigurePersistedGrantContext(...) { ... } }
    // The receiver is declared on the block, not on the method. The analyzer builds against
    // Roslyn 4.3, which has no symbol API for it, so it is read from the block's parameter list:
    // newer compilers parse the block as a type declaration whose keyword is `extension`.
    private static bool TryGetExtensionBlockReceiver(
        IMethodSymbol method,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        out IParameterSymbol receiver)
    {
        receiver = null!;
        if (method.IsStatic)
            return false;

        foreach (var syntaxRef in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (syntaxRef.GetSyntax(cancellationToken).Parent is not TypeDeclarationSyntax block ||
                block.Keyword.ValueText != "extension")
            {
                continue;
            }

            var parameterList = block.ChildNodes().OfType<ParameterListSyntax>().FirstOrDefault();
            if (parameterList is not { Parameters.Count: 1 })
                continue;

            var semanticModel = compilationModel.GetSemanticModel(block.SyntaxTree);
            if (semanticModel?.GetDeclaredSymbol(parameterList.Parameters[0], cancellationToken) is IParameterSymbol parameter)
            {
                receiver = parameter;
                return true;
            }
        }

        return false;
    }
}
