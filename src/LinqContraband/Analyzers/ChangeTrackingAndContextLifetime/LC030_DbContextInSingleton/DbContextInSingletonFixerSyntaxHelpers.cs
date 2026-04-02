using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonFixer
{
    private static bool TryGetExistingFactoryContextLocalName(BlockSyntax body, string factoryMemberName,
        out string localName)
    {
        foreach (var statement in body.Statements.OfType<LocalDeclarationStatementSyntax>())
        {
            if (!statement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            {
                continue;
            }

            var declaration = statement.Declaration;
            if (declaration == null || declaration.Variables.Count != 1)
            {
                continue;
            }

            var initializer = declaration.Variables[0].Initializer?.Value as InvocationExpressionSyntax;
            if (initializer?.Expression is not MemberAccessExpressionSyntax
                {
                    Name.Identifier.Text: "CreateDbContext"
                } createCall)
            {
                continue;
            }

            if (createCall.Expression is IdentifierNameSyntax factoryIdentifier &&
                factoryIdentifier.Identifier.Text == factoryMemberName)
            {
                localName = declaration.Variables[0].Identifier.ValueText;
                return true;
            }

            if (createCall.Expression is MemberAccessExpressionSyntax
                {
                    Expression: ThisExpressionSyntax,
                    Name: IdentifierNameSyntax thisMember
                } && thisMember.Identifier.Text == factoryMemberName)
            {
                localName = declaration.Variables[0].Identifier.ValueText;
                return true;
            }
        }

        localName = string.Empty;
        return false;
    }

    private static LocalDeclarationStatementSyntax CreateContextUsingStatement(string factoryMemberName, string localName)
    {
        return SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(localName))
                                .WithInitializer(
                                    SyntaxFactory.EqualsValueClause(
                                        SyntaxFactory.InvocationExpression(
                                            SyntaxFactory.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SyntaxFactory.IdentifierName(factoryMemberName),
                                                SyntaxFactory.IdentifierName("CreateDbContext")))))))
            )
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword))
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);
    }

    private static string GetPreferredContextLocalName(string memberName)
    {
        var baseName = memberName.TrimStart('_');
        if (baseName.EndsWith("Factory", System.StringComparison.Ordinal))
        {
            baseName = baseName.Substring(0, baseName.Length - "Factory".Length);
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            return "db";
        }

        if (char.IsUpper(baseName[0]))
        {
            return char.ToLowerInvariant(baseName[0]) + baseName.Substring(1);
        }

        return baseName;
    }

    private static string GetUniqueLocalName(SyntaxNode scope, string preferredName, IEnumerable<string> reservedNames)
    {
        var usedNames = new HashSet<string>(
            scope.DescendantTokens()
                .Where(t => t.IsKind(SyntaxKind.IdentifierToken))
                .Select(t => t.ValueText),
            System.StringComparer.Ordinal);

        foreach (var reserved in reservedNames)
        {
            usedNames.Add(reserved);
        }

        if (!usedNames.Contains(preferredName))
        {
            return preferredName;
        }

        for (var i = 1; ; i++)
        {
            var candidate = preferredName + i;
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string AddFactorySuffix(string name)
    {
        if (name.EndsWith("Factory"))
            return name;
        return name + "Factory";
    }

    private static void EnsureUsingDirective(DocumentEditor editor)
    {
        const string requiredNamespace = "Microsoft.EntityFrameworkCore";
        var root = editor.OriginalRoot;

        if (root is CompilationUnitSyntax compilationUnit)
        {
            var alreadyHasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == requiredNamespace);
            if (alreadyHasUsing)
            {
                return;
            }

            var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(requiredNamespace))
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            if (compilationUnit.Usings.Any())
            {
                editor.InsertAfter(compilationUnit.Usings.Last(), new[] { usingDirective });
                return;
            }

            if (compilationUnit.Members.Any())
            {
                editor.InsertBefore(compilationUnit.Members.First(), new[] { usingDirective });
                return;
            }

            editor.ReplaceNode(compilationUnit, compilationUnit.AddUsings(usingDirective));
        }
    }
}
