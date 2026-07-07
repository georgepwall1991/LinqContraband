using System.Collections.Immutable;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

/// <summary>
/// Analyzes local method calls within IQueryable expressions that cannot be translated to SQL. Diagnostic ID: LC001
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Methods invoked inside an IQueryable expression must be translatable to SQL by the query provider.
/// Local methods and custom user-defined methods cannot be translated, causing the entire query to be evaluated client-side,
/// which results in fetching all data from the database into memory before filtering. This defeats the purpose of using IQueryable
/// and can cause severe performance degradation and memory issues when working with large datasets.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class LocalMethodAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC001";
    private const string Category = "Performance";
    private const string DbFunctionAttributeMetadataName = "Microsoft.EntityFrameworkCore.DbFunctionAttribute";
    private const string ProjectableAttributeMetadataName = "EntityFrameworkCore.Projectables.ProjectableAttribute";
    private static readonly LocalizableString Title = "Client-side evaluation risk: Local method usage in IQueryable";

    private static readonly LocalizableString MessageFormat =
        "The method '{0}' cannot be translated to SQL and may cause client-side evaluation";

    private static readonly LocalizableString Description =
        "Methods invoked inside an IQueryable expression must be translatable to SQL.";

    private static readonly ImmutableHashSet<string> TranslationCriticalQueryMethods = ImmutableHashSet.Create(
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Join",
        "GroupJoin",
        "GroupBy",
        "Any",
        "All",
        "Count",
        "LongCount",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "Last",
        "LastOrDefault",
        "SkipWhile",
        "TakeWhile",
        // Aggregate operators with a selector: Queryable.Sum/Average/Min/Max(source, selector)
        // translate to SQL SUM/AVG/MIN/MAX(expr); a local/source method inside the selector cannot
        // translate and forces client evaluation (or throws), the exact LC001 smell.
        "Sum",
        "Average",
        "Min",
        "Max");

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC001_LocalMethod.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var dbFunctionAttribute = context.Compilation.GetTypeByMetadataName(DbFunctionAttributeMetadataName);
        var projectableAttribute = context.Compilation.GetTypeByMetadataName(ProjectableAttributeMetadataName);

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, dbFunctionAttribute, projectableAttribute),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol? dbFunctionAttribute,
        INamedTypeSymbol? projectableAttribute)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var methodSymbol = invocation.TargetMethod;

        // Constraint 3: Method defined in Source Code or untrusted library
        if (methodSymbol.MethodKind != MethodKind.Ordinary ||
            methodSymbol.IsImplicitlyDeclared)
            return;

        // Trust methods from specific namespaces known to be translatable
        if (IsTrustedTranslatableMethod(methodSymbol, dbFunctionAttribute, projectableAttribute))
            return;

        var parent = invocation.Parent;
        var lambdas = new List<IAnonymousFunctionOperation>();

        while (parent != null)
        {
            if (parent is IAnonymousFunctionOperation anonymousFunction)
            {
                lambdas.Add(anonymousFunction);
            }

            if (parent is IInvocationOperation queryInvocation &&
                IsTranslationCriticalQueryableInvocation(queryInvocation) &&
                lambdas.Count > 0 &&
                InvocationDependsOnLambdaParameter(invocation, lambdas[lambdas.Count - 1]))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
                return;
            }

            parent = parent.Parent;
        }
    }
}
