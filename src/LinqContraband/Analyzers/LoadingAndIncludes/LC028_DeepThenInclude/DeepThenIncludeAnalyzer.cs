using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC028_DeepThenInclude;

/// <summary>
/// Detects ThenInclude chains deeper than 3 levels, which indicate over-fetching
/// and generate complex SQL with many LEFT JOINs. Diagnostic ID: LC028
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeepThenIncludeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC028";
    internal const string MaxDepthOptionKey = "dotnet_code_quality.LC028.max_depth";

    private const string Category = "Performance";
    private const int DefaultMaxDepth = 3;
    private static readonly LocalizableString Title = "Deep ThenInclude Chain";

    private static readonly LocalizableString MessageFormat =
        "ThenInclude chain is {0} levels deep (threshold: {1}). Consider using Select projection for deeply nested data.";

    private static readonly LocalizableString Description =
        "Deep ThenInclude chains generate complex SQL with many LEFT JOINs, degrading query performance. Consider using Select projection instead.";

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
        var maxDepthCache = new ConditionalWeakTable<SyntaxTree, StrongBox<int>>();
        context.RegisterOperationAction(operationContext => AnalyzeInvocation(operationContext, maxDepthCache), OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ConditionalWeakTable<SyntaxTree, StrongBox<int>> maxDepthCache)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // Only match ThenInclude from EF Core
        if (method.Name != "ThenInclude") return;
        if (!IsEfCoreMethod(method)) return;

        // Count ThenInclude depth by walking backward
        var depth = 1; // Current ThenInclude counts as 1
        var current = invocation.GetInvocationReceiver();

        while (current is IInvocationOperation prevInvocation)
        {
            var prevMethod = prevInvocation.TargetMethod;
            if (prevMethod.Name == "ThenInclude" && IsEfCoreMethod(prevMethod))
            {
                depth++;
                current = prevInvocation.GetInvocationReceiver();
            }
            else
            {
                break;
            }
        }

        var maxDepth = GetMaxDepth(context, maxDepthCache);
        if (depth == maxDepth + 1)
        {
            // Narrow the diagnostic to just the ".ThenInclude(...)" portion, excluding the receiver
            var location = invocation.Syntax.GetLocation();
            if (invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
            {
                // Span from the dot before ThenInclude through the closing paren
                var start = memberAccess.OperatorToken.SpanStart;
                var end = invocation.Syntax.Span.End;
                var textSpan = TextSpan.FromBounds(start, end);
                location = Location.Create(invocation.Syntax.SyntaxTree, textSpan);
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, depth, maxDepth));
        }
    }

    private static int GetMaxDepth(OperationAnalysisContext context, ConditionalWeakTable<SyntaxTree, StrongBox<int>> maxDepthCache)
    {
        var syntaxTree = context.Operation.Syntax.SyntaxTree;
        return maxDepthCache.GetValue(
            syntaxTree,
            tree => new StrongBox<int>(ReadMaxDepth(context.Options.AnalyzerConfigOptionsProvider, tree))).Value;
    }

    private static int ReadMaxDepth(AnalyzerConfigOptionsProvider optionsProvider, SyntaxTree syntaxTree)
    {
        var options = optionsProvider.GetOptions(syntaxTree);
        var maxDepth = DefaultMaxDepth;

        if (options.TryGetValue(MaxDepthOptionKey, out var value) &&
            int.TryParse(value, out var configuredMaxDepth) &&
            configuredMaxDepth > 0)
        {
            maxDepth = configuredMaxDepth;
        }

        return maxDepth;
    }

    private static bool IsEfCoreMethod(IMethodSymbol method)
    {
        return method.ContainingNamespace?.ToString()
            .StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) == true;
    }
}
