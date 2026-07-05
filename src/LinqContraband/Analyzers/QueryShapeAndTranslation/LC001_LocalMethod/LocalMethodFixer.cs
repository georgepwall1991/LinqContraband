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

namespace LinqContraband.Analyzers.LC001_LocalMethod;

/// <summary>
/// Provides code fixes for LC001. Switches LINQ queries to client-side evaluation using AsEnumerable() when local methods are called.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalMethodFixer))]
[Shared]
public sealed class LocalMethodFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(LocalMethodAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>().FirstOrDefault();

        if (invocation == null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var queryInvocation = FindQueryInvocation(invocation, semanticModel, context.CancellationToken);
        if (queryInvocation == null ||
            IsNestedQueryInvocation(semanticModel, queryInvocation, context.CancellationToken) ||
            !CanRewriteQueryInvocation(semanticModel, queryInvocation, context.CancellationToken))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Switch to client-side evaluation (AsEnumerable)",
                c => SwitchToClientSideAsync(context.Document, invocation, c),
                "SwitchToClientSide"),
            diagnostic);
    }

    private async Task<Document> SwitchToClientSideAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        editor.EnsureUsing("System.Linq");

        var queryInvocation = FindQueryInvocation(invocation, semanticModel, cancellationToken);
        if (queryInvocation == null) return document;

        if (queryInvocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        if (IsSystemLinqQueryableType(semanticModel, memberAccess.Expression, cancellationToken))
        {
            var enumerableQualifier = CreateEnumerableQualifier(memberAccess.Expression);
            if (!RewriteStaticQueryableInvocation(
                    editor,
                    semanticModel,
                    queryInvocation,
                    memberAccess,
                    enumerableQualifier,
                    cancellationToken))
                return document;

            RewriteEnclosingStaticQueryableContinuations(editor, semanticModel, queryInvocation, cancellationToken);

            return editor.GetChangedDocument();
        }

        // 3. Check if it is using extension method syntax: source.Where(...)
        var source = memberAccess.Expression;

        if (IsInvocationOf(source, "AsEnumerable")) return editor.GetChangedDocument();

        // 4. Create .AsEnumerable() call on the source
        var asEnumerableInvocation = CreateAsEnumerableInvocation(source);

        // 5. Replace the original source with the new source, preserving trivia
        editor.ReplaceNode(source, asEnumerableInvocation.WithTriviaFrom(source));

        return editor.GetChangedDocument();
    }

    private static bool RewriteStaticQueryableInvocation(
        DocumentEditor editor,
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        MemberAccessExpressionSyntax memberAccess,
        ExpressionSyntax enumerableQualifier,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputSequenceArgument(semanticModel, queryInvocation, cancellationToken, out var sourceArgument))
            return false;

        var source = sourceArgument.Expression;
        var rewritesOrderedSource = IsThenBy(memberAccess.Name.Identifier.ValueText);
        var sourceWasRewritten = rewritesOrderedSource &&
            (RewriteStaticQueryableSourceChain(editor, semanticModel, source, cancellationToken) ||
             RewriteQueryableExtensionOrderingSourceChain(editor, semanticModel, source, cancellationToken));

        if (!sourceWasRewritten && rewritesOrderedSource)
            return false;

        if (!sourceWasRewritten && !IsInvocationOf(source, "AsEnumerable"))
        {
            var asEnumerableInvocation = CreateAsEnumerableInvocation(source);
            editor.ReplaceNode(source, asEnumerableInvocation.WithTriviaFrom(source));
        }

        editor.ReplaceNode(memberAccess.Expression, enumerableQualifier.WithTriviaFrom(memberAccess.Expression));

        return true;
    }

    private static InvocationExpressionSyntax? FindQueryInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel?.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            return null;

        var parent = invocation.Parent;
        var lambdas = new List<IAnonymousFunctionOperation>();

        while (parent != null)
        {
            if (parent is LambdaExpressionSyntax lambda &&
                semanticModel.GetOperation(lambda, cancellationToken) is IAnonymousFunctionOperation lambdaOperation)
            {
                lambdas.Add(lambdaOperation);
            }

            if (parent is InvocationExpressionSyntax queryInvocation &&
                IsQueryableInvocation(semanticModel, queryInvocation, cancellationToken) &&
                lambdas.Count > 0 &&
                InvocationDependsOnLambdaParameter(invocationOperation, lambdas[lambdas.Count - 1]))
            {
                return queryInvocation;
            }

            parent = parent.Parent;
        }

        return null;
    }

    private static bool IsNestedQueryInvocation(
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null) return false;

        foreach (var lambda in queryInvocation.Ancestors().OfType<LambdaExpressionSyntax>())
        {
            var enclosingInvocation = lambda.Parent?.AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault();

            if (enclosingInvocation != null &&
                IsQueryableInvocation(semanticModel, enclosingInvocation, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQueryableInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return false;

        var type = operation.Instance?.Type;
        if (type == null)
            type = GetInputSequenceArgument(operation)?.Value.Type;

        return type.IsIQueryable();
    }

    private static IArgumentOperation? GetInputSequenceArgument(IInvocationOperation invocation)
    {
        IArgumentOperation? firstArgument = null;
        IArgumentOperation? namedSequenceArgument = null;

        foreach (var argument in invocation.Arguments)
        {
            firstArgument ??= argument;

            if (argument.Parameter?.Type.IsIQueryable() == true)
                return argument;

            if (argument.Parameter?.Name is "source" or "outer")
                namedSequenceArgument ??= argument;
        }

        return namedSequenceArgument ?? firstArgument;
    }

    private static bool InvocationDependsOnLambdaParameter(
        IInvocationOperation invocation,
        IAnonymousFunctionOperation lambda)
    {
        foreach (var parameter in lambda.Symbol.Parameters)
        {
            if (invocation.ReferencesParameter(parameter))
                return true;
        }

        return false;
    }

    private static bool CanRewriteQueryInvocation(
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        CancellationToken cancellationToken)
    {
        if (queryInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (!IsSystemLinqQueryableType(semanticModel, memberAccess.Expression, cancellationToken))
            return true;

        if (!CanSwitchStaticQueryableMethodToEnumerable(semanticModel, memberAccess))
            return false;

        if (!TryGetInputSequenceArgument(semanticModel, queryInvocation, cancellationToken, out var sourceArgument))
            return false;

        return !IsThenBy(memberAccess.Name.Identifier.ValueText) ||
               IsRewritableOrderedSource(semanticModel, sourceArgument.Expression, cancellationToken);
    }

    private static bool TryGetInputSequenceArgument(
        SemanticModel? semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken,
        out ArgumentSyntax sequenceArgument)
    {
        if (semanticModel != null)
        {
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (semanticModel.GetOperation(argument, cancellationToken) is IArgumentOperation argumentOperation &&
                    argumentOperation.Parameter?.Type.IsIQueryable() == true)
                {
                    sequenceArgument = argument;
                    return true;
                }
            }
        }

        return TryGetNamedSequenceArgument(invocation.ArgumentList, out sequenceArgument);
    }

    private static bool TryGetNamedSequenceArgument(
        ArgumentListSyntax argumentList,
        out ArgumentSyntax sequenceArgument)
    {
        if (argumentList.Arguments.Count == 0)
        {
            sequenceArgument = null!;
            return false;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText is "source" or "outer")
            {
                sequenceArgument = argument;
                return true;
            }
        }

        sequenceArgument = argumentList.Arguments[0];
        return true;
    }

    private static bool RewriteStaticQueryableSourceChain(
        DocumentEditor editor,
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        if (source is not InvocationExpressionSyntax sourceInvocation ||
            sourceInvocation.Expression is not MemberAccessExpressionSyntax sourceMemberAccess ||
            !IsSystemLinqQueryableType(semanticModel, sourceMemberAccess.Expression, cancellationToken) ||
            !CanSwitchStaticQueryableMethodToEnumerable(semanticModel, sourceMemberAccess))
            return false;

        var enumerableQualifier = CreateEnumerableQualifier(sourceMemberAccess.Expression);
        return RewriteStaticQueryableInvocation(
            editor,
            semanticModel,
            sourceInvocation,
            sourceMemberAccess,
            enumerableQualifier,
            cancellationToken);
    }

    private static bool RewriteQueryableExtensionOrderingSourceChain(
        DocumentEditor editor,
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        if (source is not InvocationExpressionSyntax sourceInvocation ||
            sourceInvocation.Expression is not MemberAccessExpressionSyntax sourceMemberAccess ||
            !IsQueryableOrderingInvocation(semanticModel, sourceInvocation, cancellationToken))
            return false;

        var receiver = sourceMemberAccess.Expression;
        if (!RewriteStaticQueryableSourceChain(editor, semanticModel, receiver, cancellationToken) &&
            !RewriteQueryableExtensionOrderingSourceChain(editor, semanticModel, receiver, cancellationToken) &&
            !IsInvocationOf(receiver, "AsEnumerable"))
        {
            var asEnumerableInvocation = CreateAsEnumerableInvocation(receiver);
            editor.ReplaceNode(receiver, asEnumerableInvocation.WithTriviaFrom(receiver));
        }

        return true;
    }

    private static bool IsRewritableOrderedSource(
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        return IsStaticQueryableInvocation(semanticModel, source, cancellationToken) ||
               IsQueryableOrderingInvocation(semanticModel, source, cancellationToken);
    }

    private static bool IsStaticQueryableInvocation(
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        return source is InvocationExpressionSyntax sourceInvocation &&
               sourceInvocation.Expression is MemberAccessExpressionSyntax sourceMemberAccess &&
               IsSystemLinqQueryableType(semanticModel, sourceMemberAccess.Expression, cancellationToken);
    }

    private static bool IsQueryableOrderingInvocation(
        SemanticModel? semanticModel,
        ExpressionSyntax source,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null || source is not InvocationExpressionSyntax sourceInvocation)
            return false;

        var symbol = semanticModel.GetSymbolInfo(sourceInvocation, cancellationToken).Symbol as IMethodSymbol;
        var method = symbol?.ReducedFrom ?? symbol;

        return method?.ContainingType?.Name == "Queryable" &&
               method.ContainingType.ContainingNamespace.ToDisplayString() == "System.Linq" &&
               IsOrderingMethod(method.Name);
    }

    private static void RewriteEnclosingStaticQueryableContinuations(
        DocumentEditor editor,
        SemanticModel? semanticModel,
        InvocationExpressionSyntax queryInvocation,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax currentExpression = queryInvocation;

        while (currentExpression.Parent is ArgumentSyntax argument &&
               argument.Parent is ArgumentListSyntax argumentList &&
               argumentList.Parent is InvocationExpressionSyntax parentInvocation &&
               TryGetInputSequenceArgument(semanticModel, parentInvocation, cancellationToken, out var sourceArgument) &&
               argument.Span == sourceArgument.Span &&
               parentInvocation.Expression is MemberAccessExpressionSyntax parentMemberAccess &&
               IsSystemLinqQueryableType(semanticModel, parentMemberAccess.Expression, cancellationToken) &&
               CanSwitchStaticQueryableMethodToEnumerable(semanticModel, parentMemberAccess))
        {
            var enumerableQualifier = CreateEnumerableQualifier(parentMemberAccess.Expression);
            editor.ReplaceNode(parentMemberAccess.Expression, enumerableQualifier.WithTriviaFrom(parentMemberAccess.Expression));
            currentExpression = parentInvocation;
        }
    }

    private static InvocationExpressionSyntax CreateAsEnumerableInvocation(ExpressionSyntax source)
    {
        var receiver = ParenthesizeForMemberAccessIfNeeded(source);

        // construct: source.AsEnumerable()
        var asEnumerable = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            SyntaxFactory.IdentifierName("AsEnumerable"));

        return SyntaxFactory.InvocationExpression(asEnumerable);
    }

    private static ExpressionSyntax ParenthesizeForMemberAccessIfNeeded(ExpressionSyntax source)
    {
        return source switch
        {
            BaseExpressionSyntax or
            ElementAccessExpressionSyntax or
            IdentifierNameSyntax or
            InvocationExpressionSyntax or
            MemberAccessExpressionSyntax or
            ObjectCreationExpressionSyntax or
            ParenthesizedExpressionSyntax or
            ThisExpressionSyntax => source,
            _ => SyntaxFactory.ParenthesizedExpression(source.WithoutTrivia()).WithTriviaFrom(source)
        };
    }

    private static ExpressionSyntax CreateEnumerableQualifier(ExpressionSyntax qualifier)
    {
        return TryGetEnumerableQualifier(qualifier, out var rewrittenQualifier)
            ? rewrittenQualifier
            : SyntaxFactory.ParseExpression("System.Linq.Enumerable").WithTriviaFrom(qualifier);
    }

    private static bool IsSystemLinqQueryableType(
        SemanticModel? semanticModel,
        ExpressionSyntax qualifier,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null) return false;

        var symbol = semanticModel.GetSymbolInfo(qualifier, cancellationToken).Symbol;
        if (IsSystemLinqQueryableTypeSymbol(symbol)) return true;

        return qualifier is NameSyntax nameSyntax &&
               semanticModel.GetAliasInfo(nameSyntax, cancellationToken)?.Target is INamedTypeSymbol aliasTarget &&
               IsSystemLinqQueryableTypeSymbol(aliasTarget);
    }

    private static bool CanSwitchStaticQueryableMethodToEnumerable(
        SemanticModel? semanticModel,
        MemberAccessExpressionSyntax memberAccess)
    {
        if (semanticModel == null) return false;

        var methodName = memberAccess.Name.Identifier.ValueText;
        var enumerableType = semanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");

        return enumerableType?.GetMembers(methodName).OfType<IMethodSymbol>().Any() == true;
    }

    private static bool IsSystemLinqQueryableTypeSymbol(ISymbol? symbol)
    {
        return symbol is INamedTypeSymbol typeSymbol &&
               typeSymbol.Name == "Queryable" &&
               typeSymbol.ContainingNamespace.ToDisplayString() == "System.Linq";
    }

    private static bool TryGetEnumerableQualifier(ExpressionSyntax qualifier, out ExpressionSyntax enumerableQualifier)
    {
        enumerableQualifier = qualifier;

        if (qualifier is IdentifierNameSyntax identifierName &&
            identifierName.Identifier.ValueText == "Queryable")
        {
            enumerableQualifier = SyntaxFactory.ParseExpression("System.Linq.Enumerable")
                .WithTriviaFrom(identifierName);

            return true;
        }

        if (qualifier is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "Queryable")
        {
            var enumerableName = SyntaxFactory.IdentifierName(
                    SyntaxFactory.Identifier(
                        memberAccess.Name.Identifier.LeadingTrivia,
                        "Enumerable",
                        memberAccess.Name.Identifier.TrailingTrivia))
                .WithTriviaFrom(memberAccess.Name);

            enumerableQualifier = memberAccess.WithName(enumerableName);
            return true;
        }

        return false;
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax ma)
            return ma.Name.Identifier.Text == methodName;

        return false;
    }

    private static bool IsThenBy(string methodName)
    {
        return methodName is "ThenBy" or "ThenByDescending";
    }

    private static bool IsOrderingMethod(string methodName)
    {
        return methodName is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending";
    }
}
