using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

/// <summary>
/// Provides code fixes for LC030. Changes DbContext field to IDbContextFactory&lt;TContext&gt;.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DbContextInSingletonFixer))]
[Shared]
public sealed partial class DbContextInSingletonFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray<string>.Empty;

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var fieldDecl = token.Parent.AncestorsAndSelf().OfType<FieldDeclarationSyntax>().FirstOrDefault();
        var variableDecl = token.Parent.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        var propDecl = token.Parent.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();

        if (fieldDecl != null && variableDecl != null && fieldDecl.Declaration.Variables.Contains(variableDecl))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Change to IDbContextFactory<T>",
                    c => ApplyFieldFixAsync(context.Document, fieldDecl, variableDecl, c),
                    "ChangeToDbContextFactory"),
                diagnostic);
        }
        else if (propDecl != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Change to IDbContextFactory<T>",
                    c => ApplyPropertyFixAsync(context.Document, propDecl, c),
                    "ChangeToDbContextFactory"),
                diagnostic);
        }
    }
}
