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
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use string.Equals with StringComparison.OrdinalIgnoreCase",
                    c => FixAsync(context.Document, node, c),
                    nameof(AvoidStringCaseConversionFixer)),
                diagnostic);
        }
    }

    private bool CanFix(InvocationExpressionSyntax node)
    {
        // Case 1: Binary Expression (== or !=)
        if (node.Parent is BinaryExpressionSyntax binary &&
            (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)))
        {
            return true;
        }

        // Case 2: .Equals() method call
        // structure: Invocation(MemberAccess(Expression=Invocation(ToLower), Name=Equals))
        if (node.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "Equals" &&
            memberAccess.Parent is InvocationExpressionSyntax equalsInvocation &&
            equalsInvocation.ArgumentList.Arguments.Count == 1)
        {
            return true;
        }

        return false;
    }

    private async Task<Document> FixAsync(Document document, InvocationExpressionSyntax toLowerInvocation, CancellationToken cancellationToken)
    {
        var generator = SyntaxGenerator.GetGenerator(document);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        ExpressionSyntax? targetNode = null;
        ExpressionSyntax? left = null;
        ExpressionSyntax? right = null;
        bool isNotEquals = false;

        // Extract the "Instance" that ToLower was called on.
        // toLowerInvocation is `x.ToLower()`. Expression is `x.ToLower` (MemberAccess).
        if (toLowerInvocation.Expression is not MemberAccessExpressionSyntax toLowerAccess) return document;
        
        left = toLowerAccess.Expression; // `x`

        // Identify parent structure
        if (toLowerInvocation.Parent is BinaryExpressionSyntax binary)
        {
            targetNode = binary;
            // If toLowerInvocation is on the left, right is the other side.
            // If toLowerInvocation is on the right, left is the other side (but we want to compare `x` vs `other`).
            // Actually, `string.Equals(a, b)` is symmetric.
            
            // We found `left` as the thing ToLower was called on.
            // We need the "other" operand of the binary expression.
            var otherOperand = binary.Left == toLowerInvocation ? binary.Right : binary.Left;
            right = otherOperand;

            if (binary.IsKind(SyntaxKind.NotEqualsExpression))
            {
                isNotEquals = true;
            }
        }
        else if (toLowerInvocation.Parent is MemberAccessExpressionSyntax memberAccess &&
                 memberAccess.Parent is InvocationExpressionSyntax equalsInvocation)
        {
            targetNode = equalsInvocation;
            right = equalsInvocation.ArgumentList.Arguments[0].Expression;
            // .Equals is usually equality.
        }

        if (targetNode == null || left == null || right == null) return document;

        // Build: string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
        var stringType = generator.TypeExpression(SpecialType.System_String);
        
        // We want StringComparison.OrdinalIgnoreCase as a MemberAccessExpression, not QualifiedName.
        // DottedName produces QualifiedName.
        var stringComparisonType = generator.IdentifierName("StringComparison");
        var stringComparison = generator.MemberAccessExpression(stringComparisonType, "OrdinalIgnoreCase");
        
        var replacement = generator.InvocationExpression(
            generator.MemberAccessExpression(stringType, "Equals"),
            left,
            right,
            stringComparison);

        if (isNotEquals)
        {
            replacement = generator.LogicalNotExpression(replacement);
        }

        var newRoot = root.ReplaceNode(targetNode, replacement);
        
        // Add "using System;" if missing (for StringComparison)
        // SyntaxGenerator handles namespaces gracefully? Not always "using" directives.
        // We'll manually check/add or let the user do it?
        // Better to try to add the import.
        var compilationUnit = newRoot as CompilationUnitSyntax;
        if (compilationUnit != null)
        {
            // Check if System is imported
            bool hasSystem = compilationUnit.Usings.Any(u => u.Name?.ToString() == "System");
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
