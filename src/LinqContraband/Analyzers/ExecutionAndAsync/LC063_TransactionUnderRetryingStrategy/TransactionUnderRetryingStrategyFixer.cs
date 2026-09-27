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
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy;

/// <summary>
/// Provides code fixes for LC063. Runs the transaction through the context's execution strategy: the
/// <c>using</c>/<c>await using</c> declaration of the transaction and the rest of its block (or the <c>using</c>
/// statement that owns it) move into <c>strategy.Execute(() =&gt; { ... })</c>, or
/// <c>await strategy.ExecuteAsync(async () =&gt; { ... })</c> when the moved code awaits.
/// </summary>
/// <remarks>
/// Only the simple shape gets a fix: a <c>BeginTransaction</c>/<c>BeginTransactionAsync</c> call on
/// <c>ctx.Database</c> (where <c>ctx</c> is <c>this</c>, a readonly field, a non-virtual (or sealed-type) get-only
/// auto-property, or a parameter or local the method never writes) that initializes the single local
/// of a using declaration or using statement directly inside a block, with no <c>return</c>, <c>yield</c>, label or
/// <c>goto</c> in the moved code. The fixer compiles the rewritten document and only offers a rewrite that adds no
/// errors, which also rules out moved code that uses <c>ref</c>/<c>out</c> parameters, ref-like locals, jumps out of
/// the block, or locals read before they are assigned after it.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TransactionUnderRetryingStrategyFixer))]
[Shared]
public sealed class TransactionUnderRetryingStrategyFixer : CodeFixProvider
{
    private const string Title = "Run the transaction through the execution strategy";
    private const string StrategyTypeName = "Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(TransactionUnderRetryingStrategyAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        int? errorsBefore = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var invocation = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation == null) continue;

            var plan = Plan(invocation, semanticModel, cancellationToken);
            if (plan == null) continue;

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            editor.ReplaceNode(plan.Block, plan.NewBlock);
            editor.EnsureUsingForExtensionMethod(
                semanticModel,
                plan.Block.SpanStart,
                semanticModel.Compilation.GetTypeByMetadataName(StrategyTypeName),
                plan.Async ? "ExecuteAsync" : "Execute",
                "Microsoft.EntityFrameworkCore");
            var newDocument = editor.GetChangedDocument();
            newDocument = await Formatter.FormatAsync(newDocument, Formatter.Annotation, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            errorsBefore ??= CountErrors(semanticModel, cancellationToken);
            var newModel = await newDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (newModel == null || CountErrors(newModel, cancellationToken) > errorsBefore.Value) continue;

            context.RegisterCodeFix(
                CodeAction.Create(Title, _ => Task.FromResult(newDocument), nameof(TransactionUnderRetryingStrategyFixer)),
                diagnostic);
        }
    }

    private sealed class RewritePlan
    {
        public RewritePlan(BlockSyntax block, BlockSyntax newBlock, bool isAsync)
        {
            Block = block;
            NewBlock = newBlock;
            Async = isAsync;
        }

        public BlockSyntax Block { get; }
        public BlockSyntax NewBlock { get; }
        public bool Async { get; }
    }

    private static RewritePlan? Plan(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "BeginTransaction" or "BeginTransactionAsync",
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Database" } facade
            } ||
            !IsStableContext(facade.Expression, semanticModel, cancellationToken) ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            !TransactionUnderRetryingStrategyAnalyzer.TryGetTransactionContext(operation, out _))
        {
            return null;
        }

        // The transaction value, through an optional ConfigureAwait(...) and await.
        SyntaxNode value = invocation;
        if (value.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configureAwait &&
            configureAwait.Parent is InvocationExpressionSyntax configureAwaitCall)
        {
            value = configureAwaitCall;
        }

        if (value.Parent is AwaitExpressionSyntax awaitExpression)
            value = awaitExpression;

        if (value.Parent is not EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Variables.Count: 1 } declaration }
            })
        {
            return null;
        }

        BlockSyntax block;
        int start;
        int count;
        switch (declaration.Parent)
        {
            case LocalDeclarationStatementSyntax local
                when local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) && local.Parent is BlockSyntax localBlock:
                block = localBlock;
                start = localBlock.Statements.IndexOf(local);
                count = localBlock.Statements.Count - start;
                break;
            case UsingStatementSyntax usingStatement
                when usingStatement.Declaration == declaration && usingStatement.Parent is BlockSyntax usingBlock:
                block = usingBlock;
                start = usingBlock.Statements.IndexOf(usingStatement);
                count = 1;
                break;
            default:
                return null;
        }

        var moved = block.Statements.Skip(start).Take(count).ToList();
        if (moved.Any(ChangesControlFlow))
            return null;

        // Code that picks its resumption context (ConfigureAwait(false), or a value that is not the constant true)
        // cannot keep that choice across the new outer await without guessing, so it gets no fix.
        if (moved.Any(statement => ChoosesAwaitContext(statement, semanticModel, cancellationToken)))
            return null;

        var isAsync = moved.Any(ContainsAwait);
        if (isAsync && !IsInAsyncFunction(block))
            return null;

        var strategyName = PickStrategyName(block, semanticModel);
        var first = moved[0];

        var declarationStatement = SyntaxFactory.ParseStatement(
                "var " + strategyName + " = " + facade.WithoutTrivia().ToString() + ".CreateExecutionStrategy();")
            .WithLeadingTrivia(first.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var executeText = isAsync
            ? "await " + strategyName + ".ExecuteAsync(async () => { });"
            : strategyName + ".Execute(() => { });";
        var executeStatement = SyntaxFactory.ParseStatement(executeText);
        var placeholder = executeStatement.DescendantNodes().OfType<BlockSyntax>().First();
        var body = SyntaxFactory.Block(
            moved.Select((statement, index) => index == 0
                ? statement.WithLeadingTrivia(StripLeadingComments(statement.GetLeadingTrivia()))
                : statement));
        executeStatement = executeStatement.ReplaceNode(placeholder, body)
            .WithTrailingTrivia(moved[moved.Count - 1].GetTrailingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);

        var statements = block.Statements.Take(start)
            .Concat(new[] { declarationStatement, executeStatement })
            .Concat(block.Statements.Skip(start + count));

        return new RewritePlan(block, block.WithStatements(SyntaxFactory.List(statements)), isAsync);
    }

    // The fix reads the context twice (CreateExecutionStrategy() before the transaction, BeginTransaction inside the
    // delegate), so the receiver must not be able to change in between: this, a readonly field, a non-dispatching get-only
    // auto-property, or a parameter or non-ref local that the method never writes after declaring it.
    private static bool IsStableContext(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (expression is ThisExpressionSyntax)
            return true;

        if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax }))
            return false;

        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        switch (symbol)
        {
            case ILocalSymbol local:
                return local.RefKind == RefKind.None && !IsWrittenInMember(expression, local, semanticModel, cancellationToken);
            case IParameterSymbol parameter:
                return parameter.RefKind == RefKind.None && !IsWrittenInMember(expression, parameter, semanticModel, cancellationToken);
            case IFieldSymbol field:
                return field.IsReadOnly || field.IsConst;
            case IPropertySymbol property:
                // An override could compute a different context on each read, so the property must not dispatch.
                var canDispatch = property.IsVirtual || property.IsAbstract || property.IsOverride ||
                                  property.ContainingType.TypeKind == TypeKind.Interface;
                return property.SetMethod == null &&
                       (!canDispatch || (property.ContainingType.IsSealed && property.ContainingType.TypeKind != TypeKind.Interface)) &&
                       property.ContainingType.GetMembers().Any(member =>
                           member is IFieldSymbol field && SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
            default:
                return false;
        }
    }

    /// <summary>
    /// True when anything in the enclosing member (or the top-level statements) assigns, increments, deconstructs into,
    /// or passes by <c>ref</c>/<c>out</c> the given local or parameter.
    /// </summary>
    private static bool IsWrittenInMember(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var scope = (SyntaxNode?)expression.Ancestors()
                        .OfType<MemberDeclarationSyntax>()
                        .FirstOrDefault(member => member is not GlobalStatementSyntax)
                    ?? expression.SyntaxTree.GetRoot(cancellationToken);

        foreach (var name in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (name.Identifier.ValueText != symbol.Name ||
                !SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(name, cancellationToken).Symbol, symbol))
            {
                continue;
            }

            if (IsWritePosition(name))
                return true;
        }

        return false;
    }

    private static bool IsWritePosition(ExpressionSyntax name)
    {
        ExpressionSyntax current = name;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
            current = parenthesized;

        switch (current.Parent)
        {
            case AssignmentExpressionSyntax assignment when assignment.Left == current:
            case PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression):
            case PostfixUnaryExpressionSyntax:
            case RefExpressionSyntax:
            case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) &&
                                              !argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword):
                return true;
            case ArgumentSyntax { Parent: TupleExpressionSyntax tuple }:
                // (db, other) = (a, b): a tuple on the left of an assignment writes its elements.
                SyntaxNode outer = tuple;
                while (outer.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax nested })
                    outer = nested;
                return outer.Parent is AssignmentExpressionSyntax deconstruction && deconstruction.Left == outer;
            default:
                return false;
        }
    }

    private static IEnumerable<SyntaxNode> DescendantsOutsideFunctions(SyntaxNode node)
    {
        return node.DescendantNodesAndSelf(child =>
            child == node || child is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));
    }

    private static bool ChangesControlFlow(StatementSyntax statement)
    {
        return DescendantsOutsideFunctions(statement).Any(node =>
            node is ReturnStatementSyntax or YieldStatementSyntax or LabeledStatementSyntax or GotoStatementSyntax);
    }

    private static bool ChoosesAwaitContext(StatementSyntax statement, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var invocation in statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" })
                continue;

            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count != 1 ||
                semanticModel.GetConstantValue(arguments[0].Expression, cancellationToken) is not { HasValue: true, Value: true })
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAwait(StatementSyntax statement)
    {
        return DescendantsOutsideFunctions(statement).Any(node => node switch
        {
            AwaitExpressionSyntax => true,
            LocalDeclarationStatementSyntax local => local.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            UsingStatementSyntax usingStatement => usingStatement.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            CommonForEachStatementSyntax forEach => forEach.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            _ => false
        });
    }

    private static bool IsInAsyncFunction(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                    return lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>Comments above the transaction move to the strategy declaration; only the indentation stays.</summary>
    private static SyntaxTriviaList StripLeadingComments(SyntaxTriviaList trivia)
    {
        return SyntaxFactory.TriviaList(trivia.Reverse().TakeWhile(item => item.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse());
    }

    private static string PickStrategyName(BlockSyntax block, SemanticModel semanticModel)
    {
        // Names in scope at the block, plus every identifier inside it, so the new local neither hides nor is hidden by
        // another. Sibling blocks can reuse the name.
        var used = new HashSet<string>(
            block.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText));
        foreach (var symbol in semanticModel.LookupSymbols(block.SpanStart))
            used.Add(symbol.Name);

        foreach (var candidate in new[] { "strategy", "executionStrategy" })
        {
            if (!used.Contains(candidate))
                return candidate;
        }

        for (var suffix = 2;; suffix++)
        {
            var candidate = "strategy" + suffix;
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private static int CountErrors(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
