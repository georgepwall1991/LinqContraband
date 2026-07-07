using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC022_ToListInSelectProjection;

/// <summary>
/// Detects ToList/ToArray/ToDictionary/ToHashSet calls inside Select projections on IQueryable,
/// which can be expensive or provider-version sensitive. Diagnostic ID: LC022
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ToListInSelectProjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC022";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Nested collection materialization inside projection";

    private static readonly LocalizableString MessageFormat =
        "'{0}' inside a Select projection can be expensive. Consider projecting directly or using split queries.";

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
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC022_ToListInSelectProjection.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static bool IsCollectionMaterializer(string methodName)
    {
        return methodName is
            "ToList" or "ToListAsync" or
            "ToArray" or "ToArrayAsync" or
            "ToDictionary" or "ToDictionaryAsync" or
            "ToHashSet" or "ToHashSetAsync";
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsCollectionMaterializer(method.Name)) return;

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
                        lambdaParameter != null &&
                        materializerReceiver != null &&
                        materializerReceiver.ReferencesParameter(lambdaParameter))
                    {
                        if (IsGroupingQueryable(receiverType))
                            return;

                        context.ReportDiagnostic(
                            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
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
