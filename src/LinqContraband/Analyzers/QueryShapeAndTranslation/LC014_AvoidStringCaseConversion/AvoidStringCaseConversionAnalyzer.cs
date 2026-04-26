using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
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
public class AvoidStringCaseConversionAnalyzer : DiagnosticAnalyzer
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
        Description);

    private static readonly HashSet<string> CaseConversionMethods = new()
    {
        "ToLower",
        "ToLowerInvariant",
        "ToUpper",
        "ToUpperInvariant"
    };

    private static readonly HashSet<string> TargetLinqMethods = new()
    {
        "Where",
        "OrderBy", "OrderByDescending",
        "ThenBy", "ThenByDescending",
        "Count", "LongCount",
        "Any", "All",
        "First", "FirstOrDefault",
        "Single", "SingleOrDefault",
        "Last", "LastOrDefault",
        "Join", "GroupJoin"
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

    private ImmutableArray<IParameterSymbol> GetEnclosingQueryableLambdaParameters(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IAnonymousFunctionOperation lambda)
            {
                // Check if this lambda is passed to a Queryable method
                // Lambda -> (Conversion) -> Argument -> Invocation
                var parent = lambda.Parent;
                while (parent is IConversionOperation) parent = parent.Parent;

                if (parent is IArgumentOperation argument &&
                    argument.Parent is IInvocationOperation linqInvocation)
                {
                    var method = linqInvocation.TargetMethod;
                    if (IsTargetQueryableMethod(method) &&
                        HasEntityFrameworkQuerySource(linqInvocation.GetInvocationReceiver()))
                    {
                        return lambda.Symbol.Parameters;
                    }
                }
            }

            current = current.Parent;
        }

        return ImmutableArray<IParameterSymbol>.Empty;
    }

    private static bool IsTargetQueryableMethod(IMethodSymbol method)
    {
        return TargetLinqMethods.Contains(method.Name) &&
               method.ContainingType.Name == "Queryable" &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }

    private static bool HasEntityFrameworkQuerySource(IOperation? operation)
    {
        var current = operation;
        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current.Type.IsDbSet())
                return true;

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (invocation.TargetMethod.Name == "Set" && invocation.TargetMethod.ContainingType.IsDbContext())
                        return true;

                    current = invocation.GetInvocationReceiver();
                    continue;

                case IPropertyReferenceOperation propertyReference:
                    if (propertyReference.Type.IsDbSet())
                        return true;

                    current = propertyReference.Instance;
                    continue;

                case IFieldReferenceOperation fieldReference:
                    if (fieldReference.Type.IsDbSet())
                        return true;

                    current = fieldReference.Instance;
                    continue;

                case ILocalReferenceOperation localReference:
                    if (localReference.Type.IsDbSet())
                        return true;

                    if (TryResolveLocalValue(localReference.Local, localReference, localReference.FindOwningExecutableRoot(), out var resolvedValue))
                    {
                        current = resolvedValue;
                        continue;
                    }

                    return false;

                case IParameterReferenceOperation parameterReference:
                    return parameterReference.Type.IsDbSet();

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryResolveLocalValue(ILocalSymbol local, IOperation reference, IOperation? executableRoot, out IOperation value)
    {
        value = null!;

        if (executableRoot == null)
            return false;

        var referenceStart = reference.Syntax.SpanStart;
        var bestWriteStart = -1;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) || declarator.Initializer == null)
                        continue;

                    var writeStart = declarator.Syntax.SpanStart;
                    if (writeStart >= referenceStart || writeStart <= bestWriteStart)
                        continue;

                    bestWriteStart = writeStart;
                    value = declarator.Initializer.Value;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
            {
                var writeStart = assignment.Syntax.SpanStart;
                if (writeStart >= referenceStart || writeStart <= bestWriteStart)
                    continue;

                bestWriteStart = writeStart;
                value = assignment.Value;
            }
        }

        return bestWriteStart >= 0;
    }

    private bool ReceiverDependsOnParameter(IOperation? operation, ImmutableArray<IParameterSymbol> targetParameters)
    {
        if (operation == null) return false;

        // Unwrap conversions
        operation = operation!.UnwrapConversions();

        // If it's a parameter reference, check if it matches our target lambda parameters
        if (operation is IParameterReferenceOperation paramRef) return targetParameters.Contains(paramRef.Parameter);

        // If it's a property reference, check the instance of the property
        if (operation is IPropertyReferenceOperation propRef)
            return ReceiverDependsOnParameter(propRef.Instance, targetParameters);

        // If it's a method call (chained), check the instance
        if (operation is IInvocationOperation invocation)
            return ReceiverDependsOnParameter(invocation.Instance, targetParameters);

        // If it's an array/indexer access
        if (operation is IPropertyReferenceOperation indexer && indexer.Arguments.Length > 0)
            return ReceiverDependsOnParameter(indexer.Instance, targetParameters);

        // Binary Operator
        if (operation is IBinaryOperation binaryOp)
            return ReceiverDependsOnParameter(binaryOp.LeftOperand, targetParameters) ||
                   ReceiverDependsOnParameter(binaryOp.RightOperand, targetParameters);

        // Coalesce Operator
        if (operation is ICoalesceOperation coalesce)
            return ReceiverDependsOnParameter(coalesce.Value, targetParameters) ||
                   ReceiverDependsOnParameter(coalesce.WhenNull, targetParameters);

        if (operation.Kind == OperationKind.ConditionalAccess)
        {
            var conditional = (IConditionalAccessOperation)operation;
            return ReceiverDependsOnParameter(conditional.Operation, targetParameters);
        }

        if (operation.Kind == OperationKind.ConditionalAccessInstance)
        {
            var parent = operation.Parent; // Invocation (ToLower)
            var grandParent = parent?.Parent; // ConditionalAccessOperation

            if (grandParent is IConditionalAccessOperation caOp)
                return ReceiverDependsOnParameter(caOp.Operation, targetParameters);
        }

        return false;
    }
}
