using System;
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
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SingleEntityScalarProjectionFixer))]
[Shared]
public sealed class SingleEntityScalarProjectionFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SingleEntityScalarProjectionAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation == null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        if (!TryGetFixContext(invocation, semanticModel, out var fixContext))
            return;

        if (!IsSafeFixMaterializer(invocation))
            return;

        if (!fixContext.IsVarDeclaration)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Project consumed scalar before materializing",
                c => ApplyFixAsync(context.Document, invocation, fixContext, c),
                "ProjectConsumedScalar"),
            diagnostic);
    }

    private static bool IsSafeFixMaterializer(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var methodName = memberAccess.Name.Identifier.Text;
        return methodName is "First" or "FirstAsync" or "Single" or "SingleAsync";
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        FixContext fixContext,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var newInvocation = RewriteInvocation(invocation, fixContext.PropertyName);
        editor.ReplaceNode(invocation, newInvocation.WithTriviaFrom(invocation));

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel != null)
        {
            foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                var operation = semanticModel.GetOperation(memberAccess, cancellationToken) as IPropertyReferenceOperation;
                if (operation == null)
                    continue;

                if (operation.Property.Name != fixContext.PropertyName)
                    continue;

                if (operation.Instance?.UnwrapConversions() is not ILocalReferenceOperation localReference ||
                    !SymbolEqualityComparer.Default.Equals(localReference.Local, fixContext.Local))
                {
                    continue;
                }

                editor.ReplaceNode(memberAccess, SyntaxFactory.IdentifierName(fixContext.Local.Name).WithTriviaFrom(memberAccess));
            }
        }

        return editor.GetChangedDocument();
    }

    private static ExpressionSyntax RewriteInvocation(InvocationExpressionSyntax invocation, string propertyName)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return invocation;

        var receiver = memberAccess.Expression.WithoutTrivia();
        var methodName = memberAccess.Name.Identifier.Text;
        var arguments = invocation.ArgumentList.Arguments;
        var predicateIndex = FindPredicateIndex(arguments);
        var tailArguments = predicateIndex >= 0
            ? arguments.Skip(predicateIndex + 1).Select(argument => argument.ToString()).ToArray()
            : arguments.Select(argument => argument.ToString()).ToArray();

        var builder = new System.Text.StringBuilder();
        builder.Append(receiver.ToString());

        if (predicateIndex >= 0)
        {
            builder.Append(".Where(");
            builder.Append(arguments[predicateIndex]);
            builder.Append(')');
        }

        builder.Append(".Select(x => x.");
        builder.Append(propertyName);
        builder.Append(')');
        builder.Append('.');
        builder.Append(methodName);
        builder.Append('(');
        builder.Append(string.Join(", ", tailArguments));
        builder.Append(')');

        return SyntaxFactory.ParseExpression(builder.ToString()).WithTriviaFrom(invocation);
    }

    private static int FindPredicateIndex(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax)
                return i;
        }

        return -1;
    }

    private static bool TryGetFixContext(InvocationExpressionSyntax invocation, SemanticModel semanticModel, out FixContext fixContext)
    {
        fixContext = null!;

        var declarator = invocation.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        if (declarator == null)
            return false;

        if (declarator.Parent is not VariableDeclarationSyntax declaration ||
            !declaration.Type.IsVar)
        {
            return false;
        }

        if (semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol assignedLocal)
            return false;

        var operation = semanticModel.GetOperation(invocation);
        if (operation == null)
            return false;

        var executableRoot = operation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return false;

        if (!SingleEntityScalarProjectionAnalyzer.TryAnalyzeLocalUsage(executableRoot, assignedLocal, out var property))
            return false;

        fixContext = new FixContext(assignedLocal, property.Name, true);
        return true;
    }

    private sealed class FixContext
    {
        public FixContext(ILocalSymbol local, string propertyName, bool isVarDeclaration)
        {
            Local = local;
            PropertyName = propertyName;
            IsVarDeclaration = isVarDeclaration;
        }

        public ILocalSymbol Local { get; }

        public string PropertyName { get; }

        public bool IsVarDeclaration { get; }
    }
}
