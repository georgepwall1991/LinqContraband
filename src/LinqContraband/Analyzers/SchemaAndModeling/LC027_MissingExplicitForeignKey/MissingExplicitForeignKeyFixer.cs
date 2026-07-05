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
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

/// <summary>
/// Provides code fixes for LC027. Inserts an explicit FK property above the navigation property.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingExplicitForeignKeyFixer))]
[Shared]
public sealed class MissingExplicitForeignKeyFixer : CodeFixProvider
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
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var navSymbol = semanticModel.GetDeclaredSymbol(navProperty, cancellationToken) as IPropertySymbol;
        if (navSymbol == null) return document;

        var navType = navSymbol.Type as INamedTypeSymbol;
        var fkTypeName = "int"; // default
        var nullableForeignKey = false;

        if (navType != null)
        {
            var pkProp = TryFindConfiguredPrimaryKey(navType, semanticModel, cancellationToken) ??
                         TryFindConventionPrimaryKey(navType);
            if (pkProp != null)
            {
                fkTypeName = pkProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                nullableForeignKey = navSymbol.NullableAnnotation == NullableAnnotation.Annotated &&
                                     pkProp.Type.IsValueType &&
                                     pkProp.NullableAnnotation != NullableAnnotation.Annotated;
            }
        }

        var fkName = $"{navSymbol.Name}Id";
        var fkType = SyntaxFactory.ParseTypeName(fkTypeName);
        if (nullableForeignKey)
            fkType = SyntaxFactory.NullableType(fkType);

        var fkProperty = SyntaxFactory.PropertyDeclaration(
                fkType,
                SyntaxFactory.Identifier(fkName))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
            .NormalizeWhitespace()
            .WithLeadingTrivia(GetIndentationTrivia(navProperty))
            .WithTrailingTrivia(navProperty.GetDocumentEndOfLine());

        if (navProperty.Parent is not TypeDeclarationSyntax typeDeclaration)
            return document;

        var propertyIndex = typeDeclaration.Members.IndexOf(navProperty);
        if (propertyIndex < 0)
            return document;

        var updatedType = typeDeclaration.WithMembers(typeDeclaration.Members.Insert(propertyIndex, fkProperty));
        var updatedRoot = root.ReplaceNode(typeDeclaration, updatedType);
        return document.WithSyntaxRoot(updatedRoot);
    }

    private static IPropertySymbol? TryFindConventionPrimaryKey(INamedTypeSymbol entityType)
    {
        var pkName = entityType.TryFindPrimaryKey();
        return pkName == null
            ? null
            : entityType.GetMembers(pkName).OfType<IPropertySymbol>().FirstOrDefault();
    }

    private static IPropertySymbol? TryFindConfiguredPrimaryKey(
        INamedTypeSymbol entityType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var tree in semanticModel.Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = tree == semanticModel.SyntaxTree
                ? semanticModel
                : semanticModel.Compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);

            foreach (var invocationSyntax in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText != "HasKey" ||
                    model.GetOperation(invocationSyntax, cancellationToken) is not IInvocationOperation invocation ||
                    !TryGetEntityTypeBuilderEntity(invocation.GetInvocationReceiverType(), out var configuredEntity) ||
                    !SymbolEqualityComparer.Default.Equals(configuredEntity, entityType) ||
                    invocation.Arguments.FirstOrDefault()?.Value.UnwrapConversions() is not IAnonymousFunctionOperation lambda)
                {
                    continue;
                }

                var body = lambda.Body.Operations.FirstOrDefault();
                if (body is IReturnOperation returnOperation)
                    body = returnOperation.ReturnedValue;

                if (body?.UnwrapConversions() is IPropertyReferenceOperation propertyReference &&
                    propertyReference.Instance?.UnwrapConversions() is IParameterReferenceOperation parameterReference &&
                    SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, lambda.Symbol.Parameters.FirstOrDefault()))
                {
                    return propertyReference.Property;
                }
            }
        }

        return null;
    }

    private static bool TryGetEntityTypeBuilderEntity(ITypeSymbol? receiverType, out ITypeSymbol entityType)
    {
        entityType = null!;

        if (receiverType is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.TypeArguments.Length != 1 ||
            namedType.Name != "EntityTypeBuilder")
        {
            return false;
        }

        var namespaceName = namedType.ContainingNamespace?.ToString();
        if (namespaceName is not ("Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Metadata.Builders"))
            return false;

        entityType = namedType.TypeArguments[0];
        return true;
    }

    private static SyntaxTriviaList GetIndentationTrivia(PropertyDeclarationSyntax property)
    {
        var leadingTrivia = property.GetLeadingTrivia();
        return SyntaxFactory.TriviaList(
            leadingTrivia
                .Reverse()
                .TakeWhile(trivia => !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                .Reverse());
    }
}
