using System;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters;

/// <summary>
/// Analyzes usage of IgnoreQueryFilters which can bypass critical global security or logic filters. Diagnostic ID: LC021
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidIgnoreQueryFiltersAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC021";
    private const string Category = "Security";
    private static readonly LocalizableString Title = "Avoid IgnoreQueryFilters";

    private static readonly LocalizableString MessageFormat =
        "Usage of 'IgnoreQueryFilters' can bypass critical global filters like multi-tenancy or soft-delete. Ensure this is intentional.";

    private static readonly LocalizableString Description =
        "IgnoreQueryFilters disables all global query filters for the current query, which might lead to unintended data access or incorrect business logic.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC021_AvoidIgnoreQueryFilters.html");

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

        if (!IsEfCoreIgnoreQueryFiltersMethod(method)) return;

        if (!GetQuerySourceType(invocation).IsIQueryable()) return;

        // posts.IgnoreQueryFilters().Where(p => p.IsDeleted): maintenance code that reads soft-deleted or
        // archived rows on purpose. Removing the call would make the query always empty. Only the parameterless
        // overload: IgnoreQueryFilters(["Tenant"]) names the filters it turns off, and a deleted-rows predicate
        // says nothing about whether a named filter guards tenancy instead.
        if (IsParameterlessOverload(method) && QueryChainSelectsDeletedRows(invocation)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
    }

    private static ITypeSymbol? GetQuerySourceType(IInvocationOperation invocation)
    {
        if (invocation.Instance is not null)
            return invocation.Instance.UnwrapConversions().Type;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "source")
                return argument.Value.UnwrapConversions().Type;
        }

        return invocation.Arguments.Length > 0
            ? invocation.Arguments[0].Value.UnwrapConversions().Type
            : null;
    }

    private static bool IsParameterlessOverload(IMethodSymbol method)
    {
        return method.ReducedFrom != null ? method.Parameters.Length == 0 : method.Parameters.Length == 1;
    }

    // The query an operator runs on: the instance, or the extension method's first parameter wherever a named
    // argument puts it (Queryable.Where(predicate: ..., source: ...)).
    private static IOperation? GetChainSource(IInvocationOperation invocation)
    {
        if (invocation.Instance != null)
            return invocation.Instance.UnwrapConversions();

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
                return argument.Value.UnwrapConversions();
        }

        return null;
    }

    // Operators over one query source that keep its rows as they are, so a deleted-rows Where on either side of
    // IgnoreQueryFilters() still constrains every row the call exposes. Concat, Union, Join or SelectMany bring
    // in another source, and Select changes what a later Where filters, so the walk stops at them.
    private static readonly ImmutableHashSet<string> ChainOperators = ImmutableHashSet.Create(
        "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Skip", "Take", "SkipWhile",
        "TakeWhile", "Distinct", "Reverse", "AsQueryable", "AsNoTracking", "AsNoTrackingWithIdentityResolution",
        "AsTracking", "Include", "ThenInclude", "AsSplitQuery", "AsSingleQuery", "TagWith", "TagWithCallSite",
        "IgnoreAutoIncludes", "IgnoreQueryFilters");

    // Only the real System.Linq.Queryable and EF Core operators: a project's own Where could ignore its predicate.
    private static bool IsChainOperator(IMethodSymbol method)
    {
        if (!ChainOperators.Contains(method.Name))
            return false;

        var containingType = method.ContainingType?.Name;
        var containingNamespace = method.ContainingNamespace?.ToString();
        return containingType == "Queryable" && containingNamespace == "System.Linq" ||
               containingNamespace == "Microsoft.EntityFrameworkCore" &&
               containingType is "EntityFrameworkQueryableExtensions" or "RelationalQueryableExtensions";
    }

    private static bool QueryChainSelectsDeletedRows(IInvocationOperation invocation)
    {
        // Calls before IgnoreQueryFilters() in the same chain.
        var current = GetChainSource(invocation);
        while (current is IInvocationOperation earlier && IsChainOperator(earlier.TargetMethod))
        {
            if (IsWhereSelectingDeletedRows(earlier))
                return true;

            current = GetChainSource(earlier);
        }

        // Calls after it.
        IOperation link = invocation;
        while (true)
        {
            var parent = link.Parent;
            while (parent is IConversionOperation)
            {
                link = parent;
                parent = parent.Parent;
            }

            IInvocationOperation? later = parent switch
            {
                IInvocationOperation instanceCall when instanceCall.Instance == link => instanceCall,
                IArgumentOperation { Parent: IInvocationOperation extensionCall, Parameter.Ordinal: 0 }
                    when extensionCall.TargetMethod.IsExtensionMethod => extensionCall,
                _ => null
            };

            if (later == null || !IsChainOperator(later.TargetMethod))
                return false;

            if (IsWhereSelectingDeletedRows(later))
                return true;

            link = later;
        }
    }

    private static bool IsWhereSelectingDeletedRows(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "Where" || !IsChainOperator(invocation.TargetMethod) ||
            invocation.Arguments.Length < 2)
            return false;

        // The predicate is the parameter after the source, wherever a named argument puts it.
        IOperation? predicate = null;
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 1)
                predicate = argument.Value.UnwrapConversions();
        }

        if (predicate == null)
            return false;

        if (predicate is IDelegateCreationOperation delegateCreation)
            predicate = delegateCreation.Target.UnwrapConversions();

        if (predicate is not IAnonymousFunctionOperation { Body.Operations.Length: 1 } lambda ||
            lambda.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } body })
            return false;

        return SelectsDeletedRows(body, lambda.Symbol.Parameters);
    }

    // p.IsDeleted, p.IsDeleted == true, p.DeletedAt != null, p.DeletedAt.HasValue, or any of these as one side
    // of &&. Expression trees cannot hold pattern matching, so 'is true' and 'is not null' never reach here.
    private static bool SelectsDeletedRows(IOperation expression, ImmutableArray<IParameterSymbol> rowParameters)
    {
        switch (Unwrap(expression))
        {
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } and:
                return SelectsDeletedRows(and.LeftOperand, rowParameters) || SelectsDeletedRows(and.RightOperand, rowParameters);

            case IPropertyReferenceOperation { Property.Name: "HasValue" } hasValue:
                return hasValue.Instance != null && IsPresenceMarker(hasValue.Instance, rowParameters);

            case IPropertyReferenceOperation property when property.Type?.SpecialType == SpecialType.System_Boolean:
                return IsDeletedMarker(property, rowParameters);

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } equals:
                return IsDeletedMarker(equals.LeftOperand, rowParameters) && IsConstant(equals.RightOperand, true) ||
                       IsDeletedMarker(equals.RightOperand, rowParameters) && IsConstant(equals.LeftOperand, true);

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.NotEquals } notEquals:
                return IsPresenceMarker(notEquals.LeftOperand, rowParameters) && IsConstant(notEquals.RightOperand, null) ||
                       IsPresenceMarker(notEquals.RightOperand, rowParameters) && IsConstant(notEquals.LeftOperand, null);

            default:
                return false;
        }
    }

    // p.DeletedAt != null proves deletion; p.IsArchived != null on a bool? does not, because false is a value too.
    private static bool IsPresenceMarker(IOperation operation, ImmutableArray<IParameterSymbol> rowParameters)
    {
        if (!IsDeletedMarker(operation, rowParameters))
            return false;

        return Unwrap(operation).Type is not INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
        } nullable || nullable.TypeArguments[0].SpecialType != SpecialType.System_Boolean;
    }

    // A soft-delete column on the row itself: p.IsDeleted, not a captured options.IncludeDeleted switch, and not a
    // negated name such as p.IsNotDeleted or p.Undeleted, which selects the rows the filter already keeps.
    private static bool IsDeletedMarker(IOperation operation, ImmutableArray<IParameterSymbol> rowParameters)
    {
        if (Unwrap(operation) is not IPropertyReferenceOperation { Property.IsIndexer: false } property ||
            property.Instance == null ||
            Unwrap(property.Instance) is not IParameterReferenceOperation { Parameter: var parameter } ||
            !rowParameters.Contains(parameter, SymbolEqualityComparer.Default))
        {
            return false;
        }

        return IsRowDeletionStateName(property.Property.Name);
    }

    // The row's own deletion state, by whole name: [Is][Soft]Deleted or [Is]Archived, optionally followed by a
    // timestamp or actor suffix (IsDeleted, SoftDeletedAt, ArchivedOn, DeletedBy). A substring match would also take
    // CanDelete, IsNotDeleted or HasDeletedComments, which do not say the row itself is deleted.
    private static readonly ImmutableHashSet<string> DeletionStateSuffixes = ImmutableHashSet.Create(
        "", "At", "AtUtc", "On", "OnUtc", "Date", "DateUtc", "Time", "Utc", "Timestamp", "By", "ById", "ByUserId");

    private static bool IsRowDeletionStateName(string name)
    {
        var rest = name;
        if (rest.StartsWith("Is", StringComparison.Ordinal))
            rest = rest.Substring(2);

        if (rest.StartsWith("Soft", StringComparison.Ordinal))
            rest = rest.Substring(4);

        if (rest.StartsWith("Deleted", StringComparison.Ordinal))
            rest = rest.Substring("Deleted".Length);
        else if (rest.StartsWith("Archived", StringComparison.Ordinal))
            rest = rest.Substring("Archived".Length);
        else
            return false;

        return DeletionStateSuffixes.Contains(rest);
    }

    private static bool IsConstant(IOperation operation, object? value)
    {
        var constant = Unwrap(operation).ConstantValue;
        return constant.HasValue && Equals(constant.Value, value);
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation or IParenthesizedOperation)
            operation = operation is IConversionOperation conversion ? conversion.Operand : ((IParenthesizedOperation)operation).Operand;

        return operation;
    }

    private static bool IsEfCoreIgnoreQueryFiltersMethod(IMethodSymbol method)
    {
        return method.Name == "IgnoreQueryFilters" &&
               method.ContainingType?.Name == "EntityFrameworkQueryableExtensions" &&
               method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }
}
