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

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

/// <summary>
/// Provides code fixes for LC015. Injects OrderBy(x => x.Id) before unordered Skip/Last calls.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingOrderByFixer))]
[Shared]
public class MissingOrderByFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingOrderByAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return;

        // Pre-check if we can determine the primary key before registering the fix
        // This prevents offering a code fix when we can't reliably generate valid code
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Name.Identifier.ValueText is not ("Skip" or "Take" or "Last" or "LastOrDefault" or "Chunk")) return;

        var sourceExpression = memberAccess.Expression;
        var sourceType = semanticModel.GetTypeInfo(sourceExpression).Type;

        ITypeSymbol? entityType = null;
        if (sourceType is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            entityType = namedType.TypeArguments[0];

        if (entityType == null) return;

        // Composite keys produce an ambiguous OrderBy. TryFindPrimaryKey returns the
        // first `[Key]` property and never sees siblings, so without this check the
        // fixer would offer a partial-key OrderBy that does not guarantee
        // deterministic pagination — the very behaviour LC015 exists to flag.
        if (HasCompositeKeyAttribute(entityType)) return;

        var keyName = entityType.TryFindPrimaryKey();
        if (keyName == null) return; // Don't offer fix if we can't determine the primary key

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add OrderBy",
                c => AddOrderByAsync(context.Document, invocation, keyName, c),
                nameof(MissingOrderByFixer)),
            diagnostic);
    }

    private static bool HasCompositeKeyAttribute(ITypeSymbol entityType)
    {
        var propertyKeyCount = 0;
        for (var current = entityType; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            // EF Core 7+ class-level composite-key declaration:
            //   [PrimaryKey(nameof(TenantId), nameof(Id))]
            // Treated as a composite key when two or more property names are
            // supplied, regardless of which key part the entity also exposes
            // through `[Key]` or the `Id` / `<Entity>Id` conventions.
            foreach (var attr in current.GetAttributes())
            {
                if (attr.AttributeClass is { Name: "PrimaryKeyAttribute" } primaryKeyAttr &&
                    primaryKeyAttr.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore" &&
                    CountPrimaryKeyParts(attr) >= 2)
                {
                    return true;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;

                foreach (var attr in prop.GetAttributes())
                {
                    if (attr.AttributeClass is { Name: "KeyAttribute" } attrClass &&
                        attrClass.ContainingNamespace?.ToString() == "System.ComponentModel.DataAnnotations")
                    {
                        propertyKeyCount++;
                        if (propertyKeyCount >= 2) return true;
                        break;
                    }
                }
            }
        }

        return false;
    }

    private static int CountPrimaryKeyParts(AttributeData attribute)
    {
        // [PrimaryKey] accepts either a params string[] of property names or
        // a single property name plus additional names. Count both positional
        // and array-valued arguments.
        var count = 0;
        foreach (var arg in attribute.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Array)
                count += arg.Values.Length;
            else
                count += 1;
        }

        return count;
    }

    private async Task<Document> AddOrderByAsync(Document document, InvocationExpressionSyntax invocation,
        string keyName, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var sourceExpression = memberAccess.Expression;

        // Generate: .OrderBy(x => x.{keyName})
        var generator = editor.Generator;

        // Lambda: x => x.Id
        var lambdaParamName = "x";
        var lambda = generator.ValueReturningLambdaExpression(
            lambdaParamName,
            generator.MemberAccessExpression(generator.IdentifierName(lambdaParamName), keyName)
        );

        // Expression: source.OrderBy(...)
        var orderByInvocation = generator.InvocationExpression(
            generator.MemberAccessExpression(sourceExpression, "OrderBy"),
            lambda
        );

        // Replace the original source expression (e.g. 'db.Users') with 'db.Users.OrderBy(x => x.Id)'
        // But wait, 'sourceExpression' is inside 'invocation'.
        // Example: db.Users.Skip(10)
        // sourceExpression = db.Users
        // invocation = db.Users.Skip(10)
        // We want: db.Users.OrderBy(x => x.Id).Skip(10)

        // If we replace sourceExpression, we are modifying the tree correctly.
        editor.ReplaceNode(sourceExpression, orderByInvocation);

        editor.EnsureUsing("System.Linq");

        return editor.GetChangedDocument();
    }
}
