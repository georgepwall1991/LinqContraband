using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC058_TransactionScopeWithoutAsyncFlow;

/// <summary>
/// Analyzes <c>TransactionScope</c>s created without <c>TransactionScopeAsyncFlowOption.Enabled</c> whose lifetime
/// spans an <c>await</c>. Diagnostic ID: LC058
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> without async flow the ambient transaction lives in thread-local storage. After an
/// <c>await</c> resumes on another thread, <c>Transaction.Current</c> is gone, so EF Core commands run outside the
/// transaction, and disposing the scope on the new thread throws "A TransactionScope must be disposed on the same
/// thread that it was created".</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransactionScopeWithoutAsyncFlowAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC058";
    private const string Category = "Reliability";
    private const int AsyncFlowEnabled = 1;
    private static readonly LocalizableString Title = "TransactionScope without async flow spans an await";

    private static readonly LocalizableString MessageFormat =
        "This TransactionScope stays open across an await without TransactionScopeAsyncFlowOption.Enabled; the ambient transaction does not flow to the code after the await";

    private static readonly LocalizableString Description =
        "Without TransactionScopeAsyncFlowOption.Enabled, a TransactionScope keeps the ambient transaction on the current thread. Code after an await can resume on another thread, where EF Core does not see the transaction and disposing the scope throws. Pass TransactionScopeAsyncFlowOption.Enabled.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC058_TransactionScopeWithoutAsyncFlow.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (!IsTransactionScope(creation.Type) || creation.Constructor == null)
            return;

        if (!LacksAsyncFlow(creation))
            return;

        var root = creation.FindOwningExecutableRoot();
        if (root == null || !IsAsync(root))
            return;

        if (!TryGetLifetimeSpan(creation, out var lifetime))
            return;

        if (!ContainsAwait(root, lifetime))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation()));
    }

    internal static bool IsTransactionScope(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "TransactionScope" } named &&
               named.ContainingNamespace?.ToDisplayString() == "System.Transactions";
    }

    internal static bool IsAsyncFlowOptionType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "TransactionScopeAsyncFlowOption", TypeKind: TypeKind.Enum } named &&
               named.ContainingNamespace?.ToDisplayString() == "System.Transactions";
    }

    /// <summary>
    /// True when the constructor takes no <c>TransactionScopeAsyncFlowOption</c>, or is passed the constant
    /// <c>Suppress</c>. A non-constant option is unknown, so it does not count.
    /// </summary>
    private static bool LacksAsyncFlow(IObjectCreationOperation creation)
    {
        foreach (var argument in creation.Arguments)
        {
            if (argument.Parameter == null || !IsAsyncFlowOptionType(argument.Parameter.Type))
                continue;

            var constant = argument.Value.ConstantValue;
            return constant.HasValue && constant.Value is int value && value != AsyncFlowEnabled;
        }

        return true;
    }

    private static bool IsAsync(IOperation root)
    {
        return root switch
        {
            IAnonymousFunctionOperation lambda => lambda.Symbol.IsAsync,
            ILocalFunctionOperation localFunction => localFunction.Symbol.IsAsync,
            _ => root.SemanticModel?.GetDeclaredSymbol(root.Syntax) is IMethodSymbol { IsAsync: true }
        };
    }

    /// <summary>
    /// The code that runs while the scope is alive: the body of <c>using (new TransactionScope())</c>, or the rest of
    /// the block after <c>using var scope = new TransactionScope();</c>. A scope that is not owned by a using is
    /// not followed.
    /// </summary>
    private static bool TryGetLifetimeSpan(IObjectCreationOperation creation, out TextSpan lifetime)
    {
        lifetime = default;
        var node = creation.Syntax;
        var parent = node.Parent;
        while (parent is ParenthesizedExpressionSyntax)
        {
            node = parent;
            parent = parent.Parent;
        }

        if (parent is UsingStatementSyntax directUsing && directUsing.Expression == node)
        {
            lifetime = directUsing.Statement.Span;
            return true;
        }

        if (parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } })
            return false;

        switch (declaration.Parent)
        {
            case UsingStatementSyntax usingStatement:
                lifetime = usingStatement.Statement.Span;
                return true;

            case LocalDeclarationStatementSyntax local when local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword):
            {
                SyntaxNode? container = local.Parent;
                if (container is not (BlockSyntax or SwitchSectionSyntax))
                    return false;

                lifetime = TextSpan.FromBounds(local.Span.End, container.Span.End);
                return true;
            }

            default:
                return false;
        }
    }

    private static bool ContainsAwait(IOperation root, TextSpan lifetime)
    {
        foreach (var descendant in root.Descendants())
        {
            var isAwait = descendant switch
            {
                IAwaitOperation => true,
                IForEachLoopOperation { IsAsynchronous: true } => true,
                IUsingOperation { IsAsynchronous: true } => true,
                IUsingDeclarationOperation { IsAsynchronous: true } => true,
                _ => false
            };

            if (isAwait &&
                lifetime.Contains(descendant.Syntax.SpanStart) &&
                ReferenceEquals(descendant.FindOwningExecutableRoot(), root))
            {
                return true;
            }
        }

        return false;
    }
}
