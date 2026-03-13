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
using Microsoft.CodeAnalysis.Formatting;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

/// <summary>
/// Provides safe LC002 code fixes only for analyzer-proven inline rewrites.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PrematureMaterializationFixer))]
[Shared]
public sealed class PrematureMaterializationFixer : CodeFixProvider
{
    private static readonly ImmutableHashSet<string> SequenceContinuationMethods = ImmutableHashSet.Create(
        "Where",
        "Select",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take"
    );

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PrematureMaterializationAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (!diagnostic.Properties.TryGetValue(PrematureMaterializationAnalyzer.FixKindKey, out var fixKind))
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var invocation = root?
            .FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation == null) return;

        if (fixKind == PrematureMaterializationAnalyzer.MoveBeforeMaterializationFixKind &&
            IsInlineMaterializerReceiver(invocation))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Move query operator before materialization",
                    c => MoveBeforeMaterializationAsync(context.Document, invocation, diagnostic, c),
                    PrematureMaterializationAnalyzer.MoveBeforeMaterializationFixKind),
                diagnostic);
            return;
        }

        if (fixKind == PrematureMaterializationAnalyzer.RemoveRedundantMaterializationFixKind &&
            IsInlineMaterializerReceiver(invocation))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove redundant materialization",
                    c => RemoveRedundantMaterializationAsync(context.Document, invocation, c),
                    PrematureMaterializationAnalyzer.RemoveRedundantMaterializationFixKind),
                diagnostic);
        }
    }

    private static async Task<Document> MoveBeforeMaterializationAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        if (!TryGetInlineMaterializerParts(invocation, out var currentMemberAccess, out var materializerInvocation, out var materializerMemberAccess))
        {
            return document;
        }

        var materializerSource = materializerMemberAccess.Expression.WithoutTrivia();
        var rewrittenCurrentInvocation = invocation.WithExpression(
                currentMemberAccess.WithExpression(materializerSource))
            .WithTriviaFrom(invocation)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var currentMethodName = diagnostic.Properties.TryGetValue(PrematureMaterializationAnalyzer.CurrentMethodKey, out var methodName) &&
                                methodName != null
            ? methodName
            : currentMemberAccess.Name.Identifier.Text;

        SyntaxNode replacement = rewrittenCurrentInvocation;

        if (SequenceContinuationMethods.Contains(currentMethodName))
        {
            if (!IsInsideOuterMaterialization(invocation, semanticModel, cancellationToken))
            {
                replacement = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            rewrittenCurrentInvocation.WithoutTrivia(),
                            materializerMemberAccess.Name.WithoutTrivia()))
                    .WithArgumentList(materializerInvocation.ArgumentList)
                    .WithTriviaFrom(invocation)
                    .WithAdditionalAnnotations(Formatter.Annotation);
            }
        }

        editor.ReplaceNode(invocation, replacement);
        return editor.GetChangedDocument();
    }

    private static async Task<Document> RemoveRedundantMaterializationAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        if (!TryGetInlineMaterializerParts(invocation, out var currentMemberAccess, out var previousInvocation, out var previousMemberAccess))
        {
            return document;
        }

        SyntaxNode replacement;
        var currentMaterializer = currentMemberAccess.Name.Identifier.Text;

        if (currentMaterializer == "AsEnumerable")
        {
            replacement = previousInvocation
                .WithTriviaFrom(invocation)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }
        else
        {
            replacement = invocation.WithExpression(
                    currentMemberAccess.WithExpression(previousMemberAccess.Expression.WithoutTrivia()))
                .WithTriviaFrom(invocation)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        editor.ReplaceNode(invocation, replacement);
        return editor.GetChangedDocument();
    }

    private static bool IsInlineMaterializerReceiver(InvocationExpressionSyntax invocation)
    {
        return TryGetInlineMaterializerParts(invocation, out _, out _, out _);
    }

    private static bool TryGetInlineMaterializerParts(
        InvocationExpressionSyntax invocation,
        out MemberAccessExpressionSyntax currentMemberAccess,
        out InvocationExpressionSyntax previousInvocation,
        out MemberAccessExpressionSyntax previousMemberAccess)
    {
        currentMemberAccess = null!;
        previousInvocation = null!;
        previousMemberAccess = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax currentAccess) return false;
        if (currentAccess.Expression is not InvocationExpressionSyntax previousCall) return false;
        if (previousCall.Expression is not MemberAccessExpressionSyntax previousAccess) return false;

        currentMemberAccess = currentAccess;
        previousInvocation = previousCall;
        previousMemberAccess = previousAccess;
        return true;
    }

    private static bool IsInsideOuterMaterialization(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Parent is MemberAccessExpressionSyntax parentMemberAccess &&
            parentMemberAccess.Parent is InvocationExpressionSyntax parentInvocation)
        {
            var parentSymbol = semanticModel.GetSymbolInfo(parentInvocation, cancellationToken).Symbol as IMethodSymbol;
            return parentSymbol != null && IsMaterializingMethod(parentSymbol.Name);
        }

        if (invocation.Parent is ArgumentSyntax argument &&
            argument.Parent?.Parent is ObjectCreationExpressionSyntax objectCreation)
        {
            var constructor = semanticModel.GetSymbolInfo(objectCreation, cancellationToken).Symbol as IMethodSymbol;
            return constructor != null && IsMaterializingConstructor(constructor.ContainingType.Name);
        }

        return false;
    }

    private static bool IsMaterializingMethod(string methodName)
    {
        return methodName == "AsEnumerable" ||
               methodName == "ToList" ||
               methodName == "ToArray" ||
               methodName == "ToDictionary" ||
               methodName == "ToHashSet" ||
               methodName == "ToLookup" ||
               methodName.StartsWith("ToImmutable");
    }

    private static bool IsMaterializingConstructor(string typeName)
    {
        return typeName is
            "List" or
            "HashSet" or
            "Dictionary" or
            "SortedDictionary" or
            "SortedList" or
            "LinkedList" or
            "Queue" or
            "Stack";
    }
}
