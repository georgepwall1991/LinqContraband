using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

/// <summary>
/// EF Core ships its own raw-SQL analyzers in the Microsoft.EntityFrameworkCore package:
/// EF1002 (EF Core 8+) reports an interpolated string with a runtime hole passed straight to
/// FromSqlRaw, ExecuteSqlRaw, ExecuteSqlRawAsync or SqlQueryRaw, and EF1003 (EF Core 10+) reports
/// a non-constant concatenation there. Both come with EF's own FromSql/ExecuteSql/SqlQuery fix.
/// LC018 and LC034 defer to them so one line does not carry two warnings for the same injection.
/// </summary>
public static class EfCoreRawSqlAnalyzerOverlap
{
    private const string RelationalAssemblyName = "Microsoft.EntityFrameworkCore.Relational";

    /// <summary>
    /// True when EF Core's own analyzer reports this exact call. The check mirrors what EF1002 and
    /// EF1003 inspect: the relational extension method in the real EF assembly, and the SQL in its
    /// positional slot (a reordered named <c>sql:</c> argument escapes EF's analyzer, so it stays ours).
    /// </summary>
    public static bool IsReportedByEfCoreAnalyzer(IInvocationOperation invocation, IArgumentOperation sqlArgument)
    {
        var method = invocation.TargetMethod;
        var containingType = method.ContainingType;
        if (containingType is null ||
            containingType.ContainingNamespace?.ToDisplayString() != "Microsoft.EntityFrameworkCore")
            return false;

        var expectedTypeName = method.Name == "FromSqlRaw"
            ? "RelationalQueryableExtensions"
            : "RelationalDatabaseFacadeExtensions";
        if (containingType.Name != expectedTypeName)
            return false;

        var assembly = containingType.ContainingAssembly?.Identity;
        if (assembly?.Name != RelationalAssemblyName)
            return false;

        if (invocation.Arguments.Length < 2 || !ReferenceEquals(invocation.Arguments[1], sqlArgument))
            return false;

        var sql = sqlArgument.Value;
        if (sql.ConstantValue.HasValue)
            return false;

        return sql switch
        {
            IInterpolatedStringOperation => assembly.Version.Major >= 8,
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } => assembly.Version.Major >= 10,
            _ => false
        };
    }

    /// <summary>
    /// Reads <c>dotnet_code_quality.&lt;ruleId&gt;.defer_to_ef_analyzers</c>. Defaults to true; set it
    /// to false when EF Core's analyzers are excluded or EF1002/EF1003 are disabled.
    /// </summary>
    public static bool DefersToEfCoreAnalyzers(AnalyzerOptions options, SyntaxTree syntaxTree, string ruleId)
    {
        var configOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
        return !configOptions.TryGetValue("dotnet_code_quality." + ruleId + ".defer_to_ef_analyzers", out var value) ||
               !bool.TryParse(value, out var defer) ||
               defer;
    }

    public static bool IsObsolete(this ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "ObsoleteAttribute" } attributeClass &&
                attributeClass.ContainingNamespace?.ToDisplayString() == "System")
                return true;
        }

        return false;
    }
}
