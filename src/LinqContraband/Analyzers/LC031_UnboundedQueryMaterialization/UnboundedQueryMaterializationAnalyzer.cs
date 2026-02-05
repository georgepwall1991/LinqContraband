using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// Detects collection materializer calls (ToList, ToArray, etc.) on IQueryable chains originating from DbSet
/// without any bounding method (Take, First, Single, etc.), which risks loading unbounded data. Diagnostic ID: LC031
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnboundedQueryMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC031";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Unbounded Query Materialization";

    private static readonly LocalizableString MessageFormat =
        "Query materializes from '{0}' without Take, First, or similar bounding. Consider adding Take(n) to prevent loading unbounded data.";

    private static readonly LocalizableString Description =
        "Materializing an IQueryable from a DbSet without Take, First, Single, or similar bounding risks loading unbounded data into memory.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description);

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

    private static bool IsBoundingMethod(string methodName)
    {
        return methodName is
            "Take" or "TakeWhile" or
            "First" or "FirstOrDefault" or "FirstAsync" or "FirstOrDefaultAsync" or
            "Single" or "SingleOrDefault" or "SingleAsync" or "SingleOrDefaultAsync" or
            "Last" or "LastOrDefault" or "LastAsync" or "LastOrDefaultAsync" or
            "Find" or "FindAsync";
    }

    private static bool IsAggregateMethod(string methodName)
    {
        return methodName is
            "Count" or "LongCount" or "CountAsync" or "LongCountAsync" or
            "Any" or "All" or "AnyAsync" or "AllAsync" or
            "Sum" or "Average" or "Min" or "Max" or
            "SumAsync" or "AverageAsync" or "MinAsync" or "MaxAsync" or
            "ExecuteDelete" or "ExecuteDeleteAsync" or
            "ExecuteUpdate" or "ExecuteUpdateAsync";
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsCollectionMaterializer(method.Name)) return;

        // Walk the receiver chain backward
        var foundDbSet = false;
        var foundBounding = false;
        string? dbSetName = null;
        var current = invocation.GetInvocationReceiver();

        while (current != null)
        {
            while (current is IConversionOperation conversion)
                current = conversion.Operand;

            if (current is IInvocationOperation prevInvocation)
            {
                var prevMethod = prevInvocation.TargetMethod;

                if (IsBoundingMethod(prevMethod.Name) || IsAggregateMethod(prevMethod.Name))
                {
                    foundBounding = true;
                    break;
                }

                current = prevInvocation.GetInvocationReceiver();
            }
            else if (current is IPropertyReferenceOperation propRef)
            {
                if (propRef.Type.IsDbSet())
                {
                    foundDbSet = true;
                    dbSetName = propRef.Property.Name;
                }
                break;
            }
            else if (current is IFieldReferenceOperation fieldRef)
            {
                if (fieldRef.Type.IsDbSet())
                {
                    foundDbSet = true;
                    dbSetName = fieldRef.Field.Name;
                }
                break;
            }
            else
            {
                if (current.Type.IsDbSet())
                {
                    foundDbSet = true;
                    dbSetName = current.Type?.Name ?? "DbSet";
                }
                break;
            }
        }

        if (foundDbSet && !foundBounding)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), dbSetName ?? "DbSet"));
        }
    }
}
