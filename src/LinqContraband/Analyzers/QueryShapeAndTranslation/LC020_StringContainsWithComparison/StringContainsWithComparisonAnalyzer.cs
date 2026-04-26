using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC020_StringContainsWithComparison;

/// <summary>
/// Analyzes usage of string comparison overloads (Contains, StartsWith, EndsWith) in LINQ queries that might not be translatable to SQL. Diagnostic ID: LC020
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringContainsWithComparisonAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC020";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Avoid untranslatable string comparison overloads";

    private static readonly LocalizableString MessageFormat =
        "The method '{0}' with a StringComparison argument is used in a LINQ query. This often cannot be translated to SQL and may cause client-side evaluation.";

    private static readonly LocalizableString Description =
        "Using StringComparison overloads in LINQ to Entities queries often leads to translation failures or client-side evaluation. Use the simple overload instead.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC020_StringContainsWithComparison.md");

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "Contains", "StartsWith", "EndsWith"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.ContainingType.SpecialType != SpecialType.System_String) return;
        if (!TargetMethods.Contains(method.Name)) return;

        var hasStringComparison = invocation.Arguments.Any(IsStringComparisonArgument);

        if (!hasStringComparison) return;

        var lambdaParameters = GetQueryableExpressionLambdaParameters(invocation);
        if (lambdaParameters.Any(parameter => ReceiverDependsOnParameter(invocation.Instance, parameter)))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    private static bool IsStringComparisonArgument(IArgumentOperation argument)
    {
        var parameterType = argument.Parameter?.Type;
        var valueType = argument.Value.Type;

        return IsStringComparison(parameterType) || IsStringComparison(valueType);
    }

    private static bool IsStringComparison(ITypeSymbol? type)
    {
        return type?.Name == nameof(System.StringComparison) &&
               type.ContainingNamespace?.ToString() == "System";
    }

    private static ImmutableArray<IParameterSymbol> GetQueryableExpressionLambdaParameters(IOperation operation)
    {
        var builder = ImmutableArray.CreateBuilder<IParameterSymbol>();
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IAnonymousFunctionOperation anonymousFunction)
            {
                builder.AddRange(anonymousFunction.Symbol.Parameters);

                var lambdaParent = anonymousFunction.Parent;
                while (lambdaParent is IConversionOperation or IDelegateCreationOperation)
                {
                    lambdaParent = lambdaParent.Parent;
                }

                if (lambdaParent is IArgumentOperation { Parent: IInvocationOperation parentInvocation } &&
                    IsQueryableInvocation(parentInvocation))
                {
                    return builder.ToImmutable();
                }
            }

            current = current.Parent;
        }

        return ImmutableArray<IParameterSymbol>.Empty;
    }

    private static bool IsQueryableInvocation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.ContainingType.Name != "Queryable" ||
            method.ContainingNamespace?.ToString() != "System.Linq")
        {
            return false;
        }

        return invocation.GetInvocationReceiverType().IsIQueryable();
    }

    private static bool ReceiverDependsOnParameter(IOperation? operation, IParameterSymbol parameter)
    {
        if (operation is null) return false;

        operation = operation.UnwrapConversions();

        return operation switch
        {
            IParameterReferenceOperation parameterReference =>
                SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter),
            IPropertyReferenceOperation propertyReference =>
                ReceiverDependsOnParameter(propertyReference.Instance, parameter) ||
                propertyReference.Arguments.Any(argument => ReceiverDependsOnParameter(argument.Value, parameter)),
            IFieldReferenceOperation fieldReference =>
                ReceiverDependsOnParameter(fieldReference.Instance, parameter),
            IInvocationOperation invocation =>
                ReceiverDependsOnParameter(invocation.Instance, parameter) ||
                invocation.Arguments.Any(argument => ReceiverDependsOnParameter(argument.Value, parameter)),
            IBinaryOperation binary =>
                ReceiverDependsOnParameter(binary.LeftOperand, parameter) ||
                ReceiverDependsOnParameter(binary.RightOperand, parameter),
            ICoalesceOperation coalesce =>
                ReceiverDependsOnParameter(coalesce.Value, parameter) ||
                ReceiverDependsOnParameter(coalesce.WhenNull, parameter),
            IConditionalAccessOperation conditionalAccess =>
                ReceiverDependsOnParameter(conditionalAccess.Operation, parameter) ||
                ReceiverDependsOnParameter(conditionalAccess.WhenNotNull, parameter),
            IConditionalAccessInstanceOperation when operation.Parent?.Parent is IConditionalAccessOperation conditionalAccess =>
                ReceiverDependsOnParameter(conditionalAccess.Operation, parameter),
            _ => false
        };
    }
}
