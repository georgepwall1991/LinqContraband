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

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

/// <summary>
/// Provides code fixes for LC011. Adds an 'Id' property to the entity.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EntityMissingPrimaryKeyFixer))]
[Shared]
public class EntityMissingPrimaryKeyFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EntityMissingPrimaryKeyAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        // The diagnostic is on the DbSet property in the DbContext
        var propertyDecl = token.Parent.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (propertyDecl == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add 'Id' property to entity",
                c => ApplyFixAsync(context.Document, propertyDecl, c),
                "AddIdProperty"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, PropertyDeclarationSyntax propertyDecl, CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var propertySymbol = semanticModel.GetDeclaredSymbol(propertyDecl, cancellationToken) as IPropertySymbol;
        if (propertySymbol == null) return document;

        // Get the TEntity from DbSet<TEntity>
        var dbSetType = propertySymbol.Type as INamedTypeSymbol;
        if (dbSetType == null || dbSetType.TypeArguments.Length == 0) return document;

        var entityType = dbSetType.TypeArguments[0];
        var entitySyntaxRef = entityType.DeclaringSyntaxReferences.FirstOrDefault();
        if (entitySyntaxRef == null) return document;

        var entitySyntax = await entitySyntaxRef.GetSyntaxAsync(cancellationToken).ConfigureAwait(false) as ClassDeclarationSyntax;
        if (entitySyntax == null) return document;

        // We need to get the document containing the entity
        var entityDocument = document.Project.GetDocument(entitySyntax.SyntaxTree);
        if (entityDocument == null) return document;

        var editor = await DocumentEditor.CreateAsync(entityDocument, cancellationToken).ConfigureAwait(false);
        
        var idProperty = SyntaxFactory.PropertyDeclaration(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
            SyntaxFactory.Identifier("Id"))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);

        editor.InsertMembers(entitySyntax, 0, new[] { idProperty });

        return editor.GetChangedDocument();
    }
}
