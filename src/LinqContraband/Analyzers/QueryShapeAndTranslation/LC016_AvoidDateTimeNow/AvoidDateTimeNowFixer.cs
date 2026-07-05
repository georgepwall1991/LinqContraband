using System.Collections.Generic;
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
using Microsoft.CodeAnalysis.Formatting;
using LinqContraband.Extensions;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

/// <summary>
/// Provides code fixes for LC016. Extracts DateTime.Now to a local variable.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidDateTimeNowFixer))]
[Shared]
public sealed class AvoidDateTimeNowFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidDateTimeNowAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var memberAccess = token.Parent.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
        if (memberAccess == null) return;
        if (!CanApplyFix(memberAccess)) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Extract to local variable",
                c => ApplyFixAsync(context.Document, memberAccess, c),
                "ExtractToLocal"),
            diagnostic);
    }

    private static bool CanApplyFix(MemberAccessExpressionSyntax memberAccess)
    {
        if (IsInsideStaticLambda(memberAccess))
            return false;

        var expressionBody = memberAccess.AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (expressionBody?.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
            return true;

        return memberAccess.AncestorsAndSelf().OfType<StatementSyntax>().Any();
    }

    private async Task<Document> ApplyFixAsync(Document document, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var expressionBody = memberAccess.AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (expressionBody?.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
        {
            return ConvertExpressionBodiedMember(document, editor, memberAccess, semanticModel, cancellationToken);
        }

        // Find the statement containing the expression.
        var statement = memberAccess.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement == null)
        {
            return ConvertExpressionBodiedMember(document, editor, memberAccess, semanticModel, cancellationToken);
        }

        var variableName = GetUniqueVariableName(memberAccess);
        var newVariable = CreateLocalDeclaration(memberAccess, variableName)
            .WithTrailingTrivia(statement.GetDocumentEndOfLine());

        foreach (var access in FindMatchingClockAccesses(memberAccess, semanticModel, cancellationToken))
        {
            editor.ReplaceNode(access, SyntaxFactory.IdentifierName(variableName).WithTriviaFrom(access));
        }

        // Insert the declaration before the statement
        editor.InsertBefore(statement, newVariable);

        return editor.GetChangedDocument();
    }

    private static Document ConvertExpressionBodiedMember(
        Document document,
        DocumentEditor editor,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionBody = memberAccess.AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (expressionBody == null) return document;

        var replacements = BuildExpressionBodyReplacements(
            expressionBody.Expression,
            memberAccess,
            semanticModel,
            cancellationToken);
        if (replacements.Count == 0) return document;

        var rewrittenAccesses = FindExpressionBodyClockAccesses(
            expressionBody.Expression,
            semanticModel,
            cancellationToken);
        var updatedExpression = expressionBody.Expression.ReplaceNodes(
            rewrittenAccesses,
            (original, _) =>
            {
                var replacement = FindReplacementFor(original, replacements, semanticModel, cancellationToken);
                return replacement is null
                    ? original
                    : SyntaxFactory.IdentifierName(replacement.VariableName).WithTriviaFrom(original);
            });

        var endOfLine = expressionBody.GetDocumentEndOfLine();
        var statements = replacements
            .Select(replacement => (StatementSyntax)CreateLocalDeclaration(replacement.Initializer, replacement.VariableName)
                .WithTrailingTrivia(endOfLine))
            .ToList();
        var bodyStatement = CreateExpressionBodyStatement(
            expressionBody.Parent,
            updatedExpression,
            endOfLine,
            semanticModel,
            cancellationToken);
        statements.Add(bodyStatement);

        var body = SyntaxFactory.Block(statements)
            .WithAdditionalAnnotations(Formatter.Annotation);

        switch (expressionBody.Parent)
        {
            case MethodDeclarationSyntax method:
                editor.ReplaceNode(
                    method,
                    method.WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(body)
                        .WithAdditionalAnnotations(Formatter.Annotation));
                return editor.GetChangedDocument();

            case LocalFunctionStatementSyntax localFunction:
                editor.ReplaceNode(
                    localFunction,
                    localFunction.WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(body)
                        .WithAdditionalAnnotations(Formatter.Annotation));
                return editor.GetChangedDocument();

            default:
                return document;
        }
    }

    private static StatementSyntax CreateExpressionBodyStatement(
        SyntaxNode? member,
        ExpressionSyntax expression,
        SyntaxTrivia endOfLine,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (RequiresExpressionStatement(member, semanticModel, cancellationToken))
            return SyntaxFactory.ExpressionStatement(expression).WithTrailingTrivia(endOfLine);

        return SyntaxFactory.ReturnStatement(expression).WithTrailingTrivia(endOfLine);
    }

    private static bool RequiresExpressionStatement(
        SyntaxNode? member,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        member switch
        {
            MethodDeclarationSyntax method => IsVoid(method.ReturnType) ||
                                              (HasAsyncModifier(method.Modifiers) &&
                                               IsNonGenericTaskLike(method.ReturnType, semanticModel, cancellationToken)),
            LocalFunctionStatementSyntax localFunction => IsVoid(localFunction.ReturnType) ||
                                                          (HasAsyncModifier(localFunction.Modifiers) &&
                                                           IsNonGenericTaskLike(localFunction.ReturnType, semanticModel, cancellationToken)),
            _ => false
        };

    private static bool IsVoid(TypeSyntax returnType) =>
        returnType is PredefinedTypeSyntax predefined &&
        predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);

    private static bool HasAsyncModifier(SyntaxTokenList modifiers) =>
        modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword));

    private static bool IsNonGenericTaskLike(
        TypeSyntax returnType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetTypeInfo(returnType, cancellationToken).Type is INamedTypeSymbol namedType &&
            namedType.Arity == 0 &&
            namedType.Name is "Task" or "ValueTask" &&
            namedType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
        {
            return true;
        }

        return IsNonGenericTaskLikeBySyntax(returnType);
    }

    private static bool IsNonGenericTaskLikeBySyntax(TypeSyntax returnType) =>
        returnType switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text is "Task" or "ValueTask",
            QualifiedNameSyntax qualified => IsNonGenericTaskLikeBySyntax(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => IsNonGenericTaskLikeBySyntax(aliasQualified.Name),
            _ => false
        };

    private static IReadOnlyList<ClockReplacement> BuildExpressionBodyReplacements(
        ExpressionSyntax expression,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var existingNames = CollectExistingNames(memberAccess);
        var replacements = new List<ClockReplacement>();

        foreach (var access in FindExpressionBodyClockAccesses(expression, semanticModel, cancellationToken))
        {
            var symbol = semanticModel.GetSymbolInfo(access, cancellationToken).Symbol;
            if (symbol is null ||
                replacements.Any(replacement => SymbolEqualityComparer.Default.Equals(replacement.Symbol, symbol)))
            {
                continue;
            }

            var variableName = GetUniqueVariableName(existingNames);
            existingNames.Add(variableName);
            replacements.Add(new ClockReplacement(symbol, access, variableName));
        }

        return replacements;
    }

    private static ClockReplacement? FindReplacementFor(
        MemberAccessExpressionSyntax access,
        IEnumerable<ClockReplacement> replacements,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(access, cancellationToken).Symbol;
        return replacements.FirstOrDefault(replacement => SymbolEqualityComparer.Default.Equals(replacement.Symbol, symbol));
    }

    private static IEnumerable<MemberAccessExpressionSyntax> FindExpressionBodyClockAccesses(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        expression.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(access => IsClockPropertyAccess(access, semanticModel, cancellationToken) &&
                             !IsInsideStaticLambda(access) &&
                             IsInsideQueryableLambda(access, semanticModel, cancellationToken));

    private static bool IsInsideStaticLambda(SyntaxNode node) =>
        node.AncestorsAndSelf()
            .OfType<LambdaExpressionSyntax>()
            .Any(static lambda => lambda switch
            {
                ParenthesizedLambdaExpressionSyntax parenthesized => HasStaticModifier(parenthesized.Modifiers),
                SimpleLambdaExpressionSyntax simple => HasStaticModifier(simple.Modifiers),
                _ => false
            });

    private static bool HasStaticModifier(SyntaxTokenList modifiers) =>
        modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword));

    private static bool IsClockPropertyAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is not IPropertySymbol property)
            return false;

        if (property.Name is not ("Now" or "UtcNow"))
            return false;

        var containingType = property.ContainingType;
        return containingType.SpecialType == SpecialType.System_DateTime ||
               (containingType.Name == "DateTimeOffset" &&
                containingType.ContainingNamespace.ToDisplayString() == "System");
    }

    private static bool IsInsideQueryableLambda(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var lambda = node.AncestorsAndSelf().OfType<LambdaExpressionSyntax>().FirstOrDefault();
        if (lambda is null)
            return false;

        foreach (var argument in lambda.Ancestors().OfType<ArgumentSyntax>())
        {
            if (argument.Parent?.Parent is not InvocationExpressionSyntax invocation)
                continue;

            if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
                method.ContainingType.Name == "Queryable" &&
                method.ContainingNamespace.ToDisplayString() == "System.Linq")
            {
                return true;
            }
        }

        return false;
    }

    private static LocalDeclarationStatementSyntax CreateLocalDeclaration(
        MemberAccessExpressionSyntax memberAccess,
        string variableName) =>
        SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(
                        SyntaxFactory.Identifier(variableName),
                        null,
                        SyntaxFactory.EqualsValueClause(memberAccess.WithoutTrivia())
                    )
                )
            )
        );

    private static IEnumerable<MemberAccessExpressionSyntax> FindMatchingClockAccesses(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
        if (symbol == null)
        {
            yield return memberAccess;
            yield break;
        }

        var lambda = memberAccess.AncestorsAndSelf().OfType<LambdaExpressionSyntax>().FirstOrDefault();
        var searchRoot = (SyntaxNode?)lambda ?? memberAccess;

        foreach (var candidate in searchRoot.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var candidateSymbol = semanticModel.GetSymbolInfo(candidate, cancellationToken).Symbol;
            if (SymbolEqualityComparer.Default.Equals(candidateSymbol, symbol))
                yield return candidate;
        }
    }

    private static string GetUniqueVariableName(SyntaxNode node) =>
        GetUniqueVariableName(CollectExistingNames(node));

    private static string GetUniqueVariableName(HashSet<string> existingNames)
    {
        const string baseName = "now";
        if (!existingNames.Contains(baseName)) return baseName;

        for (var i = 1; i < 100; i++)
        {
            var candidate = baseName + i;
            if (!existingNames.Contains(candidate)) return candidate;
        }

        return baseName;
    }

    private static HashSet<string> CollectExistingNames(SyntaxNode node)
    {
        var existingNames = new HashSet<string>();
        var block = node.AncestorsAndSelf().OfType<BlockSyntax>().FirstOrDefault();
        if (block != null)
        {
            foreach (var descendant in block.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                existingNames.Add(descendant.Identifier.Text);
            }
        }

        AddEnclosingParameterNames(node, existingNames);
        return existingNames;
    }

    private static void AddEnclosingParameterNames(SyntaxNode node, HashSet<string> existingNames)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case BaseMethodDeclarationSyntax methodDeclaration:
                    AddParameterNames(methodDeclaration.ParameterList, existingNames);
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    AddParameterNames(localFunction.ParameterList, existingNames);
                    break;
                case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                    AddParameterNames(parenthesizedLambda.ParameterList, existingNames);
                    break;
                case SimpleLambdaExpressionSyntax simpleLambda:
                    existingNames.Add(simpleLambda.Parameter.Identifier.Text);
                    break;
                case AnonymousMethodExpressionSyntax anonymousMethod:
                    AddParameterNames(anonymousMethod.ParameterList, existingNames);
                    break;
            }
        }
    }

    private static void AddParameterNames(ParameterListSyntax? parameterList, HashSet<string> existingNames)
    {
        if (parameterList == null) return;

        foreach (var parameter in parameterList.Parameters)
        {
            existingNames.Add(parameter.Identifier.Text);
        }
    }

    private sealed class ClockReplacement
    {
        public ClockReplacement(ISymbol symbol, MemberAccessExpressionSyntax initializer, string variableName)
        {
            Symbol = symbol;
            Initializer = initializer;
            VariableName = variableName;
        }

        public ISymbol Symbol { get; }
        public MemberAccessExpressionSyntax Initializer { get; }
        public string VariableName { get; }
    }
}
