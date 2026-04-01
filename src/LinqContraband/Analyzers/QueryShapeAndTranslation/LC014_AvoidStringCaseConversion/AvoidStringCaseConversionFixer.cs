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

namespace LinqContraband.Analyzers.LC014_AvoidStringCaseConversion;

/// <summary>
/// Provides code fixes for LC014. Replaces ToLower/ToUpper comparisons with string.Equals using StringComparison.OrdinalIgnoreCase.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidStringCaseConversionFixer))]
[Shared]
public class AvoidStringCaseConversionFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidStringCaseConversionAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // The diagnostic is reported on the Invocation of ToLower/ToUpper
        var node = root?.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (node == null) return;

        // Check if we can fix this usage
        if (CanFix(node))
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use string.Equals with StringComparison.OrdinalIgnoreCase",
                    c => FixAsync(context.Document, node, c),
                    nameof(AvoidStringCaseConversionFixer)),
                diagnostic);
    }

    private bool CanFix(InvocationExpressionSyntax node)
    {
        var parent = node.Parent;

        // Handle Conditional Access: u.Name?.ToLower()
        // Parent is ConditionalAccessExpression. We need to look at ITS parent.
        if (parent is ConditionalAccessExpressionSyntax) parent = parent.Parent;

        if (parent == null) return false;

        // Case 1: Binary Expression (== or !=)
        if (parent is BinaryExpressionSyntax binary &&
            (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)))
            return true;

        // Case 2: .Equals() method call
        // structure: Invocation(MemberAccess(Expression=Invocation(ToLower), Name=Equals))
        if (parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "Equals" &&
            memberAccess.Parent is InvocationExpressionSyntax equalsInvocation &&
            equalsInvocation.ArgumentList.Arguments.Count == 1)
            return true;

        return false;
    }

    private async Task<Document> FixAsync(Document document, InvocationExpressionSyntax toLowerInvocation,
        CancellationToken cancellationToken)
    {
        var generator = SyntaxGenerator.GetGenerator(document);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        ExpressionSyntax? targetNode = null;
        ExpressionSyntax? left = null;
        ExpressionSyntax? right = null;
        var isNotEquals = false;

        var currentParent = toLowerInvocation.Parent;
        SyntaxNode?
            comparisonNode = toLowerInvocation; // The node participating in comparison (Binary or Equals target)

        // 1. Extract 'left' (the string instance)
        if (currentParent is ConditionalAccessExpressionSyntax conditional)
        {
            // Case: u.Name?.ToLower()
            left = conditional.Expression;
            comparisonNode = conditional;
            currentParent = conditional.Parent;
        }
        else if (toLowerInvocation.Expression is MemberAccessExpressionSyntax toLowerAccess)
        {
            // Case: u.Name.ToLower()
            left = toLowerAccess.Expression;
        }
        else
        {
            return document;
        }

        if (currentParent == null) return document;

        // 2. Identify comparison structure
        if (currentParent is BinaryExpressionSyntax binary)
        {
            targetNode = binary;
            // Determine which side is 'right' (the other operand)
            var otherOperand = binary.Left == comparisonNode ? binary.Right : binary.Left;
            right = otherOperand;

            if (binary.IsKind(SyntaxKind.NotEqualsExpression)) isNotEquals = true;
        }
        else if (currentParent is MemberAccessExpressionSyntax memberAccess &&
                 memberAccess.Parent is InvocationExpressionSyntax equalsInvocation)
        {
            targetNode = equalsInvocation;
            right = equalsInvocation.ArgumentList.Arguments[0].Expression;
        }

        if (targetNode == null || left == null || right == null) return document;

        // Build: string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
        var stringType = generator.TypeExpression(SpecialType.System_String);

        // We want StringComparison.OrdinalIgnoreCase as a MemberAccessExpression, not QualifiedName.
        var stringComparisonType = generator.IdentifierName("StringComparison");
        var stringComparison = generator.MemberAccessExpression(stringComparisonType, "OrdinalIgnoreCase");

        var replacement = generator.InvocationExpression(
            generator.MemberAccessExpression(stringType, "Equals"),
            left,
            right,
            stringComparison);

        if (isNotEquals) replacement = generator.LogicalNotExpression(replacement);

        var newRoot = root.ReplaceNode(targetNode, replacement);

        // Add "using System;" if missing (for StringComparison)
        // SyntaxGenerator handles namespaces gracefully? Not always "using" directives.
        // We'll manually check/add or let the user do it?
        // Better to try to add the import.
        var compilationUnit = newRoot as CompilationUnitSyntax;
        if (compilationUnit != null)
        {
            // Check if System is imported
            var hasSystem = compilationUnit.Usings.Any(u => u.Name?.ToString() == "System");
            if (!hasSystem)
            {
                var systemUsing = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("System"))
                    .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));
                // Add to top
                newRoot = compilationUnit.AddUsings(systemUsing);
            }
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
