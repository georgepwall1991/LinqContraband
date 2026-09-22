using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC049_IncludeIgnoredByProjection;

/// <summary>
/// Analyzes Include/ThenInclude calls that a later Select projection makes EF Core ignore. Diagnostic ID: LC049
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> EF Core applies Include only to entity instances the query returns. When a
/// Select projects the root entity into scalars, DTOs, or anonymous objects, the Include is silently dropped:
/// it loads nothing, and it misleads readers into thinking related data is eager-loaded. The projection
/// already controls which related columns are read, so the Include can be deleted.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class IncludeIgnoredByProjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC049";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Include is ignored by a Select projection";

    private static readonly LocalizableString MessageFormat =
        "'{0}' is ignored because the Select projection does not return '{1}' entities; remove it";

    private static readonly LocalizableString Description =
        "EF Core applies Include only to entities the query returns. A Select that projects the entity into scalars, DTOs, or anonymous types makes EF Core ignore the Include, so it loads nothing and misleads readers.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC049_IncludeIgnoredByProjection.html");

    internal const string FluentPropertyName = "Fluent";

    private static readonly ImmutableDictionary<string, string?> FluentProperties =
        ImmutableDictionary<string, string?>.Empty.Add(FluentPropertyName, "true");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var select = (IInvocationOperation)context.Operation;
        if (!IsQueryableSelect(select.TargetMethod)) return;
        if (select.Arguments.Length != 2) return;

        var lambda = TryGetSelector(select.Arguments[1].Value);
        if (lambda == null || lambda.Symbol.Parameters.Length == 0) return;

        var entityType = lambda.Symbol.Parameters[0].Type;
        if (!IsEntityLike(entityType)) return;
        if (ProjectionMayReturnEntities(lambda)) return;

        foreach (var include in CollectIgnoredIncludes(select.Arguments[0].Value, entityType))
        {
            var memberAccess = (include.Syntax as InvocationExpressionSyntax)?.Expression as MemberAccessExpressionSyntax;
            var location = memberAccess?.Name.GetLocation() ?? include.Syntax.GetLocation();

            // Only the fluent form `source.Include(...)` can be fixed by keeping the receiver; the static form
            // `EntityFrameworkQueryableExtensions.Include(source, ...)` is reported without a fix.
            var isFluent = memberAccess != null &&
                           include.Arguments.Length > 0 &&
                           include.Arguments[0].Value.Syntax == memberAccess.Expression;
            var properties = isFluent ? FluentProperties : ImmutableDictionary<string, string?>.Empty;

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, properties, include.TargetMethod.Name, entityType.Name));
        }
    }

    private static ImmutableArray<IInvocationOperation> CollectIgnoredIncludes(IOperation source, ITypeSymbol entityType)
    {
        var includes = ImmutableArray.CreateBuilder<IInvocationOperation>();
        var current = source.UnwrapConversions();

        while (current is IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            if (IsEfInclude(method))
            {
                if (method.TypeArguments.Length > 0 &&
                    SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], entityType))
                {
                    includes.Add(invocation);
                }
            }
            else if (!IsEntityPreservingOperator(method))
            {
                break;
            }

            var receiver = invocation.GetInvocationReceiver();
            if (receiver == null) break;
            current = receiver;
        }

        return includes.ToImmutable();
    }

    private static bool IsQueryableSelect(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return original.Name == "Select" &&
               original.ContainingType?.ToDisplayString() == "System.Linq.Queryable";
    }

    private static bool IsEfInclude(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return original.Name == "Include" &&
               original.ContainingType?.Name == "EntityFrameworkQueryableExtensions" &&
               original.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsEntityPreservingOperator(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        var containingType = original.ContainingType?.ToDisplayString();

        if (containingType == "System.Linq.Queryable")
        {
            return original.Name is "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or
                "Skip" or "Take" or "Distinct" or "Reverse";
        }

        if (original.ContainingType?.Name == "EntityFrameworkQueryableExtensions" &&
            original.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
        {
            return original.Name is "ThenInclude" or "AsNoTracking" or "AsNoTrackingWithIdentityResolution" or
                "AsTracking" or "TagWith" or "TagWithCallSite" or "IgnoreQueryFilters" or "IgnoreAutoIncludes" or
                "AsSplitQuery" or "AsSingleQuery";
        }

        if (original.ContainingType?.Name == "RelationalQueryableExtensions" &&
            original.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
        {
            return original.Name is "AsSplitQuery" or "AsSingleQuery";
        }

        return false;
    }

    private static IAnonymousFunctionOperation? TryGetSelector(IOperation argument)
    {
        var value = argument.UnwrapConversions();
        return value switch
        {
            IAnonymousFunctionOperation lambda => lambda,
            IDelegateCreationOperation { Target: IAnonymousFunctionOperation target } => target,
            _ => null
        };
    }
}
