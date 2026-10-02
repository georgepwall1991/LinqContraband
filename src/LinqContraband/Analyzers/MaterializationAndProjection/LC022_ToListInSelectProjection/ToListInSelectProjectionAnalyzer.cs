using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC022_ToListInSelectProjection;

/// <summary>
/// Detects ToList/ToArray/ToDictionary/ToHashSet calls inside Select projections on IQueryable,
/// which can be expensive or provider-version sensitive. Diagnostic ID: LC022
/// </summary>
/// <remarks>
/// EF Core 8 and later with a relational provider drop a nested <c>ToList</c>, <c>ToArray</c> or <c>ToHashSet</c>
/// from the translation (the SQL is the same with or without it), so only <c>ToDictionary</c>, which EF Core cannot
/// translate and throws on, is reported there.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ToListInSelectProjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC022";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Nested collection materialization inside projection";

    private static readonly LocalizableString MessageFormat =
        "'{0}' inside a Select projection {1}";

    private const string ReviewReason =
        "can be expensive or provider-version sensitive. Consider projecting directly or using split queries.";

    private const string UntranslatableReason =
        "cannot be translated by EF Core and throws at run time. Project a list and build the dictionary after the query.";

    private static readonly LocalizableString Description =
        "Calling collection materializers (ToList, ToArray, etc.) inside a Select projection on IQueryable can be expensive or provider-version sensitive.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC022_ToListInSelectProjection.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var translatesNestedCollections = TranslatesNestedCollections(compilationContext.Compilation);
            // Only claim a translation failure on EF Core 3.0 or later, which throws instead of evaluating on the client.
            var dbContext = compilationContext.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            var referencesEfCore = dbContext?.ContainingAssembly.Identity.Version.Major >= 3;
            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, translatesNestedCollections, referencesEfCore),
                OperationKind.Invocation);
        });
    }

    /// <summary>
    /// EF Core 8 and later (checked on 8 and 10 with SQLite) translate a correlated collection projection to the same
    /// SQL whether or not it ends in <c>ToList</c>, <c>ToArray</c> or <c>ToHashSet</c>, and strip those calls from
    /// <c>c.Orders.ToList().Count</c> and similar chains. Older EF Core, or a compilation where the EF Core version
    /// cannot be read, keeps the advisory behavior. Correlated collections need EF Core's relational layer and no
    /// Cosmos provider.
    /// </summary>
    private static bool TranslatesNestedCollections(Compilation compilation)
    {
        var dbContext = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        return dbContext?.ContainingAssembly.Identity.Version.Major >= 8 &&
               compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions") != null &&
               compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.CosmosDbContextOptionsExtensions") == null;
    }

    private static bool IsDictionaryMaterializer(string methodName)
    {
        return methodName is "ToDictionary" or "ToDictionaryAsync";
    }

    private static bool IsCollectionMaterializer(string methodName)
    {
        return methodName is
            "ToList" or "ToListAsync" or
            "ToArray" or "ToArrayAsync" or
            "ToDictionary" or "ToDictionaryAsync" or
            "ToHashSet" or "ToHashSetAsync";
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, bool translatesNestedCollections, bool referencesEfCore)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsCollectionMaterializer(method.Name)) return;

        var isDictionary = IsDictionaryMaterializer(method.Name);
        // Only the synchronous ToList/ToArray/ToHashSet are stripped; async terminals return tasks.
        if (translatesNestedCollections && method.Name is ("ToList" or "ToArray" or "ToHashSet")) return;

        // Walk up to find if inside a lambda
        var parent = invocation.Parent;
        IAnonymousFunctionOperation? lambda = null;

        while (parent != null)
        {
            if (parent is IAnonymousFunctionOperation anon)
            {
                lambda = anon;
                break;
            }
            parent = parent.Parent;
        }

        if (lambda == null) return;

        // Check if the lambda is an argument to a Select call on IQueryable
        var lambdaParent = lambda.Parent;
        while (lambdaParent != null)
        {
            if (lambdaParent is IArgumentOperation)
            {
                lambdaParent = lambdaParent.Parent;
                continue;
            }

            if (lambdaParent is IInvocationOperation selectInvocation)
            {
                if (selectInvocation.TargetMethod.Name == "Select")
                {
                    var receiverType = selectInvocation.GetInvocationReceiverType();
                    var lambdaParameter = lambda.Symbol.Parameters.FirstOrDefault();
                    var materializerReceiver = invocation.GetInvocationReceiver();

                    if (receiverType.IsIQueryable() &&
                        !selectInvocation.GetInvocationReceiver().IsProvablyInMemoryQueryable() &&
                        lambdaParameter != null &&
                        materializerReceiver != null &&
                        materializerReceiver.ReferencesParameter(lambdaParameter))
                    {
                        if (IsGroupingQueryable(receiverType))
                            return;

                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                Rule,
                                invocation.Syntax.GetLocation(),
                                method.Name,
                                isDictionary && referencesEfCore ? UntranslatableReason : ReviewReason));
                    }
                }
                break;
            }

            // Also handle conversion/delegate creation operations that wrap the lambda
            if (lambdaParent is IConversionOperation or IDelegateCreationOperation)
            {
                lambdaParent = lambdaParent.Parent;
                continue;
            }

            break;
        }
    }

}
