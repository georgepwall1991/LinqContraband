using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC043_AsyncEnumerableBuffering;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncEnumerableBufferingFixer))]
[Shared]
public sealed class AsyncEnumerableBufferingFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsyncEnumerableBufferingAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation == null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        if (!TryGetFixInfo(invocation, semanticModel, out var fixInfo))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use await foreach",
                c => ApplyFixAsync(context.Document, fixInfo, c),
                "UseAwaitForeach"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, FixInfo fixInfo, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var awaitForeach = fixInfo.LoopSyntax
            .WithAwaitKeyword(SyntaxFactory.Token(SyntaxKind.AwaitKeyword))
            .WithExpression(fixInfo.SourceExpression)
            .WithTriviaFrom(fixInfo.LoopSyntax);

        editor.ReplaceNode(fixInfo.LoopSyntax, awaitForeach);
        editor.RemoveNode(fixInfo.LocalDeclaration, SyntaxRemoveOptions.KeepNoTrivia);
        return editor.GetChangedDocument();
    }

    private static bool TryGetFixInfo(InvocationExpressionSyntax invocation, SemanticModel semanticModel, out FixInfo fixInfo)
    {
        fixInfo = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax)
            return false;

        if (!TryGetContainingLoopAndDeclaration(invocation, out var localDeclaration, out var loopSyntax))
            return false;

        if (semanticModel.GetOperation(loopSyntax) is not IForEachLoopOperation loopOperation)
            return false;

        var collection = loopOperation.Collection.UnwrapConversions();
        if (collection is not ILocalReferenceOperation localReference)
            return false;

        var declarator = localDeclaration.Declaration.Variables.FirstOrDefault();
        if (declarator == null)
            return false;

        if (semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol declaredLocal ||
            !SymbolEqualityComparer.Default.Equals(declaredLocal, localReference.Local))
        {
            return false;
        }

        if (!AsyncEnumerableBufferingAnalyzer.TryGetImmediateBufferedLocal(loopSyntax, localReference.Local, out var bufferInfo))
            return false;

        if (!ReferenceEquals(bufferInfo.BufferInvocation, invocation))
            return false;

        fixInfo = new FixInfo(bufferInfo.LocalDeclaration, bufferInfo.LoopSyntax, bufferInfo.SourceExpression);
        return true;
    }

    private static bool TryGetContainingLoopAndDeclaration(
        InvocationExpressionSyntax invocation,
        out LocalDeclarationStatementSyntax localDeclaration,
        out ForEachStatementSyntax loopSyntax)
    {
        localDeclaration = null!;
        loopSyntax = null!;

        if (invocation.AncestorsAndSelf().OfType<AwaitExpressionSyntax>().FirstOrDefault() is not AwaitExpressionSyntax awaitExpression)
            return false;

        if (awaitExpression.Parent is not EqualsValueClauseSyntax equalsValueClause)
            return false;

        if (equalsValueClause.Parent is not VariableDeclaratorSyntax declarator)
            return false;

        if (declarator.Parent?.Parent is not LocalDeclarationStatementSyntax declaration)
            return false;

        if (declarator.Parent?.Parent?.Parent is not BlockSyntax block)
            return false;

        var statements = block.Statements;
        var declarationIndex = -1;
        for (var i = 0; i < statements.Count; i++)
        {
            if (ReferenceEquals(statements[i], declaration))
            {
                declarationIndex = i;
                break;
            }
        }

        if (declarationIndex < 0 || declarationIndex + 1 >= statements.Count)
            return false;

        if (statements[declarationIndex + 1] is not ForEachStatementSyntax loop)
            return false;

        localDeclaration = declaration;
        loopSyntax = loop;
        return true;
    }

    private sealed class FixInfo
    {
        public FixInfo(
            LocalDeclarationStatementSyntax localDeclaration,
            ForEachStatementSyntax loopSyntax,
            ExpressionSyntax sourceExpression)
        {
            LocalDeclaration = localDeclaration;
            LoopSyntax = loopSyntax;
            SourceExpression = sourceExpression;
        }

        public LocalDeclarationStatementSyntax LocalDeclaration { get; }

        public ForEachStatementSyntax LoopSyntax { get; }

        public ExpressionSyntax SourceExpression { get; }
    }
}
