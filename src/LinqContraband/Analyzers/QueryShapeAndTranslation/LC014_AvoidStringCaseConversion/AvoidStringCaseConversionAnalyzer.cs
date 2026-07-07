using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC014_AvoidStringCaseConversion;

/// <summary>
/// Analyzes LINQ queries for use of ToLower() or ToUpper() string methods that prevent index usage. Diagnostic ID: LC014
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Using ToLower() or ToUpper() in query predicates transforms column values before comparison,
/// making the query non-sargable (Search ARGument ABLE). This prevents the database from using indexes and forces full table
/// scans. Instead, use database collation, a normalized column, or provider-specific collation support.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class AvoidStringCaseConversionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC014";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Avoid String.ToLower() or ToUpper() in LINQ queries";

    private static readonly LocalizableString MessageFormat =
        "Using '{0}' in a LINQ query prevents index usage. Use database collation, a normalized column, or EF.Functions.Collate instead.";

    private static readonly LocalizableString Description =
        "Using ToLower() or ToUpper() in a LINQ query predicate forces a full table scan (non-sargable) because it transforms the column value before comparison. Indexes cannot be used.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC014_AvoidStringCaseConversion.md");

    private static readonly HashSet<string> CaseConversionMethods = new()
    {
        "ToLower",
        "ToLowerInvariant",
        "ToUpper",
        "ToUpperInvariant"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // 1. Check if it is ToLower/ToUpper
        if (!CaseConversionMethods.Contains(method.Name)) return;

        // Check if it belongs to System.String
        if (method.ContainingType.SpecialType != SpecialType.System_String) return;

        // 2. Find enclosing IQueryable Lambda and get its parameters
        var lambdaParameters = GetEnclosingQueryableLambdaParameters(invocation);
        if (lambdaParameters.IsEmpty) return;

        // 3. Check if the receiver depends on one of the lambda parameters
        if (!ReceiverDependsOnParameter(invocation.Instance, lambdaParameters)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
    }

    // Whether a method argument of the given type can carry a column's text into the result, so
    // that a case conversion over that result would still touch column text. Reference types
    // (string, string[]/object[], collections, object) can; a `char`/`char?` argument contributes
    // a character (string.Concat(u.Name[0]), "x".Replace('x', u.MiddleInitial)); other value-type
    // arguments (int count/index, bool, an enum such as StringComparison) only control
    // position/format and cannot. A null/unknown type is followed conservatively.
    private static bool ArgumentCanCarryStringContent(ITypeSymbol? type)
    {
        if (type == null) return true;
        if (!type.IsValueType) return true;

        var underlying = type is INamedTypeSymbol named &&
                         named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? named.TypeArguments[0]
            : type;

        return underlying.SpecialType == SpecialType.System_Char;
    }

}
