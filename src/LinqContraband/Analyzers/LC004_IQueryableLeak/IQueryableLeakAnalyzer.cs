using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class IQueryableLeakAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC004";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Deferred Execution Leak: IQueryable passed as IEnumerable";

    private static readonly LocalizableString MessageFormat =
        "Passing IQueryable '{0}' to parameter of type '{1}' causes implicit materialization. Change parameter to IQueryable or materialize explicitly.";

    private static readonly LocalizableString Description =
        "Passing an IQueryable to a method that only accepts IEnumerable causes the query to be implicitly materialized (executed) if the method iterates it. This prevents further query composition.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // Skip framework methods (e.g. System.Linq.Enumerable.ToList, String.Join, etc.)
        // These are "sinks" and it's safe/intended to pass IQueryable to them.
        if (method.IsFrameworkMethod()) return;

        // Iterate over arguments to find matching parameters
        foreach (var argument in invocation.Arguments)
        {
            // Check if argument corresponds to a parameter
            if (argument.Parameter == null) continue;

            var paramType = argument.Parameter.Type;
            var argValue = argument.Value;
            
            // Check if parameter is IEnumerable<T> but NOT IQueryable<T>
            if (!IsIEnumerable(paramType) || IsIQueryable(paramType)) continue;

            // We need to check if the underlying object being passed is IQueryable.
            // This requires peeling back conversions.
            if (IsSourceIQueryable(argValue))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, argument.Syntax.GetLocation(), 
                        argument.Parameter.Name, 
                        paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }

    private bool IsIEnumerable(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Collections_IEnumerable) return true;
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) return true;
        
        // Check by name for robustness (especially in tests/mocks)
        if (type.Name == "IEnumerable" && type.ContainingNamespace?.ToString() == "System.Collections.Generic") return true;
        if (type.Name == "IEnumerable" && type.ContainingNamespace?.ToString() == "System.Collections") return true;
        
        foreach (var i in type.AllInterfaces)
        {
             if (i.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T || 
                 i.SpecialType == SpecialType.System_Collections_IEnumerable) return true;
             
             if (i.Name == "IEnumerable" && i.ContainingNamespace?.ToString() == "System.Collections.Generic") return true;
        }
        
        return false;
    }

    private bool IsIQueryable(ITypeSymbol type)
    {
         return type.IsIQueryable();
    }

    private bool IsSourceIQueryable(IOperation operation)
    {
        var current = operation;
        
        // Walk back conversions to find the real source
        while (current is IConversionOperation conv)
        {
            current = conv.Operand;
        }

        // Check if the source type is IQueryable
        return current.Type != null && IsIQueryable(current.Type);
    }
}
