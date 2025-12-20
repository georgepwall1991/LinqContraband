using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

/// <summary>
/// Analyzes usage of AsNoTracking on entities that are subsequently passed to Update/Remove methods. Diagnostic ID: LC025
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsNoTrackingWithUpdateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC025";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Avoid AsNoTracking with Update/Remove";

    private static readonly LocalizableString MessageFormat =
        "Entity from an 'AsNoTracking' query is passed to '{0}'. This can lead to inefficient updates or tracking issues.";

    private static readonly LocalizableString Description =
        "Passing untracked entities to Update() causes EF Core to mark all properties as modified, leading to inefficient SQL. Remove AsNoTracking() if the entity will be modified.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC025_AsNoTrackingWithUpdate.md");

    private static readonly ImmutableHashSet<string> TrackingMethods = ImmutableHashSet.Create(
        "Update", "UpdateRange", "Remove", "RemoveRange"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
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

    private bool IsFromNoTrackingQuery(ILocalSymbol local, IOperation currentOperation)
    {
        // Find the top-most block or method body
        var root = currentOperation;
        while (root.Parent != null)
        {
            root = root.Parent;
        }

        var descendants = root.Descendants().ToList();
        var allOps = new List<IOperation>(descendants) { root };
        
        foreach (var op in allOps)
        {
            // 1. Standard Assignments
            if (op is ISimpleAssignmentOperation assignment && 
                assignment.Target is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
            {
                if (IsAsNoTrackingQuery(assignment.Value)) return true;
            }
            
            // 2. Variable Declarations
            if (op is IVariableDeclarationOperation decl)
            {
                foreach (var declarator in decl.Declarators)
                {
                    if (SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) && 
                        declarator.Initializer != null && 
                        IsAsNoTrackingQuery(declarator.Initializer.Value))
                    {
                        return true;
                    }
                }
            }

            // 3. Foreach Loops
            if (op is IForEachLoopOperation forEach)
            {
                // Check if our target 'local' is one of the locals defined by the loop
                if (forEach.Locals.Any(l => SymbolEqualityComparer.Default.Equals(l, local)))
                {
                    var collection = forEach.Collection.UnwrapConversions();
                    if (IsAsNoTrackingQuery(collection)) return true;

                    if (collection is ILocalReferenceOperation collRef)
                    {
                        // Check if collection variable itself is from a no-tracking query
                        if (IsFromNoTrackingQuery(collRef.Local, forEach)) return true;
                    }
                }
            }
        }

        return false;
    }

    private bool IsAsNoTrackingQuery(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        
        if (current is IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.Name.IsMaterializerMethod())
            {
                var receiver = invocation.GetInvocationReceiver();
                if (receiver != null) return HasAsNoTrackingInChain(receiver);
            }
            
            if (invocation.TargetMethod.Name == "AsNoTracking") return true;
        }

        return false;
    }

    private bool HasAsNoTrackingInChain(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        while (current is IInvocationOperation inv)
        {
            if (inv.TargetMethod.Name == "AsNoTracking") return true;
            
            var next = inv.GetInvocationReceiver();
            if (next == null) break;
            current = next.UnwrapConversions();
        }
        return false;
    }
}