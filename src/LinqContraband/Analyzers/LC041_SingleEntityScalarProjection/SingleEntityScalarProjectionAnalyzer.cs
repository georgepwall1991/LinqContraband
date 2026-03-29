using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingleEntityScalarProjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC041";
    internal const string PropertyNameKey = "PropertyName";

    private const string Category = "Performance";
    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "First",
        "FirstOrDefault",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "Single",
        "SingleOrDefault",
        "SingleAsync",
        "SingleOrDefaultAsync");

    private static readonly ImmutableHashSet<string> QuerySteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "Include",
        "ThenInclude",
        "IgnoreQueryFilters",
        "AsSplitQuery",
        "AsSingleQuery",
        "AsTracking",
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "IgnoreAutoIncludes",
        "TagWith",
        "TagWithCallSite");

    private static readonly LocalizableString Title = "Single entity query over-fetches one consumed property";

    private static readonly LocalizableString MessageFormat =
        "Query materializes '{0}' but only property '{1}' is consumed in the same scope. Consider projecting that property before materializing.";

    private static readonly LocalizableString Description =
        "When a query result is only used for one scalar property, projecting that property can avoid materializing the full entity.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description);

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

        if (!TargetMethods.Contains(method.Name))
            return;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null)
            return;

        if (!TryGetEntityType(receiver, out var entityType))
            return;

        if (HasSelectInChain(receiver))
            return;

        if (TryGetPredicateLambda(invocation, out var lambda) &&
            IsPrimaryKeyLookup(lambda, entityType))
        {
            return;
        }

        if (!TryGetAssignedLocal(invocation, out var assignedLocal))
            return;

        var executableRoot = invocation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return;

        if (!TryAnalyzeLocalUsage(executableRoot, assignedLocal, out var property))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(PropertyNameKey, property.Name),
                method.Name,
                property.Name));
    }

    private static bool TryGetEntityType(IOperation receiver, out INamedTypeSymbol entityType)
    {
        entityType = null!;

        var current = receiver;
        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                var name = invocation.TargetMethod.Name;
                if (name == "Select")
                    return false;

                if (QuerySteps.Contains(name))
                {
                    current = invocation.GetInvocationReceiver();
                    continue;
                }

                if (name == "Set" && invocation.TargetMethod.ContainingType.IsDbContext())
                {
                    var sequenceElementType = GetSequenceElementType(invocation.Type);
                    if (sequenceElementType != null)
                    {
                        entityType = sequenceElementType;
                        return true;
                    }
                }

                return false;
            }

            var currentType = current.Type;
            if (currentType == null)
                return false;

            if (currentType.IsDbSet())
            {
                var sequenceElementType = GetSequenceElementType(currentType);
                if (sequenceElementType == null)
                    return false;

                entityType = sequenceElementType;
                return true;
            }

            if (currentType.IsIQueryable())
            {
                var sequenceElementType = GetSequenceElementType(currentType);
                if (sequenceElementType == null)
                    return false;

                entityType = sequenceElementType;
                return true;
            }

            if (current is IPropertyReferenceOperation or IFieldReferenceOperation or ILocalReferenceOperation or IParameterReferenceOperation)
                return false;

            return false;
        }

        return false;
    }

    private static bool HasSelectInChain(IOperation receiver)
    {
        var current = receiver;
        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                if (invocation.TargetMethod.Name == "Select")
                    return true;

                if (QuerySteps.Contains(invocation.TargetMethod.Name))
                {
                    current = invocation.GetInvocationReceiver();
                    continue;
                }

                return false;
            }

            break;
        }

        return false;
    }

    internal static bool TryGetAssignedLocal(IInvocationOperation invocation, out ILocalSymbol local)
    {
        local = null!;

        var current = invocation.Parent;
        while (current != null)
        {
            if (current is IVariableDeclaratorOperation declarator)
            {
                local = declarator.Symbol;
                return true;
            }

            if (current is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation localReference)
            {
                local = localReference.Local;
                return true;
            }

            if (current is IExpressionStatementOperation || current is IReturnOperation)
                return false;

            current = current.Parent;
        }

        return false;
    }

    private static bool TryGetPredicateLambda(IInvocationOperation invocation, out IAnonymousFunctionOperation lambda)
    {
        lambda = null!;

        foreach (var argument in invocation.Arguments)
        {
            var value = argument.Value.UnwrapConversions();
            if (value is IAnonymousFunctionOperation anonymousFunction)
            {
                lambda = anonymousFunction;
                return true;
            }
        }

        return false;
    }

    private static bool IsPrimaryKeyLookup(IAnonymousFunctionOperation lambda, ITypeSymbol entityType)
    {
        var primaryKey = entityType.TryFindPrimaryKey();
        if (primaryKey == null)
            return false;

        var body = lambda.Body.Operations.FirstOrDefault();
        if (body is IReturnOperation returnOp)
            body = returnOp.ReturnedValue;

        if (body == null)
            return false;

        body = body.UnwrapConversions();
        if (body is not IBinaryOperation binary || binary.OperatorKind != BinaryOperatorKind.Equals)
            return false;

        return IsPrimaryKeyProperty(binary.LeftOperand, lambda, primaryKey) ||
               IsPrimaryKeyProperty(binary.RightOperand, lambda, primaryKey);
    }

    private static bool IsPrimaryKeyProperty(IOperation operation, IAnonymousFunctionOperation lambda, string primaryKey)
    {
        var current = operation.UnwrapConversions();
        if (current is not IPropertyReferenceOperation propertyReference)
            return false;

        if (propertyReference.Instance?.UnwrapConversions() is not IParameterReferenceOperation parameterReference)
            return false;

        if (!SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, lambda.Symbol.Parameters.FirstOrDefault()))
            return false;

        return propertyReference.Property.Name == primaryKey;
    }

    internal static bool TryAnalyzeLocalUsage(IOperation executableRoot, ILocalSymbol local, out IPropertySymbol property)
    {
        property = null!;
        var properties = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            if (!ReferenceEquals(localReference.FindOwningExecutableRoot(), executableRoot))
                return false;

            if (localReference.Parent is not IPropertyReferenceOperation propertyReference)
                return false;

            if (!ReferenceEquals(propertyReference.Instance?.UnwrapConversions(), localReference))
                return false;

            if (!IsScalarLikeType(propertyReference.Property.Type))
                return false;

            properties.Add(propertyReference.Property);
        }

        if (properties.Count != 1)
            return false;

        property = properties.First();
        return true;
    }

    private static bool IsScalarLikeType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (type.SpecialType != SpecialType.None)
            return true;

        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type.TypeKind == TypeKind.Struct)
            return true;

        return type.Name == "String";
    }

    private static INamedTypeSymbol? GetSequenceElementType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType)
            return null;

        if (namedType.TypeArguments.Length > 0 && namedType.TypeArguments[0] is INamedTypeSymbol namedArgument)
            return namedArgument;

        return null;
    }
}
