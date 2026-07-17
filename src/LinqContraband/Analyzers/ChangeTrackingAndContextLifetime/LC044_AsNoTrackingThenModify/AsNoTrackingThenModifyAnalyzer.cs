using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

/// <summary>
/// Detects entities loaded via AsNoTracking() that are mutated and then passed through SaveChanges
/// on the same context without a persistence-enabling tracking operation. Update / UpdateRange or
/// Entry.State = Modified / Added persist an existing mutation; Attach is sufficient only when it
/// happens before the mutation. EF silently persists nothing otherwise. Diagnostic ID: LC044.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class AsNoTrackingThenModifyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC044";
    private const string Category = "Reliability";

    private static readonly LocalizableString Title =
        "AsNoTracking query mutated then SaveChanges — silent data loss";

    private static readonly LocalizableString MessageFormat =
        "Entity '{0}' was loaded with AsNoTracking() and property '{1}' is mutated before SaveChanges — the change will not persist. Remove AsNoTracking(), call Update, or set Entry(entity).State = Modified before SaveChanges; Attach only helps before the mutation.";

    private static readonly LocalizableString Description =
        "EF Core does not track entities materialized from an AsNoTracking() query. Mutating a property of such an entity and then calling SaveChanges silently results in no database write. This rule flags the chain AsNoTracking-origin \u2192 property mutation \u2192 SaveChanges on the same context when no persistence-enabling tracking operation intervenes. Update / UpdateRange and Entry.State = Modified / Added persist an existing mutation; Attach / AttachRange are sufficient only before the mutation.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC044_AsNoTrackingThenModifySilentWrite.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeSaveChanges, OperationKind.Invocation);
    }

    private static void AnalyzeSaveChanges(OperationAnalysisContext context)
    {
        var save = (IInvocationOperation)context.Operation;
        var method = save.TargetMethod;

        if (method.Name != "SaveChanges" && method.Name != "SaveChangesAsync") return;
        if (!method.ContainingType.IsDbContext()) return;

        if (!AsNoTrackingThenModifyRootScan.TryGetSymbol(save.Instance, out var saveContextSymbol) ||
            saveContextSymbol == null) return;

        var root = save.FindOwningExecutableRoot();
        if (root == null) return;

        var scan = AsNoTrackingThenModifyRootScan.GetOrBuild(root, context.CancellationToken);
        var saveSpan = save.Syntax.SpanStart;
        var reported = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var decl in scan.InitializedDeclarators)
        {
            TryReportForLocal(
                context, root, save, saveContextSymbol, saveSpan, scan,
                decl.Symbol, decl.Syntax.SpanStart, decl.Initializer!.Value, reported);
        }

        foreach (var forEach in scan.ForEachLoops)
        {
            TryReportForForeach(context, root, save, saveContextSymbol, saveSpan, scan, forEach, reported);
        }
    }

}
