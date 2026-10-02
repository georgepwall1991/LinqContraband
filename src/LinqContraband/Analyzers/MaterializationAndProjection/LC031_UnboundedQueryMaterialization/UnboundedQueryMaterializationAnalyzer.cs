using System;
using System.Collections.Immutable;
using LinqContraband.Catalog;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// Detects collection materializer calls (ToList, ToArray, etc.) on IQueryable chains originating from DbSet
/// without any bounding method (Take, First, Single, etc.), which risks loading unbounded data. Diagnostic ID: LC031
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class UnboundedQueryMaterializationAnalyzer : DiagnosticAnalyzer
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
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC031_UnboundedQueryMaterialization.html");

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
            "ToHashSet" or "ToHashSetAsync" or
            "ToLookup";
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

    private static bool IsInsideExpressionTree(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is not IAnonymousFunctionOperation)
                continue;

            if (current.Parent is { Type: INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } converted } conversion &&
                converted.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions" &&
                IsQueryOperatorArgument(conversion))
            {
                return true;
            }
        }

        return false;
    }

    // Only an expression tree handed to a query operator is translated; one held in a local or passed elsewhere can be
    // compiled and run in memory.
    private static bool IsQueryOperatorArgument(IOperation conversion)
    {
        if (conversion.Parent is not IArgumentOperation { Parent: IInvocationOperation { TargetMethod: var method } })
            return false;

        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        if (containingType.Name == "Queryable" && containingType.ContainingNamespace?.ToDisplayString() == "System.Linq")
            return true;

        var ns = containingType.ContainingNamespace?.ToDisplayString();
        return ns != null && (ns == "Microsoft.EntityFrameworkCore" || ns.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal));
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsCollectionMaterializer(method.Name)) return;

        // Inside an expression tree the call is part of the query EF Core translates; only the outer terminal runs it.
        if (IsInsideExpressionTree(invocation)) return;

        var querySource = ResolveQuerySource(invocation);
        if (querySource.FoundDbSet && !querySource.FoundBounding)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), querySource.DbSetName ?? "DbSet"));
        }
    }
}
