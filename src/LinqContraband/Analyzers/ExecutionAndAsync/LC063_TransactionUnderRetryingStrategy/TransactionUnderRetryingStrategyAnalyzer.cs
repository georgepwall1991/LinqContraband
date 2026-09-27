using System;
using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC063_TransactionUnderRetryingStrategy;

/// <summary>
/// Analyzes transactions the code starts itself (<c>BeginTransaction</c>, <c>BeginTransactionAsync</c>,
/// <c>UseTransaction</c>) on a context configured with a retrying execution strategy, outside a call to the strategy.
/// Diagnostic ID: LC063
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> a retrying execution strategy (<c>EnableRetryOnFailure()</c>, or a custom
/// <c>ExecutionStrategy</c>) cannot retry part of a transaction, so EF Core throws <c>InvalidOperationException</c>
/// ("The configured execution strategy ... does not support user-initiated transactions") on the first query or
/// <c>SaveChanges</c> inside it. Resiliency is often only turned on in production, so the failure first shows there. Run
/// the whole transaction as one retriable unit through <c>Database.CreateExecutionStrategy()</c>.</para>
/// </remarks>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class TransactionUnderRetryingStrategyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC063";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "User transaction under a retrying execution strategy";

    private static readonly LocalizableString MessageFormat =
        "'{0}' starts a transaction on '{1}', which uses a retrying execution strategy; EF Core throws on the first query or SaveChanges inside it unless the transaction runs through Database.CreateExecutionStrategy()";

    private static readonly LocalizableString Description =
        "A retrying execution strategy (EnableRetryOnFailure or a custom ExecutionStrategy) does not support transactions the code starts itself: the first query or SaveChanges inside one throws InvalidOperationException. Wrap the whole transaction in Database.CreateExecutionStrategy().Execute or ExecuteAsync so it retries as one unit.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC063_TransactionUnderRetryingStrategy.html");

    private const string FacadeExtensions = "RelationalDatabaseFacadeExtensions";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var compilation = context.Compilation;
        if (compilation.GetTypeByMetadataName(RetryingStrategyModel.EfNamespace + ".Infrastructure.DatabaseFacade") == null)
            return;

        var cancellationToken = context.CancellationToken;
        var model = new Lazy<RetryingStrategyModel>(
            () => RetryingStrategyModel.Build(compilation, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);

        context.RegisterOperationAction(ctx => AnalyzeInvocation(ctx, model), OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, Lazy<RetryingStrategyModel> model)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!TryGetTransactionContext(invocation, out var contextType))
            return;

        if (IsInsideStrategy(invocation, context.CancellationToken))
            return;

        var configuration = model.Value.FindConfiguration(contextType);
        if (configuration == null)
            return;

        var owner = FindOwningCallable(invocation, context.ContainingSymbol);
        if (owner != null && model.Value.IsCalledFromStrategy(owner))
            return;

        var location = invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.GetLocation()
            : invocation.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            location,
            ImmutableArray.Create(configuration),
            invocation.TargetMethod.Name,
            contextType.Name));
    }

    /// <summary>
    /// True for <c>ctx.Database.BeginTransaction(...)</c>, <c>BeginTransactionAsync(...)</c>, or
    /// <c>UseTransaction</c>/<c>UseTransactionAsync</c> with a transaction that is not the constant null, where
    /// <c>ctx</c> is statically a <c>DbContext</c>.
    /// </summary>
    internal static bool TryGetTransactionContext(IInvocationOperation invocation, out ITypeSymbol contextType)
    {
        contextType = null!;
        var method = invocation.TargetMethod;
        var isBegin = method.Name is "BeginTransaction" or "BeginTransactionAsync";
        if (!isBegin && method.Name is not ("UseTransaction" or "UseTransactionAsync"))
            return false;

        var containingType = method.ContainingType;
        var containingNamespace = containingType?.ContainingNamespace?.ToDisplayString();
        IOperation? facade = null;
        IOperation? transactionArgument = null;

        if (containingType?.Name == "DatabaseFacade" && containingNamespace == RetryingStrategyModel.EfNamespace + ".Infrastructure")
        {
            facade = invocation.Instance;
        }
        else if (containingType?.Name == FacadeExtensions && containingNamespace == RetryingStrategyModel.EfNamespace)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter?.Ordinal == 0)
                    facade = argument.Value;
                else if (argument.Parameter?.Ordinal == 1)
                    transactionArgument = argument.Value;
            }
        }

        if (facade == null)
            return false;

        if (!isBegin && (transactionArgument == null || transactionArgument.UnwrapConversions().ConstantValue is { HasValue: true, Value: null }))
            return false;

        if (facade.UnwrapConversions() is not IPropertyReferenceOperation { Property.Name: "Database", Instance: { } instance } ||
            instance.Type is not INamedTypeSymbol type ||
            !type.IsDbContext())
        {
            return false;
        }

        contextType = type;
        return true;
    }

    /// <summary>
    /// True when the transaction starts inside a lambda handed to an execution strategy (directly, or to a method in this
    /// project that wraps one), or inside a lambda the code stores rather than calls in place.
    /// </summary>
    private static bool IsInsideStrategy(IOperation operation, CancellationToken cancellationToken)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is not IAnonymousFunctionOperation lambda)
                continue;

            IOperation value = lambda;
            while (value.Parent is IDelegateCreationOperation or IConversionOperation)
                value = value.Parent;

            if (value.Parent is not IArgumentOperation { Parent: IInvocationOperation call })
                return true;

            if (RetryingStrategyModel.IsStrategyExecute(call.TargetMethod) ||
                IsProjectStrategyWrapper(call.TargetMethod, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A method declared in source whose declaration mentions an execution strategy, such as a resilient-transaction helper.</summary>
    private static bool IsProjectStrategyWrapper(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in (method.ReducedFrom ?? method).OriginalDefinition.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken).ToString().IndexOf("ExecutionStrategy", StringComparison.Ordinal) >= 0)
                return true;
        }

        return false;
    }

    private static ISymbol? FindOwningCallable(IOperation operation, ISymbol containingSymbol)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is ILocalFunctionOperation localFunction)
                return localFunction.Symbol;
            if (current is IAnonymousFunctionOperation)
                return null;
        }

        return containingSymbol as IMethodSymbol;
    }
}
