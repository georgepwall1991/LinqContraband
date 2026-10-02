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
        // archived rows on purpose. Removing the call would make the query always empty.
        if (QueryChainSelectsDeletedRows(invocation)) return;

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

    private static bool QueryChainSelectsDeletedRows(IInvocationOperation invocation)
    {
        // Calls before IgnoreQueryFilters() in the same chain.
        var current = invocation.GetInvocationReceiver();
        while (current is IInvocationOperation earlier)
        {
            if (IsWhereSelectingDeletedRows(earlier))
                return true;

            current = earlier.GetInvocationReceiver();
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
                IArgumentOperation { Parent: IInvocationOperation extensionCall } argument
                    when extensionCall.TargetMethod.IsExtensionMethod &&
                         extensionCall.Arguments.Length > 0 &&
                         extensionCall.Arguments[0] == argument => extensionCall,
                _ => null
            };

            if (later == null)
                return false;

            if (IsWhereSelectingDeletedRows(later))
                return true;

            link = later;
        }
    }

    private static bool IsWhereSelectingDeletedRows(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "Where" || invocation.Arguments.Length < 2)
            return false;

        var predicate = invocation.Arguments[invocation.Arguments.Length - 1].Value.UnwrapConversions();
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
                return hasValue.Instance != null && IsDeletedMarker(hasValue.Instance, rowParameters);

            case IPropertyReferenceOperation property when property.Type?.SpecialType == SpecialType.System_Boolean:
                return IsDeletedMarker(property, rowParameters);

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } equals:
                return IsDeletedMarker(equals.LeftOperand, rowParameters) && IsConstant(equals.RightOperand, true) ||
                       IsDeletedMarker(equals.RightOperand, rowParameters) && IsConstant(equals.LeftOperand, true);

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.NotEquals } notEquals:
                return IsDeletedMarker(notEquals.LeftOperand, rowParameters) && IsConstant(notEquals.RightOperand, null) ||
                       IsDeletedMarker(notEquals.RightOperand, rowParameters) && IsConstant(notEquals.LeftOperand, null);

            default:
                return false;
        }
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

        var name = property.Property.Name;
        if (name.IndexOf("Not", StringComparison.Ordinal) >= 0 ||
            name.IndexOf("Undelete", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Unarchiv", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.StartsWith("Non", StringComparison.Ordinal))
        {
            return false;
        }

        return name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Archiv", StringComparison.OrdinalIgnoreCase) >= 0;
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
