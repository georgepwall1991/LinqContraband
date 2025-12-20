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
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC020_StringContainsWithComparison.md");

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

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.ContainingType.SpecialType != SpecialType.System_String) return;
        if (!TargetMethods.Contains(method.Name)) return;

        // Check if any argument is StringComparison
        var hasStringComparison = invocation.Arguments.Any(arg => 
            arg.Value.Type?.Name == "StringComparison" && 
            arg.Value.Type.ContainingNamespace?.ToString() == "System");

        if (!hasStringComparison) return;

        // Check if we are inside an IQueryable context
        if (IsInsideIQueryableExpression(invocation))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    private bool IsInsideIQueryableExpression(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            // If we hit an anonymous function (lambda), check if it's passed to an IQueryable method
            if (current is IAnonymousFunctionOperation or IDelegateCreationOperation)
            {
                var lambdaParent = current.Parent;
                // Unwrap potential argument/conversion
                while (lambdaParent is IArgumentOperation or IConversionOperation)
                {
                    lambdaParent = lambdaParent.Parent;
                }

                if (lambdaParent is IInvocationOperation parentInvocation)
                {
                    var parentMethod = parentInvocation.TargetMethod;
                    // Check if the method is called on IQueryable or returns IQueryable
                    if (parentInvocation.GetInvocationReceiverType().IsIQueryable() || 
                        parentMethod.ReturnType.IsIQueryable())
                    {
                        return true;
                    }
                }
            }
            
            current = current.Parent;
        }

        return false;
    }
}
