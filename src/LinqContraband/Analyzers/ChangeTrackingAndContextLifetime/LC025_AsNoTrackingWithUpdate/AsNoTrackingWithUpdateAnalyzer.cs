using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

/// <summary>
/// Analyzes usage of AsNoTracking on entities that are subsequently passed to Update/Remove methods. Diagnostic ID: LC025
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class AsNoTrackingWithUpdateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC025";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Avoid AsNoTracking with Update/Remove";

    private static readonly LocalizableString MessageFormat =
        "Entity from an 'AsNoTracking' query is passed to '{0}'. This can lead to inefficient updates or tracking issues.";

    private static readonly LocalizableString Description =
        "Passing untracked entities to Update() causes EF Core to mark all properties as modified, leading to inefficient SQL. Remove AsNoTracking() if the entity will be modified.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC025_AsNoTrackingWithUpdate.md");

    private static readonly ImmutableHashSet<string> TrackingMethods = ImmutableHashSet.Create(
        "Update", "UpdateRange", "Remove", "RemoveRange"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeSimpleAssignment, OperationKind.SimpleAssignment);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!TrackingMethods.Contains(method.Name)) return;

        // Verify it's an EF Core method (on DbContext or DbSet)
        if (!method.ContainingType.IsDbContext() && !method.ContainingType.IsDbSet()) return;

        // Check each entity argument
        foreach (var arg in invocation.Arguments)
        {
            var value = arg.Value.UnwrapConversions();

            // If it's a local variable reference
            if (value is ILocalReferenceOperation localRef)
            {
                if (IsFromNoTrackingQuery(localRef.Local, invocation))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, arg.Syntax.GetLocation(), method.Name));
                }
            }
        }
    }

    private void AnalyzeSimpleAssignment(OperationAnalysisContext context)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (!TryParseEntryStateWrite(assignment, out var entityLocal, out var entityOperation, out var stateName))
            return;

        if (IsFromNoTrackingQuery(entityLocal, assignment))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, entityOperation.Syntax.GetLocation(), $"Entry.State = {stateName}"));
        }
    }
}
