using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AvoidDateTimeNowAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC016";
    private const string Category = "Performance";

    private static readonly LocalizableString Title = "Avoid DateTime.Now/UtcNow in LINQ queries";
    private static readonly LocalizableString MessageFormat = "Using '{0}' in a LINQ query prevents query caching and makes testing difficult. Pass the date as a variable.";
    private static readonly LocalizableString Description = "Using DateTime.Now or DateTime.UtcNow inside a LINQ query can prevent the database execution plan from being cached efficiently (as the constant value changes) and makes unit testing the query logic impossible without mocking the system clock. Store the value in a local variable before the query.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    private static readonly HashSet<string> TargetLinqMethods = new()
    {
        "Where",
        "OrderBy", "OrderByDescending",
        "ThenBy", "ThenByDescending",
        "Count", "LongCount",
        "Any", "All",
        "First", "FirstOrDefault",
        "Single", "SingleOrDefault",
        "Last", "LastOrDefault",
        "SkipWhile", "TakeWhile",
        "Select", "SelectMany", // Sometimes used in projection/filtering
        "Join", "GroupJoin"
    };

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
    }

    private void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var operation = (IPropertyReferenceOperation)context.Operation;
        var property = operation.Property;

        // Check for DateTime.Now, DateTime.UtcNow, DateTimeOffset.Now, DateTimeOffset.UtcNow
        if (property.Name is not ("Now" or "UtcNow")) return;

        var containingType = property.ContainingType;
        bool isDateTime = containingType.SpecialType == SpecialType.System_DateTime;
        bool isDateTimeOffset = !isDateTime && containingType.Name == "DateTimeOffset" && containingType.ContainingNamespace.ToString() == "System";

        if (!isDateTime && !isDateTimeOffset) return;

        // Check if inside IQueryable lambda
        if (!IsInsideQueryableLambda(operation)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation(), $"{containingType.Name}.{property.Name}"));
    }

    private bool IsInsideQueryableLambda(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IArgumentOperation argument)
            {
                // We found an argument. Check the invocation that owns it.
                if (argument.Parent is IInvocationOperation linqInvocation)
                {
                    var method = linqInvocation.TargetMethod;
                    
                    // Must be one of the target LINQ methods
                    if (TargetLinqMethods.Contains(method.Name))
                    {
                         // Must be on IQueryable
                         if (method.ContainingType.Name == "Queryable" &&
                             method.ContainingNamespace?.ToString() == "System.Linq")
                         {
                             return true;
                         }
                    }
                }
            }
            current = current.Parent;
        }
        return false;
    }
}
