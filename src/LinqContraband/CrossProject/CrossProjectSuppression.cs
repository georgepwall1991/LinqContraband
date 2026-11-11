using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.CrossProject;

/// <summary>
/// Whether a rule is switched off where a call sits. The scanner raises cross-project findings itself, after the
/// build, so the compiler never gets the chance to apply <c>#pragma warning disable</c>, <c>[SuppressMessage]</c>,
/// <c>NoWarn</c> or an <c>.editorconfig</c> severity to them. The exporter applies the same switches here and
/// leaves the call out when the rule is off.
/// </summary>
internal static class CrossProjectSuppression
{
    /// <summary>The severity code a call site fact carries when the rule keeps its default severity.</summary>
    public const char DefaultSeverity = 'd';

    /// <summary>
    /// False when <paramref name="ruleId"/> is suppressed at <paramref name="node"/>. Otherwise
    /// <paramref name="severity"/> is the configured severity (<c>e</c>, <c>w</c>, <c>i</c>) or
    /// <see cref="DefaultSeverity"/>.
    /// </summary>
    public static bool TryGetSeverity(
        Compilation compilation,
        SyntaxNode node,
        ISymbol? containingSymbol,
        string ruleId,
        CancellationToken cancellationToken,
        out char severity)
    {
        severity = DefaultSeverity;
        var tree = node.SyntaxTree;

        var configured = ReportDiagnostic.Default;
        var provider = compilation.Options.SyntaxTreeOptionsProvider;
        if (provider == null ||
            (!provider.TryGetDiagnosticValue(tree, ruleId, cancellationToken, out configured) &&
             !provider.TryGetGlobalDiagnosticValue(ruleId, cancellationToken, out configured)))
        {
            configured = ReportDiagnostic.Default;
        }

        // NoWarn and /warnaserror for one id win over the config files, as they do in the compiler.
        if (compilation.Options.SpecificDiagnosticOptions.TryGetValue(ruleId, out var specific) &&
            (specific == ReportDiagnostic.Suppress || configured == ReportDiagnostic.Default))
        {
            configured = specific;
        }

        switch (configured)
        {
            case ReportDiagnostic.Suppress:
            case ReportDiagnostic.Hidden:
                return false;
            case ReportDiagnostic.Error:
                severity = 'e';
                break;
            case ReportDiagnostic.Warn:
                severity = 'w';
                break;
            case ReportDiagnostic.Info:
                severity = 'i';
                break;
        }

        return !IsDisabledByPragma(node, ruleId, cancellationToken) &&
               !IsSuppressedByAttribute(containingSymbol, ruleId);
    }

    /// <summary>
    /// The last <c>#pragma warning disable</c> or <c>restore</c> before the node that names the rule, or names no
    /// rule at all, decides.
    /// </summary>
    private static bool IsDisabledByPragma(SyntaxNode node, string ruleId, CancellationToken cancellationToken)
    {
        var root = node.SyntaxTree.GetRoot(cancellationToken);
        if (!root.ContainsDirectives)
            return false;

        var position = node.SpanStart;
        var disabled = false;
        for (var directive = root.GetFirstDirective(); directive != null; directive = directive.GetNextDirective())
        {
            if (directive.SpanStart >= position)
                break;

            if (directive is not PragmaWarningDirectiveTriviaSyntax pragma || !Names(pragma, ruleId))
                continue;

            disabled = pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword);
        }

        return disabled;
    }

    private static bool Names(PragmaWarningDirectiveTriviaSyntax pragma, string ruleId)
    {
        if (pragma.ErrorCodes.Count == 0)
            return true;

        foreach (var code in pragma.ErrorCodes)
        {
            if (code is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == ruleId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// <c>[SuppressMessage("...", "LC007")]</c> or <c>"LC007:Title"</c> on the member, an enclosing type, or the
    /// assembly or module without a narrower target.
    /// </summary>
    private static bool IsSuppressedByAttribute(ISymbol? symbol, string ruleId)
    {
        for (var current = symbol; current != null; current = current.ContainingSymbol)
        {
            if (current is INamespaceSymbol)
                continue;

            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.Name != "SuppressMessageAttribute" ||
                    attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[1].Value is not string checkId)
                {
                    continue;
                }

                if (checkId != ruleId && !checkId.StartsWith(ruleId + ":", System.StringComparison.Ordinal))
                    continue;

                // An assembly-level attribute with a Target suppresses only that target.
                if (current is IAssemblySymbol or IModuleSymbol && HasTarget(attribute))
                    continue;

                return true;
            }
        }

        return false;
    }

    private static bool HasTarget(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Target" && argument.Value.Value is string { Length: > 0 })
                return true;
        }

        return false;
    }
}
