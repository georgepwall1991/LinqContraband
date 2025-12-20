using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC028_RedundantMaterialization;

/// <summary>
/// Analyzes IQueryable operations to detect redundant materialization calls. Diagnostic ID: LC028
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC028";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Redundant materialization call";

    private static readonly LocalizableString MessageFormat =
        "The method '{0}' is redundant because the sequence is already materialized or will be materialized immediately by '{1}'";

    private static readonly LocalizableString Description =
        "Redundant calls to materialization methods like ToList() or AsEnumerable() add unnecessary overhead and clutter code.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC028_RedundantMaterialization.md");

    private static readonly ImmutableHashSet<string> Materializers = ImmutableHashSet.Create(
        "ToList", "ToArray", "AsEnumerable", "ToDictionary", "ToHashSet",
        "ToListAsync", "ToArrayAsync", "ToDictionaryAsync", "ToHashSetAsync"
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

        if (!Materializers.Contains(method.Name)) return;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null) return;

        if (receiver.UnwrapConversions() is IInvocationOperation prevInvocation)
        {
            var prevMethod = prevInvocation.TargetMethod;
            if (Materializers.Contains(prevMethod.Name))
            {
                // Special case: AsEnumerable followed by ToList is redundant
                if (prevMethod.Name == "AsEnumerable")
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, GetMethodLocation(prevInvocation), prevMethod.Name, method.Name));
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, GetMethodLocation(invocation), method.Name, prevMethod.Name));
                }
            }
        }
    }

    private Location GetMethodLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax invocationSyntax &&
            invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.GetLocation();
        }
        return invocation.Syntax.GetLocation();
    }
}
