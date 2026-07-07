using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using LinqContraband.Extensions;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

/// <summary>
/// Provides code fixes for LC016. Extracts DateTime.Now to a local variable.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidDateTimeNowFixer))]
[Shared]
public sealed partial class AvoidDateTimeNowFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidDateTimeNowAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var memberAccess = token.Parent.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
        if (memberAccess == null) return;
        if (!CanApplyFix(memberAccess)) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Extract to local variable",
                c => ApplyFixAsync(context.Document, memberAccess, c),
                "ExtractToLocal"),
            diagnostic);
    }

    private static bool CanApplyFix(MemberAccessExpressionSyntax memberAccess)
    {
        if (IsInsideStaticLambda(memberAccess))
            return false;

        var expressionBody = memberAccess.AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (expressionBody?.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
            return true;

        return memberAccess.AncestorsAndSelf().OfType<StatementSyntax>().Any();
    }

    private async Task<Document> ApplyFixAsync(Document document, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var expressionBody = memberAccess.AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (expressionBody?.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
        {
            return ConvertExpressionBodiedMember(document, editor, memberAccess, semanticModel, cancellationToken);
        }

        // Find the statement containing the expression.
        var statement = memberAccess.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement == null)
        {
            return ConvertExpressionBodiedMember(document, editor, memberAccess, semanticModel, cancellationToken);
        }

        var variableName = GetUniqueVariableName(memberAccess);
        var newVariable = CreateLocalDeclaration(memberAccess, variableName)
            .WithTrailingTrivia(statement.GetDocumentEndOfLine());

        foreach (var access in FindMatchingClockAccesses(memberAccess, semanticModel, cancellationToken))
        {
            editor.ReplaceNode(access, SyntaxFactory.IdentifierName(variableName).WithTriviaFrom(access));
        }

        // Insert the declaration before the statement
        editor.InsertBefore(statement, newVariable);

        return editor.GetChangedDocument();
    }

    private static LocalDeclarationStatementSyntax CreateLocalDeclaration(
        MemberAccessExpressionSyntax memberAccess,
        string variableName) =>
        SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(
                        SyntaxFactory.Identifier(variableName),
                        null,
                        SyntaxFactory.EqualsValueClause(memberAccess.WithoutTrivia())
                    )
                )
            )
        );

    private static IEnumerable<MemberAccessExpressionSyntax> FindMatchingClockAccesses(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
        if (symbol == null)
        {
            yield return memberAccess;
            yield break;
        }

        var lambda = memberAccess.AncestorsAndSelf().OfType<LambdaExpressionSyntax>().FirstOrDefault();
        var searchRoot = (SyntaxNode?)lambda ?? memberAccess;

        foreach (var candidate in searchRoot.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var candidateSymbol = semanticModel.GetSymbolInfo(candidate, cancellationToken).Symbol;
            if (SymbolEqualityComparer.Default.Equals(candidateSymbol, symbol))
                yield return candidate;
        }
    }

}
