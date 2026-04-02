using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonFixer
{
    private static void RewriteMemberUsages(DocumentEditor editor, SemanticModel semanticModel,
        ClassDeclarationSyntax classDecl, ISymbol memberSymbol, string factoryMemberName)
    {
        foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            if (method.Body != null)
            {
                RewriteBlockBody(editor, semanticModel, method, method.Body, memberSymbol, factoryMemberName,
                    method.ParameterList.Parameters.Select(p => p.Identifier.ValueText));
                continue;
            }

            if (method.ExpressionBody != null)
            {
                RewriteExpressionBody(editor, semanticModel, method, memberSymbol, factoryMemberName,
                    method.ParameterList.Parameters.Select(p => p.Identifier.ValueText));
            }
        }
    }

    private static void RewriteBlockBody(DocumentEditor editor, SemanticModel semanticModel, MethodDeclarationSyntax method,
        BlockSyntax body, ISymbol memberSymbol, string factoryMemberName, IEnumerable<string> reservedNames)
    {
        var references = FindMemberReferences(body, semanticModel, memberSymbol);
        if (references.Count == 0) return;

        var hasExistingFactoryContext = TryGetExistingFactoryContextLocalName(body, factoryMemberName, out var localName);
        if (!hasExistingFactoryContext)
        {
            localName = GetUniqueLocalName(body, GetPreferredContextLocalName(memberSymbol.Name), reservedNames);
        }

        var rewrittenBody = ReplaceReferences(body, references, localName);
        if (!hasExistingFactoryContext)
        {
            var usingStatement = CreateContextUsingStatement(factoryMemberName, localName);
            rewrittenBody = rewrittenBody.WithStatements(rewrittenBody.Statements.Insert(0, usingStatement));
        }

        editor.ReplaceNode(method.Body!, rewrittenBody);
    }

    private static void RewriteExpressionBody(DocumentEditor editor, SemanticModel semanticModel,
        MethodDeclarationSyntax method, ISymbol memberSymbol, string factoryMemberName, IEnumerable<string> reservedNames)
    {
        var expressionBody = method.ExpressionBody;
        if (expressionBody == null) return;

        var references = FindMemberReferences(expressionBody.Expression, semanticModel, memberSymbol);
        if (references.Count == 0) return;

        var localName = GetUniqueLocalName(expressionBody.Expression, GetPreferredContextLocalName(memberSymbol.Name),
            reservedNames);
        var rewrittenExpression = ReplaceReferences(expressionBody.Expression, references, localName);

        var usingStatement = CreateContextUsingStatement(factoryMemberName, localName);
        StatementSyntax terminalStatement = method.ReturnType is PredefinedTypeSyntax predefinedReturnType &&
                                            predefinedReturnType.Keyword.IsKind(SyntaxKind.VoidKeyword)
            ? SyntaxFactory.ExpressionStatement(rewrittenExpression)
            : SyntaxFactory.ReturnStatement(rewrittenExpression);

        var newMethod = method
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(SyntaxFactory.Block(usingStatement, terminalStatement));

        editor.ReplaceNode(method, newMethod);
    }

    private static List<ExpressionSyntax> FindMemberReferences(SyntaxNode root, SemanticModel semanticModel,
        ISymbol memberSymbol)
    {
        var references = new List<ExpressionSyntax>();

        foreach (var memberAccess in root.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, memberSymbol))
            {
                references.Add(memberAccess);
            }
        }

        foreach (var identifier in root.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Name == identifier)
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, memberSymbol))
            {
                references.Add(identifier);
            }
        }

        return references;
    }

    private static TNode ReplaceReferences<TNode>(TNode root, IReadOnlyCollection<ExpressionSyntax> references,
        string localName)
        where TNode : SyntaxNode
    {
        return root.ReplaceNodes(references, (original, _) =>
            SyntaxFactory.IdentifierName(localName).WithTriviaFrom(original));
    }
}
