using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC022_ExplicitLoadingInLoop;

/// <summary>
/// Analyzes usage of explicit loading methods (Load, LoadAsync) inside loops, which causes N+1 query problems. Diagnostic ID: LC022
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitLoadingInLoopAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC022";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Avoid explicit loading in loops";

    private static readonly LocalizableString MessageFormat =
        "Method '{0}' is called inside a loop. This can cause N+1 database queries. Use eager loading with '.Include()' instead.";

    private static readonly LocalizableString Description =
        "Explicitly loading related entities (Load/LoadAsync) inside a loop results in multiple database round-trips. Eager loading is usually more efficient.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC022_ExplicitLoadingInLoop.md");

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "Load", "LoadAsync"
    );

    private static readonly ImmutableHashSet<string> TargetTypes = ImmutableHashSet.Create(
        "EntityEntry", "ReferenceEntry", "CollectionEntry",
        "ReferenceEntry`2", "CollectionEntry`2"
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

        if (!TargetMethods.Contains(method.Name)) return;

        // Check if it's called on an EF Core entry type
        if (!IsEfCoreEntryType(method.ContainingType)) return;

        // Check if we are inside a loop
        if (invocation.IsInsideLoop() || invocation.IsInsideAsyncForEach())
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    private bool IsEfCoreEntryType(ITypeSymbol? type)
    {
        if (type == null) return false;

        var ns = type.ContainingNamespace?.ToString();
        if (ns != "Microsoft.EntityFrameworkCore.ChangeTracking") return false;

        return TargetTypes.Contains(type.Name) || (type is INamedTypeSymbol genericType && TargetTypes.Contains(genericType.ConstructedFrom.Name));
    }
}
