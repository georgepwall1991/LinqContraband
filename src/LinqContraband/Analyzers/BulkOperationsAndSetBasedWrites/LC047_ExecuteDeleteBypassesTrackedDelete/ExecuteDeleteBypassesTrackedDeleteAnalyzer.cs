using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

/// <summary>
/// Reports <c>ExecuteDelete</c>/<c>ExecuteDeleteAsync</c> when the owning context has a proven
/// tracked delete pipeline (SaveChanges conversion or client cascade) that SQL DELETE will skip.
/// Diagnostic ID: LC047
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ExecuteDeleteBypassesTrackedDeleteAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC047";
    public const string ConversionPropertyKey = "ConversionProperty";
    private const string Category = "Safety";

    private static readonly LocalizableString Title = "ExecuteDelete bypasses the tracked delete pipeline";

    private static readonly LocalizableString MessageFormat =
        "Call to '{0}' bypasses the tracked delete pipeline for '{1}'. This issues a SQL DELETE instead of the context's converted delete or client cascade.";

    private static readonly LocalizableString Description =
        "ExecuteDelete issues a SQL DELETE and does not run SaveChanges overrides, save interceptors, or client-cascade fix-up. When the context converts EntityState.Deleted into a soft-delete update, or a relationship uses ClientCascade/ClientSetNull, ExecuteDelete physically deletes rows or leaves orphans.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC047_ExecuteDeleteBypassesTrackedDelete.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var evidence = TrackedDeletePipelineEvidence.Get(context.Compilation, context.CancellationToken);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, evidence),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        TrackedDeletePipelineEvidence evidence)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsEntityFrameworkExecuteDelete(invocation.TargetMethod))
            return;

        if (!TryResolveDeleteTarget(invocation, context.CancellationToken, out var entityType, out var contextType))
            return;

        if (!evidence.IsCovered(contextType, entityType))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (evidence.TryGetSingleBoolTrueConversionProperty(contextType, entityType, out var propertyName) &&
            EntityHasAccessibleProperty(entityType, propertyName))
        {
            properties = properties.Add(ConversionPropertyKey, propertyName);
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                properties,
                invocation.TargetMethod.Name,
                entityType.Name));
    }

    internal static bool IsEntityFrameworkExecuteDelete(IMethodSymbol method)
    {
        if (method.Name is not ("ExecuteDelete" or "ExecuteDeleteAsync"))
            return false;

        return IsEntityFrameworkCoreNamespace(method.ContainingNamespace);
    }

    internal static bool IsEntityFrameworkCoreNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

    private static bool EntityHasAccessibleProperty(ITypeSymbol entityType, string propertyName)
    {
        for (var current = entityType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is IPropertySymbol)
                    return true;
            }
        }

        foreach (var iface in entityType.AllInterfaces)
        {
            foreach (var member in iface.GetMembers(propertyName))
            {
                if (member is IPropertySymbol)
                    return true;
            }
        }

        return false;
    }
}
