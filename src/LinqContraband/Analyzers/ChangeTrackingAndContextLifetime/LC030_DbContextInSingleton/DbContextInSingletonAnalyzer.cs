using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

/// <summary>
/// Analyzes class members to detect DbContext instances held in potentially singleton or long-lived services. Diagnostic ID: LC030
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class DbContextInSingletonAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC030";
    private const string Category = "Architecture";
    private const string DetectionModeKey = "dotnet_code_quality.LC030.detection_mode";
    private const string LongLivedTypesKey = "dotnet_code_quality.LC030.long_lived_types";
    private static readonly LocalizableString Title = "Potential DbContext lifetime mismatch";

    private static readonly LocalizableString MessageFormat =
        "The {0} '{1}' uses a DbContext with a long-lived lifetime for '{2}' ({3}). Review the lifetime and prefer IDbContextFactory<TContext> or a scoped component.";

    private static readonly LocalizableString Description =
        "DbContext is not thread-safe and is intended to be short-lived. Storing it on a long-lived type can be risky, so review the service lifetime and prefer factories when the lifetime is uncertain.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC030_DbContextInSingleton.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var candidates = new ConcurrentBag<DbContextCandidate>();
            var longLivedTypes = new ConcurrentDictionary<INamedTypeSymbol, string>(NamedTypeSymbolComparer.Instance);

            compilationContext.RegisterSymbolAction(
                context => AnalyzeField(context, candidates, longLivedTypes),
                SymbolKind.Field);

            compilationContext.RegisterSyntaxNodeAction(
                context => AnalyzeProperty(context, candidates, longLivedTypes),
                SyntaxKind.PropertyDeclaration);

            compilationContext.RegisterSymbolAction(
                context => AnalyzeConstructor(context, candidates, longLivedTypes),
                SymbolKind.Method);

            compilationContext.RegisterOperationAction(
                context => AnalyzeInvocation(context, longLivedTypes),
                OperationKind.Invocation);

            compilationContext.RegisterCompilationEndAction(context =>
                ReportCandidateDiagnostics(context, candidates, longLivedTypes));
        });
    }

}
