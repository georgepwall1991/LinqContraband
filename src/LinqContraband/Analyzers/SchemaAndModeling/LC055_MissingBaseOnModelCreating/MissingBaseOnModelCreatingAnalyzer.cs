using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC055_MissingBaseOnModelCreating;

/// <summary>
/// Analyzes <c>OnModelCreating</c> overrides that never call <c>base.OnModelCreating</c> when the base context
/// configures the model. Diagnostic ID: LC055
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> contexts such as ASP.NET Core Identity's <c>IdentityDbContext</c> configure keys,
/// indexes, and relationships in their own <c>OnModelCreating</c>. An override that skips the base call drops all of
/// it, and the app fails at startup with errors like "The entity type 'IdentityUserLogin&lt;string&gt;' requires a
/// primary key to be defined", or runs with a silently incomplete model.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingBaseOnModelCreatingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC055";
    private const string Category = "Correctness";
    private const string MethodName = "OnModelCreating";
    private static readonly LocalizableString Title = "OnModelCreating override skips the base configuration";

    private static readonly LocalizableString MessageFormat =
        "'{0}.OnModelCreating' does not call base.OnModelCreating, so the model configuration in '{1}' is skipped";

    private static readonly LocalizableString Description =
        "Base contexts such as IdentityDbContext configure keys, indexes, and relationships in OnModelCreating. An override that never calls base.OnModelCreating(modelBuilder) drops that configuration, which usually fails at startup with a missing-key error. Call base.OnModelCreating(modelBuilder) first.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC055_MissingBaseOnModelCreating.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!method.IsOverride || method.IsAbstract || method.Name != MethodName || method.Parameters.Length != 1)
            return;

        var overridden = method.OverriddenMethod;
        if (overridden == null || overridden.IsAbstract || !IsModelBuilder(method.Parameters[0].Type))
            return;

        var containingType = method.ContainingType;
        if (!containingType.IsDbContext() || !BaseConfiguresModel(overridden, context.CancellationToken))
            return;

        // The base call may sit in a helper, so any base.OnModelCreating(...) in the type counts.
        if (TypeCallsBaseOnModelCreating(containingType, context.CancellationToken))
            return;

        var declaration = method.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(context.CancellationToken))
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(syntax => syntax.Body != null || syntax.ExpressionBody != null);
        if (declaration == null) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            declaration.Identifier.GetLocation(),
            containingType.Name,
            overridden.ContainingType.Name));
    }

    /// <summary>
    /// False only when the base implementation is provably empty: EF Core's own <c>DbContext.OnModelCreating</c>,
    /// or a source override whose body is empty or only forwards to its own empty base.
    /// </summary>
    private static bool BaseConfiguresModel(IMethodSymbol baseMethod, System.Threading.CancellationToken cancellationToken)
    {
        var current = baseMethod;
        for (var depth = 0; current != null && depth < 16; depth++)
        {
            if (current.ContainingType.Name == "DbContext" &&
                current.ContainingType.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore")
            {
                return false;
            }

            var declarations = current.DeclaringSyntaxReferences;
            if (declarations.Length != 1 || declarations[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
                return true;

            if (declaration.Body is { Statements.Count: 0 })
                return false;

            // Only forwards to its own base: keep walking.
            var forwards = declaration.Body is { Statements: { Count: 1 } statements }
                ? statements[0] is ExpressionStatementSyntax { Expression: var expression } && IsBaseOnModelCreatingCall(expression)
                : declaration.ExpressionBody != null && IsBaseOnModelCreatingCall(declaration.ExpressionBody.Expression);
            if (!forwards) return true;

            current = current.OverriddenMethod;
        }

        return true;
    }

    private static bool TypeCallsBaseOnModelCreating(INamedTypeSymbol type, System.Threading.CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            foreach (var access in reference.GetSyntax(cancellationToken).DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (access.Expression is BaseExpressionSyntax && access.Name.Identifier.ValueText == MethodName)
                    return true;
            }
        }

        return false;
    }

    private static bool IsBaseOnModelCreatingCall(ExpressionSyntax expression)
    {
        return expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax } access
        } && access.Name.Identifier.ValueText == MethodName;
    }

    private static bool IsModelBuilder(ITypeSymbol type)
    {
        return type.Name == "ModelBuilder" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }
}
