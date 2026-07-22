using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ConcurrentDbContextOperationsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC046";
    private const string Category = "Safety";

    private static readonly LocalizableString Title =
        "Concurrent EF Core operations on the same DbContext";

    private static readonly LocalizableString MessageFormat =
        "DbContext '{0}' is used by concurrent EF Core operations; await operations sequentially or use a separate context per operation";

    private static readonly LocalizableString Description =
        "Entity Framework Core does not support multiple parallel operations on the same DbContext instance.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC046_ConcurrentDbContextOperations.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockAction(AnalyzeOperationBlock);
    }
}
