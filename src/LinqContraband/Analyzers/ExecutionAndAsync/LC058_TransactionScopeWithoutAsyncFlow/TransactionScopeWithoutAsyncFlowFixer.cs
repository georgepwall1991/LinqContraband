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

namespace LinqContraband.Analyzers.LC058_TransactionScopeWithoutAsyncFlow;

/// <summary>
/// Provides code fixes for LC058. Passes <c>TransactionScopeAsyncFlowOption.Enabled</c> to the constructor overload
/// that takes the same arguments plus the async flow option, or replaces <c>Suppress</c> with <c>Enabled</c>, and adds
/// <c>using System.Transactions;</c> when needed.
/// </summary>
/// <remarks>
/// Overloads without an async flow counterpart, such as the <c>EnterpriseServicesInteropOption</c> ones, get no fix.
/// The fixer compiles the rewritten document and only offers a rewrite that adds no errors.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TransactionScopeWithoutAsyncFlowFixer))]
[Shared]
public sealed class TransactionScopeWithoutAsyncFlowFixer : CodeFixProvider
{
    private const string Title = "Enable TransactionScopeAsyncFlowOption";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(TransactionScopeWithoutAsyncFlowAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        int? errorsBefore = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var creationSyntax = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<BaseObjectCreationExpressionSyntax>();
            if (creationSyntax == null ||
                semanticModel.GetOperation(creationSyntax, cancellationToken) is not IObjectCreationOperation creation ||
                creation.Constructor == null ||
                !TransactionScopeWithoutAsyncFlowAnalyzer.IsTransactionScope(creation.Type))
            {
                continue;
            }

            var newArgumentList = RewriteArguments(creation, creationSyntax);
            if (newArgumentList == null) continue;

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            if (creationSyntax.ArgumentList != null)
                editor.ReplaceNode(creationSyntax.ArgumentList, newArgumentList);
            else
                editor.ReplaceNode(creationSyntax, AddArgumentList(creationSyntax, newArgumentList));
            editor.EnsureUsing("System.Transactions");
            var newDocument = editor.GetChangedDocument();

            errorsBefore ??= CountErrors(semanticModel, cancellationToken);
            var newModel = await newDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (newModel == null || CountErrors(newModel, cancellationToken) > errorsBefore.Value) continue;

            context.RegisterCodeFix(
                CodeAction.Create(Title, _ => Task.FromResult(newDocument), nameof(TransactionScopeWithoutAsyncFlowFixer)),
                diagnostic);
        }
    }

    private static ArgumentListSyntax? RewriteArguments(
        IObjectCreationOperation creation,
        BaseObjectCreationExpressionSyntax creationSyntax)
    {
        var argumentList = creationSyntax.ArgumentList ?? SyntaxFactory.ArgumentList();
        var enabled = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName("TransactionScopeAsyncFlowOption"),
            SyntaxFactory.IdentifierName("Enabled"));

        // Suppress passed explicitly: switch it to Enabled.
        var existing = creation.Arguments.FirstOrDefault(argument =>
            TransactionScopeWithoutAsyncFlowAnalyzer.IsAsyncFlowOptionType(argument.Parameter?.Type));
        if (existing != null)
        {
            if (existing.Syntax is not ArgumentSyntax existingSyntax) return null;
            return argumentList.ReplaceNode(
                existingSyntax,
                existingSyntax.WithExpression(enabled.WithTriviaFrom(existingSyntax.Expression)));
        }

        if (creation.Arguments.Any(argument => argument.ArgumentKind != ArgumentKind.Explicit))
            return null;

        var target = FindAsyncFlowOverload(creation.Constructor!);
        if (target == null) return null;

        var named = argumentList.Arguments.Any(argument => argument.NameColon != null);
        var newArgument = named
            ? SyntaxFactory.Argument(
                SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(target.Parameters.Last().Name)),
                default,
                enabled)
            : SyntaxFactory.Argument(enabled);

        return argumentList.WithArguments(argumentList.Arguments.Add(newArgument));
    }

    /// <summary>The constructor with the same parameters as <paramref name="constructor"/> plus a trailing async flow option.</summary>
    private static IMethodSymbol? FindAsyncFlowOverload(IMethodSymbol constructor)
    {
        foreach (var candidate in constructor.ContainingType.InstanceConstructors)
        {
            if (candidate.DeclaredAccessibility != Accessibility.Public ||
                candidate.Parameters.Length != constructor.Parameters.Length + 1 ||
                !TransactionScopeWithoutAsyncFlowAnalyzer.IsAsyncFlowOptionType(candidate.Parameters.Last().Type))
            {
                continue;
            }

            var matches = true;
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(candidate.Parameters[i].Type, constructor.Parameters[i].Type) ||
                    candidate.Parameters[i].Name != constructor.Parameters[i].Name)
                {
                    matches = false;
                    break;
                }
            }

            if (matches) return candidate;
        }

        return null;
    }

    private static SyntaxNode AddArgumentList(BaseObjectCreationExpressionSyntax creation, ArgumentListSyntax argumentList)
    {
        return creation switch
        {
            ObjectCreationExpressionSyntax explicitCreation => explicitCreation.WithType(explicitCreation.Type.WithoutTrailingTrivia())
                .WithArgumentList(argumentList.WithTrailingTrivia(explicitCreation.Type.GetTrailingTrivia())),
            _ => creation
        };
    }

    private static int CountErrors(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
