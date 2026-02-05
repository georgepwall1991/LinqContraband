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

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

/// <summary>
/// Provides code fixes for LC027. Inserts an explicit FK property above the navigation property.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingExplicitForeignKeyFixer))]
[Shared]
public class MissingExplicitForeignKeyFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingExplicitForeignKeyAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        var propertyDecl = node as PropertyDeclarationSyntax
                           ?? node.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();

        if (propertyDecl == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add foreign key property",
                c => ApplyFixAsync(context.Document, propertyDecl, c),
                "AddForeignKeyProperty"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, PropertyDeclarationSyntax navProperty,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var navSymbol = semanticModel.GetDeclaredSymbol(navProperty, cancellationToken) as IPropertySymbol;
        if (navSymbol == null) return document;

        // Determine the FK type from the navigation entity's PK
        var navType = navSymbol.Type as INamedTypeSymbol;
        var fkTypeName = "int"; // default

        if (navType != null)
        {
            var pkName = navType.TryFindPrimaryKey();
            if (pkName != null)
            {
                var pkProp = navType.GetMembers(pkName).OfType<IPropertySymbol>().FirstOrDefault();
                if (pkProp != null)
                    fkTypeName = pkProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            }
        }

        var fkName = $"{navSymbol.Name}Id";

        // Create the FK property declaration
        var fkProperty = SyntaxFactory.PropertyDeclaration(
                SyntaxFactory.ParseTypeName(fkTypeName),
                SyntaxFactory.Identifier(fkName))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
            .WithLeadingTrivia(navProperty.GetLeadingTrivia())
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        editor.InsertBefore(navProperty, fkProperty);

        return editor.GetChangedDocument();
    }
}
