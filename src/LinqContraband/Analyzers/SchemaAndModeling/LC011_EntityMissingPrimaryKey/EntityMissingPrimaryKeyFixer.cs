using System;
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

        if (!await CanAddIdPropertyAsync(context.Document, propertyDecl, context.CancellationToken).ConfigureAwait(false))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add 'Id' property to entity",
                c => ApplyFixAsync(context.Document, propertyDecl, c),
                "AddIdProperty"),
            diagnostic);
    }

    private static async Task<bool> CanAddIdPropertyAsync(
        Document document,
        PropertyDeclarationSyntax propertyDecl,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var propertySymbol = semanticModel?.GetDeclaredSymbol(propertyDecl, cancellationToken) as IPropertySymbol;
        if (!TryGetEntityType(propertySymbol, out var entityType))
            return false;

        if (HasIdMember(entityType))
            return false;

        return entityType.DeclaringSyntaxReferences.FirstOrDefault() != null;
    }

    private async Task<Document> ApplyFixAsync(Document document, PropertyDeclarationSyntax propertyDecl, CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var propertySymbol = semanticModel?.GetDeclaredSymbol(propertyDecl, cancellationToken) as IPropertySymbol;
        if (!TryGetEntityType(propertySymbol, out var entityType)) return document;
        if (HasIdMember(entityType)) return document;

        var entitySyntaxRef = entityType.DeclaringSyntaxReferences.FirstOrDefault();
        if (entitySyntaxRef == null) return document;

        var entitySyntax = await entitySyntaxRef.GetSyntaxAsync(cancellationToken).ConfigureAwait(false) as TypeDeclarationSyntax;
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

    private static bool TryGetEntityType(IPropertySymbol? propertySymbol, out ITypeSymbol entityType)
    {
        entityType = null!;

        if (propertySymbol?.Type is not INamedTypeSymbol dbSetType || dbSetType.TypeArguments.Length == 0)
            return false;

        entityType = dbSetType.TypeArguments[0];
        return true;
    }

    private static bool HasIdMember(ITypeSymbol entityType)
    {
        var current = entityType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            if (current.GetMembers().Any(member => member.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)))
                return true;

            current = current.BaseType;
        }

        return false;
    }
}
