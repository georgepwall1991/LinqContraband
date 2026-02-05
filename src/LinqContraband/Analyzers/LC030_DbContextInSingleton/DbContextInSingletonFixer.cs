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
        var propDecl = token.Parent.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();

        if (fieldDecl != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Change to IDbContextFactory<T>",
                    c => ApplyFieldFixAsync(context.Document, fieldDecl, c),
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
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var oldType = fieldDecl.Declaration.Type;

        var factoryType = CreateFactoryType(oldType);

        var newDeclaration = fieldDecl.Declaration.WithType(factoryType.WithTriviaFrom(oldType));
        var newFieldDecl = fieldDecl.WithDeclaration(newDeclaration);

        // Rename variable to add Factory suffix
        var variable = fieldDecl.Declaration.Variables.First();
        var oldName = variable.Identifier.Text;
        var newName = AddFactorySuffix(oldName);

        if (oldName != newName)
        {
            var newVariable = variable.WithIdentifier(
                SyntaxFactory.Identifier(newName).WithTriviaFrom(variable.Identifier));
            newFieldDecl = newFieldDecl.WithDeclaration(
                newFieldDecl.Declaration.WithVariables(
                    SyntaxFactory.SingletonSeparatedList(newVariable)));
        }

        editor.ReplaceNode(fieldDecl, newFieldDecl);

        // Update constructor parameters with matching DbContext type
        if (fieldDecl.Parent is ClassDeclarationSyntax classDecl)
        {
            UpdateConstructorParameters(editor, classDecl, oldType, factoryType, oldName, newName);
        }

        return editor.GetChangedDocument();
    }

    private static async Task<Document> ApplyPropertyFixAsync(Document document, PropertyDeclarationSyntax propDecl,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var oldType = propDecl.Type;

        var factoryType = CreateFactoryType(oldType);

        var newPropDecl = propDecl.WithType(factoryType.WithTriviaFrom(oldType));
        editor.ReplaceNode(propDecl, newPropDecl);

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
        if (constructor.ExpressionBody != null)
        {
            if (constructor.ExpressionBody.Expression is AssignmentExpressionSyntax assignment)
            {
                var newAssignment = assignment;

                if (assignment.Left is IdentifierNameSyntax leftId && leftId.Identifier.Text == oldFieldName &&
                    oldFieldName != newFieldName)
                {
                    newAssignment = newAssignment.WithLeft(
                        SyntaxFactory.IdentifierName(newFieldName).WithTriviaFrom(leftId));
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
        }
    }

    private static string AddFactorySuffix(string name)
    {
        if (name.EndsWith("Factory"))
            return name;
        return name + "Factory";
    }
}
