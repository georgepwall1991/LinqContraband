using System.Collections.Immutable;
using LinqContraband.Extensions;
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

        if (!IsCandidateMethod(methodSymbol, dbFunctionAttribute, projectableAttribute))
            return;

        if (!IsInsideQueryableLambda(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
    }

    private static bool IsCandidateMethod(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? dbFunctionAttribute,
        INamedTypeSymbol? projectableAttribute)
    {
        if (methodSymbol.MethodKind != MethodKind.Ordinary || methodSymbol.IsImplicitlyDeclared)
            return false;

        return !IsTrustedTranslatableMethod(methodSymbol, dbFunctionAttribute, projectableAttribute);
    }
}
