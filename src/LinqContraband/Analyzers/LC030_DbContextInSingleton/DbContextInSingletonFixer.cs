using System.Collections.Immutable;
using System.Composition;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

/// <summary>
/// Provides code fixes for LC030. Changes DbContext field to IDbContextFactory&lt;TContext&gt;.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DbContextInSingletonFixer))]
[Shared]
public class DbContextInSingletonFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DbContextInSingletonAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var fieldDecl = token.Parent.AncestorsAndSelf().OfType<FieldDeclarationSyntax>().FirstOrDefault();
        var variableDecl = token.Parent.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        var propDecl = token.Parent.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();

        if (fieldDecl != null && variableDecl != null && fieldDecl.Declaration.Variables.Contains(variableDecl))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Change to IDbContextFactory<T>",
                    c => ApplyFieldFixAsync(context.Document, fieldDecl, variableDecl, c),
                    "ChangeToDbContextFactory"),
                diagnostic);
        }
        else if (propDecl != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Change to IDbContextFactory<T>",
                    c => ApplyPropertyFixAsync(context.Document, propDecl, c),
                    "ChangeToDbContextFactory"),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFieldFixAsync(Document document, FieldDeclarationSyntax fieldDecl,
        VariableDeclaratorSyntax variableDecl, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return document;

        var oldType = fieldDecl.Declaration.Type;

        var factoryType = CreateFactoryType(oldType);

        var newDeclaration = fieldDecl.Declaration.WithType(factoryType.WithTriviaFrom(oldType));
        var newFieldDecl = fieldDecl.WithDeclaration(newDeclaration);

        // Rename variable to add Factory suffix, preserving all declarators
        var variableIndex = fieldDecl.Declaration.Variables.IndexOf(variableDecl);
        if (variableIndex < 0) return document;

        var oldName = variableDecl.Identifier.Text;
        var newName = AddFactorySuffix(oldName);

        if (oldName != newName)
        {
            var newVariable = variableDecl.WithIdentifier(
                SyntaxFactory.Identifier(newName).WithTriviaFrom(variableDecl.Identifier));
            var newVariables = newFieldDecl.Declaration.Variables.Replace(
                newFieldDecl.Declaration.Variables[variableIndex], newVariable);
            newFieldDecl = newFieldDecl.WithDeclaration(
                newFieldDecl.Declaration.WithVariables(newVariables));
        }

        editor.ReplaceNode(fieldDecl, newFieldDecl);
        var fieldSymbol = semanticModel.GetDeclaredSymbol(variableDecl, cancellationToken);

        // Update constructor parameters with matching DbContext type
        if (fieldDecl.Parent is ClassDeclarationSyntax classDecl && fieldSymbol != null)
        {
            UpdateConstructorParameters(editor, classDecl, oldType, factoryType, oldName, newName);
            RewriteMemberUsages(editor, semanticModel, classDecl, fieldSymbol, newName);
        }

        EnsureUsingDirective(editor);

        return editor.GetChangedDocument();
    }

    private static async Task<Document> ApplyPropertyFixAsync(Document document, PropertyDeclarationSyntax propDecl,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return document;

        var oldType = propDecl.Type;

        var factoryType = CreateFactoryType(oldType);

        var newPropDecl = propDecl.WithType(factoryType.WithTriviaFrom(oldType));
        editor.ReplaceNode(propDecl, newPropDecl);
        var propSymbol = semanticModel.GetDeclaredSymbol(propDecl, cancellationToken);

        if (propDecl.Parent is ClassDeclarationSyntax classDecl && propSymbol != null)
        {
            RewriteMemberUsages(editor, semanticModel, classDecl, propSymbol, propDecl.Identifier.Text);
        }

        EnsureUsingDirective(editor);

        return editor.GetChangedDocument();
    }

    private static GenericNameSyntax CreateFactoryType(TypeSyntax dbContextType)
    {
        return SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("IDbContextFactory"),
            SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                    dbContextType.WithoutTrivia())));
    }

    private static void UpdateConstructorParameters(DocumentEditor editor, ClassDeclarationSyntax classDecl,
        TypeSyntax dbContextType, TypeSyntax factoryType, string oldFieldName, string newFieldName)
    {
        var dbContextTypeName = dbContextType.ToString();
        foreach (var constructor in classDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                if (parameter.Type?.ToString() == dbContextTypeName)
                {
                    var newParam = parameter.WithType(factoryType.WithTriviaFrom(parameter.Type));

                    var oldParamName = parameter.Identifier.Text;
                    var newParamName = AddFactorySuffix(oldParamName);
                    if (oldParamName != newParamName)
                    {
                        newParam = newParam.WithIdentifier(
                            SyntaxFactory.Identifier(newParamName).WithTriviaFrom(parameter.Identifier));
                    }

                    editor.ReplaceNode(parameter, newParam);

                    UpdateConstructorBody(editor, constructor, oldFieldName, newFieldName, oldParamName, newParamName);
                }
            }
        }
    }

    private static void UpdateConstructorBody(DocumentEditor editor, ConstructorDeclarationSyntax constructor,
        string oldFieldName, string newFieldName, string oldParamName, string newParamName)
    {
        // Handle expression-bodied constructors: MySvc(AppDbContext db) => _db = db;
        if (constructor.ExpressionBody != null)
        {
            if (constructor.ExpressionBody.Expression is AssignmentExpressionSyntax assignment)
            {
                RenameAssignment(editor, assignment, oldFieldName, newFieldName, oldParamName, newParamName);
            }
            return;
        }

        // Handle block-bodied constructors: MySvc(AppDbContext db) { _db = db; }
        if (constructor.Body == null) return;

        foreach (var statement in constructor.Body.Statements)
        {
            if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
            {
                RenameAssignment(editor, assignment, oldFieldName, newFieldName, oldParamName, newParamName);
            }
        }
    }

    private static void RenameAssignment(DocumentEditor editor, AssignmentExpressionSyntax assignment,
        string oldFieldName, string newFieldName, string oldParamName, string newParamName)
    {
        var newAssignment = assignment;

        if (assignment.Left is IdentifierNameSyntax leftId && leftId.Identifier.Text == oldFieldName &&
            oldFieldName != newFieldName)
        {
            newAssignment = newAssignment.WithLeft(
                SyntaxFactory.IdentifierName(newFieldName).WithTriviaFrom(leftId));
        }
        // Also handle `this._db = db` pattern
        else if (assignment.Left is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax memberId }
                 && memberId.Identifier.Text == oldFieldName && oldFieldName != newFieldName)
        {
            newAssignment = newAssignment.WithLeft(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ThisExpression(),
                    SyntaxFactory.IdentifierName(newFieldName)).WithTriviaFrom(assignment.Left));
        }

        if (assignment.Right is IdentifierNameSyntax rightId && rightId.Identifier.Text == oldParamName &&
            oldParamName != newParamName)
        {
            newAssignment = newAssignment.WithRight(
                SyntaxFactory.IdentifierName(newParamName).WithTriviaFrom(rightId));
        }

        if (newAssignment != assignment)
        {
            editor.ReplaceNode(assignment, newAssignment);
        }
    }

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
            // `this._db` is represented as a member access; replace the parent node instead.
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
