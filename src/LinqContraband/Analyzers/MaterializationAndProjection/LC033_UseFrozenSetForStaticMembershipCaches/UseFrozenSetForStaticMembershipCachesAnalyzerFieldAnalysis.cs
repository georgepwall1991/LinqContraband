using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

public sealed partial class UseFrozenSetForStaticMembershipCachesAnalyzer
{
    private sealed partial class AnalysisState
    {
        public void AnalyzeField(SymbolAnalysisContext context)
        {
            var field = (IFieldSymbol)context.Symbol;
            if (!IsPotentialCandidate(field))
                return;

            if (!TryGetSingleFieldDeclaration(field, context.CancellationToken, out var fieldDeclaration))
                return;

            var declarator = fieldDeclaration.Declaration.Variables[0];
            if (declarator.Initializer?.Value is not ExpressionSyntax initializerSyntax)
                return;

            var semanticModel = _compilation.GetSemanticModel(initializerSyntax.SyntaxTree);
            if (!UseFrozenSetForStaticMembershipCachesAnalysis.TryClassifyInitializer(
                    initializerSyntax,
                    semanticModel,
                    _support,
                    context.CancellationToken,
                    out _))
            {
                return;
            }

            _candidates.TryAdd(field, new CandidateField(field, fieldDeclaration.GetLocation()));
        }

        private bool IsPotentialCandidate(IFieldSymbol field)
        {
            return field.Locations.Any(static location => location.IsInSource) &&
                   field.DeclaredAccessibility == Accessibility.Private &&
                   field.IsStatic &&
                   field.IsReadOnly &&
                   UseFrozenSetForStaticMembershipCachesAnalysis.IsHashSetType(field.Type, _support.HashSetType);
        }

        private bool TryGetSingleFieldDeclaration(
            IFieldSymbol field,
            System.Threading.CancellationToken cancellationToken,
            out FieldDeclarationSyntax fieldDeclaration)
        {
            fieldDeclaration = null!;

            if (field.DeclaringSyntaxReferences.Length != 1 ||
                field.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declarator ||
                declarator.Parent?.Parent is not FieldDeclarationSyntax declaration)
            {
                return false;
            }

            if (declaration.Declaration.Variables.Count != 1)
                return false;

            fieldDeclaration = declaration;
            return true;
        }
    }
}
