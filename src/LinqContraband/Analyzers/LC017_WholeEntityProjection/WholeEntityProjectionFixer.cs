using System.Collections.Generic;
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

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Provides code fixes for LC017. Adds .Select() projection before the materializer to load only accessed properties.
/// </summary>
/// <remarks>
/// The fixer analyzes the usage pattern of the materialized collection to determine which properties are accessed,
/// then generates an anonymous type projection containing only those properties.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WholeEntityProjectionFixer))]
[Shared]
public class WholeEntityProjectionFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(WholeEntityProjectionAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the invocation expression (ToList(), ToArray(), etc.)
        var invocation = root?.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return;

        // Find the variable that stores the result
        var variableDeclarator = invocation.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        if (variableDeclarator == null) return;

        var variableSymbol = semanticModel.GetDeclaredSymbol(variableDeclarator, context.CancellationToken) as ILocalSymbol;
        if (variableSymbol == null) return;

        // Get the entity type from the query
        var entityType = GetEntityType(invocation, semanticModel);
        if (entityType == null) return;

        // Find all accessed properties
        var accessedProperties = FindAccessedProperties(root!, variableSymbol, entityType, semanticModel);
        if (accessedProperties.Count == 0) return;

        // Register the "safe" fix that preserves property access syntax
        context.RegisterCodeFix(
            CodeAction.Create(
                $"Add .Select() with anonymous type ({accessedProperties.Count} properties)",
                c => AddSelectProjectionAsync(context.Document, invocation, accessedProperties, c),
                nameof(WholeEntityProjectionFixer) + "_AnonymousType"),
            diagnostic);
    }

    private static ITypeSymbol? GetEntityType(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Walk up the LINQ chain to find the DbSet element type
        var current = invocation.Expression;

        while (current is MemberAccessExpressionSyntax memberAccess)
        {
            var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
            var type = typeInfo.Type;

            if (type != null && type.IsDbSet() && type is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                return namedType.TypeArguments[0];
            }

            // Check if it's IQueryable<T> backed by DbSet
            if (type is INamedTypeSymbol nt && nt.IsGenericType && nt.TypeArguments.Length > 0)
            {
                if (type.IsIQueryable())
                {
                    // Continue walking to find DbSet
                    current = memberAccess.Expression;
                    if (current is InvocationExpressionSyntax prevInvocation)
                    {
                        current = prevInvocation.Expression;
                    }
                    continue;
                }
            }

            current = memberAccess.Expression;
        }

        // Handle direct property access (e.g., db.LargeEntities)
        if (current is MemberAccessExpressionSyntax directAccess)
        {
            var typeInfo = semanticModel.GetTypeInfo(directAccess);
            if (typeInfo.Type is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                return namedType.TypeArguments[0];
            }
        }

        // Try getting from the invocation's type argument
        if (invocation.Expression is MemberAccessExpressionSyntax)
        {
            var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol?.ReturnType is INamedTypeSymbol returnType && returnType.TypeArguments.Length > 0)
            {
                return returnType.TypeArguments[0];
            }
        }

        return null;
    }

    private static HashSet<string> FindAccessedProperties(
        SyntaxNode root,
        ILocalSymbol variableSymbol,
        ITypeSymbol entityType,
        SemanticModel semanticModel)
    {
        var properties = new HashSet<string>();

        // Find the containing method
        var containingMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
            {
                var span = m.Span;
                return root.DescendantNodes()
                    .OfType<VariableDeclaratorSyntax>()
                    .Any(v => v.Identifier.Text == variableSymbol.Name && span.Contains(v.Span));
            });

        if (containingMethod == null) return properties;

        // Find all foreach loops iterating over our variable
        foreach (var forEach in containingMethod.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            // Check if this foreach iterates over our variable
            var collectionExpr = forEach.Expression;
            if (collectionExpr is IdentifierNameSyntax id && id.Identifier.Text == variableSymbol.Name)
            {
                // Get the iteration variable
                var iterationVarName = forEach.Identifier.Text;

                // Find all property accesses on the iteration variable
                foreach (var memberAccess in forEach.Statement.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                {
                    // Check if this is accessing our iteration variable
                    if (memberAccess.Expression is IdentifierNameSyntax varRef && varRef.Identifier.Text == iterationVarName)
                    {
                        var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
                        if (symbol is IPropertySymbol prop)
                        {
                            // Verify it's a property of the entity type
                            if (IsPropertyOfType(prop, entityType))
                            {
                                properties.Add(prop.Name);
                            }
                        }
                    }
                }
            }
        }

        return properties;
    }

    private static bool IsPropertyOfType(IPropertySymbol property, ITypeSymbol entityType)
    {
        var propContainingType = property.ContainingType;
        if (propContainingType == null) return false;

        // Direct match
        if (SymbolEqualityComparer.Default.Equals(propContainingType, entityType))
            return true;

        // Check inheritance
        var current = entityType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(propContainingType, current))
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private static async Task<Document> AddSelectProjectionAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        HashSet<string> properties,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var sourceExpression = memberAccess.Expression;
        var paramName = "e";

        // Anonymous type projection preserves downstream property access syntax.
        var propertyAssignments = properties
            .OrderBy(p => p)
            .Select(p => CreateAnonymousObjectMemberDeclarator(paramName, p))
            .ToArray();

        ExpressionSyntax lambdaBody = SyntaxFactory.AnonymousObjectCreationExpression(
            SyntaxFactory.SeparatedList(propertyAssignments));

        var lambda = SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName)),
            lambdaBody);

        // Create: source.Select(e => ...)
        var selectInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                sourceExpression,
                SyntaxFactory.IdentifierName("Select")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(lambda))));

        // Replace the source expression with the Select invocation
        editor.ReplaceNode(sourceExpression, selectInvocation);

        editor.EnsureUsing("System.Linq");

        return editor.GetChangedDocument();
    }

    private static AnonymousObjectMemberDeclaratorSyntax CreateAnonymousObjectMemberDeclarator(string paramName, string propertyName)
    {
        // Creates: e.PropertyName (which in anonymous type becomes PropertyName = e.PropertyName implicitly)
        return SyntaxFactory.AnonymousObjectMemberDeclarator(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(paramName),
                SyntaxFactory.IdentifierName(propertyName)));
    }
}
