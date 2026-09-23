using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using LinqContraband.Catalog;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC053_OverwrittenQueryFilter;

/// <summary>
/// Reports an entity type with more than one unnamed <c>HasQueryFilter</c> call. EF Core keeps only the last unnamed
/// filter, so the others (often a tenant or soft-delete filter set in another configuration class) are dropped with no
/// error. EF Core 10 also throws when unnamed and named filters are mixed on one entity type.
/// </summary>
/// <remarks>
/// Conflicts inside one member are reported as the member is analyzed, so they show up while editing and can be fixed.
/// Conflicts between members or classes need the whole compilation and are reported when it completes (build time).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OverwrittenQueryFilterAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC053";
    private const string Category = "Security";

    internal const string OtherFilterCountProperty = "OtherFilterCount";

    internal const string MixedMessage =
        "an unnamed HasQueryFilter is mixed with named filters, which EF Core 10 rejects when it builds the model. Name this filter too.";

    private static readonly LocalizableString Title = "Global query filter silently replaced by another HasQueryFilter";

    private static readonly LocalizableString MessageFormat = "Query filters on '{0}' conflict: {1}";

    private static readonly LocalizableString Description =
        "An entity type has one unnamed global query filter. Calling HasQueryFilter again replaces it instead of adding to it, so a tenant or soft-delete filter configured elsewhere stops applying.";

    private static readonly string HelpLink = RuleCatalog.DocumentationSiteUri + "LC053_OverwrittenQueryFilter.html";

    /// <summary>Filters that conflict inside one member.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description,
        helpLinkUri: HelpLink);

    /// <summary>Filters that conflict across members or classes; found once the whole compilation is seen.</summary>
    public static readonly DiagnosticDescriptor CrossMemberRule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description,
        helpLinkUri: HelpLink,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule, CrossMemberRule);

    internal static string OverwrittenMessage(int count) =>
        $"there are {count} unnamed HasQueryFilter calls, and EF Core keeps only the last one, silently dropping the others. Combine them with && or give each a name (EF Core 10+).";

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var allFilters = new ConcurrentBag<FilterCall>();
            compilationContext.RegisterOperationBlockStartAction(blockContext =>
            {
                var blockFilters = new List<FilterCall>();
                var owner = blockContext.OwningSymbol;
                blockContext.RegisterOperationAction(
                    operationContext =>
                    {
                        if (!TryCreateFilter((IInvocationOperation)operationContext.Operation, owner, out var filter))
                            return;

                        lock (blockFilters)
                            blockFilters.Add(filter);
                        allFilters.Add(filter);
                    },
                    OperationKind.Invocation);
                blockContext.RegisterOperationBlockEndAction(endContext =>
                {
                    lock (blockFilters)
                        ReportWithinMember(endContext, blockFilters);
                });
            });
            compilationContext.RegisterCompilationEndAction(endContext => ReportAcrossMembers(endContext, allFilters));
        });
    }

    private static bool TryCreateFilter(IInvocationOperation invocation, ISymbol owner, out FilterCall filter)
    {
        filter = null!;
        var method = invocation.TargetMethod;
        if (method.Name != "HasQueryFilter" ||
            method.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore.Metadata.Builders" ||
            method.ContainingType is not { Name: "EntityTypeBuilder", TypeArguments.Length: 1 } builderType ||
            method.Parameters.Length == 0)
        {
            return false;
        }

        // Filters built in a loop over model metadata or passed as data cannot be tied to one entity statically.
        var filterParameter = method.Parameters[method.Parameters.Length - 1];
        var named = method.Parameters.Length == 2 && method.Parameters[0].Type.SpecialType == SpecialType.System_String;
        if (!named && method.Parameters.Length != 1)
            return false;

        var filterArgument = invocation.Arguments.FirstOrDefault(argument =>
            SymbolEqualityComparer.Default.Equals(argument.Parameter, filterParameter));
        if (!named && (filterArgument == null || IsNullFilter(filterArgument.Value)))
            return false;

        filter = new FilterCall(builderType.TypeArguments[0], owner, invocation.Syntax, named, GetLocation(invocation));
        return true;
    }

    private static bool IsNullFilter(IOperation value)
    {
        var current = value;
        while (current is IConversionOperation conversion)
            current = conversion.Operand;

        return current is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } } or IDefaultValueOperation;
    }

    private static void ReportWithinMember(OperationBlockAnalysisContext context, List<FilterCall> filters)
    {
        foreach (var group in filters.GroupBy(filter => filter.EntityType, SymbolEqualityComparer.Default))
        {
            var entityName = group.Key!.Name;
            var unnamed = group.Where(call => !call.Named).ToList();
            if (unnamed.Count == 0)
                continue;

            if (group.Any(call => call.Named))
            {
                foreach (var call in unnamed)
                    context.ReportDiagnostic(Diagnostic.Create(Rule, call.Location, entityName, MixedMessage));

                continue;
            }

            foreach (var call in unnamed)
            {
                var others = NonExclusiveOthers(call, unnamed);
                if (others.Count == 0)
                    continue;

                var properties = ImmutableDictionary<string, string?>.Empty
                    .Add(OtherFilterCountProperty, others.Count.ToString(CultureInfo.InvariantCulture));
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    call.Location,
                    others.Select(other => other.Location),
                    properties,
                    entityName,
                    OverwrittenMessage(others.Count + 1)));
            }
        }
    }

    /// <summary>
    /// Reports only conflicts that involve another member; conflicts inside one member were reported with it.
    /// </summary>
    private static void ReportAcrossMembers(CompilationAnalysisContext context, ConcurrentBag<FilterCall> filters)
    {
        foreach (var group in filters.GroupBy(filter => filter.EntityType, SymbolEqualityComparer.Default))
        {
            var entityName = group.Key!.Name;
            var calls = group.ToList();
            var unnamed = calls.Where(call => !call.Named).ToList();
            if (unnamed.Count == 0)
                continue;

            if (calls.Any(call => call.Named))
            {
                foreach (var call in unnamed)
                {
                    if (calls.Any(other => other.Named && !call.SameOwner(other)) &&
                        !calls.Any(other => other.Named && call.SameOwner(other)))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(CrossMemberRule, call.Location, entityName, MixedMessage));
                    }
                }

                continue;
            }

            foreach (var call in unnamed)
            {
                var others = NonExclusiveOthers(call, unnamed);
                if (!others.Any(other => !call.SameOwner(other)))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    CrossMemberRule,
                    call.Location,
                    others.Select(other => other.Location),
                    entityName,
                    OverwrittenMessage(others.Count + 1)));
            }
        }
    }

    private static List<FilterCall> NonExclusiveOthers(FilterCall call, List<FilterCall> unnamed)
    {
        return unnamed
            .Where(other => other != call && !AreMutuallyExclusive(call.Syntax, other.Syntax))
            .OrderBy(other => other.Location.SourceTree?.FilePath, System.StringComparer.Ordinal)
            .ThenBy(other => other.Location.SourceSpan.Start)
            .ToList();
    }

    /// <summary>
    /// Two calls in different branches of the same <c>if</c>, <c>?:</c> or <c>switch</c> never both run.
    /// </summary>
    private static bool AreMutuallyExclusive(SyntaxNode first, SyntaxNode second)
    {
        if (first.SyntaxTree != second.SyntaxTree)
            return false;

        var firstAncestors = first.AncestorsAndSelf().ToList();
        foreach (var ancestor in second.AncestorsAndSelf())
        {
            var index = firstAncestors.IndexOf(ancestor);
            if (index < 0)
                continue;

            // ancestor is the closest common node; the children just below it hold each call.
            var firstChild = index > 0 ? firstAncestors[index - 1] : null;
            var secondChild = second.AncestorsAndSelf().TakeWhile(node => node != ancestor).LastOrDefault();
            if (firstChild == null || secondChild == null || firstChild == secondChild)
                return false;

            return ancestor switch
            {
                IfStatementSyntax ifStatement =>
                    IsBranch(firstChild, ifStatement) && IsBranch(secondChild, ifStatement),
                ConditionalExpressionSyntax conditional =>
                    (firstChild == conditional.WhenTrue || firstChild == conditional.WhenFalse) &&
                    (secondChild == conditional.WhenTrue || secondChild == conditional.WhenFalse),
                SwitchStatementSyntax => firstChild is SwitchSectionSyntax && secondChild is SwitchSectionSyntax,
                SwitchExpressionSyntax => firstChild is SwitchExpressionArmSyntax && secondChild is SwitchExpressionArmSyntax,
                _ => false
            };
        }

        return false;
    }

    private static bool IsBranch(SyntaxNode child, IfStatementSyntax ifStatement)
    {
        return child == ifStatement.Statement || child == ifStatement.Else;
    }

    private static Location GetLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
            return memberAccess.Name.GetLocation();

        return invocation.Syntax.GetLocation();
    }

    private sealed class FilterCall
    {
        public FilterCall(ITypeSymbol entityType, ISymbol owner, SyntaxNode syntax, bool named, Location location)
        {
            EntityType = entityType;
            Owner = owner;
            Syntax = syntax;
            Named = named;
            Location = location;
        }

        public ITypeSymbol EntityType { get; }

        public ISymbol Owner { get; }

        public SyntaxNode Syntax { get; }

        public bool Named { get; }

        public Location Location { get; }

        public bool SameOwner(FilterCall other) => SymbolEqualityComparer.Default.Equals(Owner, other.Owner);
    }
}
