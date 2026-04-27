using System.Collections.Immutable;
using System.Collections.Generic;
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
public sealed class LocalMethodAnalyzer : DiagnosticAnalyzer
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
        "TakeWhile");

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

        // Constraint 3: Method defined in Source Code or untrusted library
        if (methodSymbol.MethodKind != MethodKind.Ordinary ||
            methodSymbol.IsImplicitlyDeclared)
            return;

        // Trust methods from specific namespaces known to be translatable
        if (IsTrustedTranslatableMethod(methodSymbol, dbFunctionAttribute, projectableAttribute))
            return;

        // Constraint 1: Inside a Lambda
        var parent = invocation.Parent;
        IAnonymousFunctionOperation? lambda = null;

        while (parent != null)
        {
            if (parent is IAnonymousFunctionOperation anonymousFunction)
            {
                lambda = anonymousFunction;
                break;
            }

            parent = parent.Parent;
        }

        if (lambda == null) return;
        if (!InvocationDependsOnLambdaParameter(invocation, lambda)) return;

        // Constraint 2: Lambda is argument to IQueryable extension method
        var current = lambda.Parent;
        while (current != null)
        {
            if (current is IInvocationOperation queryInvocation)
            {
                // Handle both extension syntax (Instance populated) and static call syntax (Instance null, use first arg)
                var type = queryInvocation.Instance?.Type;
                if (type == null && queryInvocation.Arguments.Length > 0)
                    type = queryInvocation.Arguments[0].Value.Type;

                if (type.IsIQueryable())
                {
                    if (!TranslationCriticalQueryMethods.Contains(queryInvocation.TargetMethod.Name))
                        return;

                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
                    return;
                }
            }

            current = current.Parent;
        }
    }

    private static bool InvocationDependsOnLambdaParameter(
        IInvocationOperation invocation,
        IAnonymousFunctionOperation lambda)
    {
        foreach (var parameter in lambda.Symbol.Parameters)
        {
            if (invocation.ReferencesParameter(parameter))
                return true;
        }

        return false;
    }

    private static bool IsTrustedTranslatableMethod(
        IMethodSymbol method,
        INamedTypeSymbol? dbFunctionAttribute,
        INamedTypeSymbol? projectableAttribute)
    {
        // System and Microsoft (Linq, EF Core base) are generally translatable
        if (method.IsFrameworkMethod()) return true;
        if (HasExplicitTranslationMarker(method, dbFunctionAttribute, projectableAttribute)) return true;

        var ns = method.ContainingNamespace?.ToString();
        if (ns == null) return false;

        // Specific database provider functions that are often used in IQueryable
        if (ns.StartsWith("Npgsql", System.StringComparison.Ordinal) ||
            ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) ||
            ns.StartsWith("NetTopologySuite", System.StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool HasExplicitTranslationMarker(
        IMethodSymbol method,
        INamedTypeSymbol? dbFunctionAttribute,
        INamedTypeSymbol? projectableAttribute)
    {
        if (dbFunctionAttribute == null && projectableAttribute == null) return false;

        foreach (var candidate in EnumerateMethodVariants(method))
        {
            foreach (var attribute in candidate.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null)
                    continue;

                if ((dbFunctionAttribute != null &&
                     SymbolEqualityComparer.Default.Equals(attributeClass, dbFunctionAttribute)) ||
                    (projectableAttribute != null &&
                     SymbolEqualityComparer.Default.Equals(attributeClass, projectableAttribute)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> EnumerateMethodVariants(IMethodSymbol method)
    {
        var pending = new Stack<IMethodSymbol>();
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        pending.Push(method);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
                continue;

            yield return current;

            if (current.ReducedFrom != null)
                pending.Push(current.ReducedFrom);

            if (!SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, current))
                pending.Push(current.OriginalDefinition);

            if (current.OverriddenMethod != null)
                pending.Push(current.OverriddenMethod);
        }
    }
}
