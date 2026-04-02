using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonFixer
{
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
        if (constructor.ExpressionBody != null)
        {
            if (constructor.ExpressionBody.Expression is AssignmentExpressionSyntax assignment)
            {
                RenameAssignment(editor, assignment, oldFieldName, newFieldName, oldParamName, newParamName);
            }
            return;
        }

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
}
