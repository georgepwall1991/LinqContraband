using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

/// <summary>
/// Analyzes query continuations that happen only after an IQueryable has already been materialized. Diagnostic ID: LC002
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PrematureMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC002";
    private const string Category = "Performance";
    private const string HelpLinkUri =
        "https://github.com/georgewall/LinqContraband/blob/main/docs/LC002_PrematureMaterialization.md";

    internal const string DiagnosticKindKey = "DiagnosticKind";
    internal const string OriginKindKey = "OriginKind";
    internal const string CurrentMethodKey = "CurrentMethod";
    internal const string MaterializerKey = "Materializer";
    internal const string FixKindKey = "FixKind";

    internal const string ContinuationDiagnosticKind = "Continuation";
    internal const string RedundantDiagnosticKind = "Redundant";

    internal const string InlineInvocationOriginKind = "InlineInvocation";
    internal const string LocalOriginKind = "Local";
    internal const string ConstructorOriginKind = "Constructor";

    internal const string MoveBeforeMaterializationFixKind = "MoveBeforeMaterialization";
    internal const string RemoveRedundantMaterializationFixKind = "RemoveRedundantMaterialization";

    private static readonly LocalizableString Title = "Premature query continuation after materialization";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' after materializing an IQueryable forces the operation to run in memory";

    private static readonly LocalizableString RedundantMessageFormat =
        "The call to '{0}' is redundant because the sequence was already materialized by '{1}'";

    private static readonly LocalizableString Description =
        "Keep approved query operations on IQueryable before materialization and avoid redundant second materializers.";

    private static readonly ImmutableHashSet<string> SequenceContinuationMethods = ImmutableHashSet.Create(
        "Where",
        "Select",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take"
    );

    private static readonly ImmutableHashSet<string> TerminalContinuationMethods = ImmutableHashSet.Create(
        "Count",
        "LongCount",
        "Any",
        "All",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "Last",
        "LastOrDefault",
        "Min",
        "Max",
        "Sum",
        "Average"
    );

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpLinkUri);

    public static readonly DiagnosticDescriptor RedundantRule = new(
        DiagnosticId,
        "Redundant materialization",
        RedundantMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpLinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, RedundantRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null) return;

        var receiverType = receiver.Type;
        var unwrappedReceiver = receiver.UnwrapConversions();

        if (TryCreateRedundantDiagnostic(context, invocation, unwrappedReceiver, out var redundantDiagnostic))
        {
            context.ReportDiagnostic(redundantDiagnostic);
            return;
        }

        if (receiverType?.IsIQueryable() == true) return;
        if (!IsApprovedContinuationMethod(invocation.TargetMethod, out _)) return;

        if (!TryResolveMaterializationOrigin(
                unwrappedReceiver,
                invocation.Syntax.SpanStart,
                context.Operation.FindOwningExecutableRoot(),
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                out var materializationOrigin))
        {
            return;
        }

        var properties = CreateProperties(
            ContinuationDiagnosticKind,
            materializationOrigin.OriginKind,
            invocation.TargetMethod.Name,
            materializationOrigin.MaterializerName);

        if (CanOfferMoveBeforeMaterializationFix(invocation.TargetMethod, materializationOrigin))
        {
            properties = properties.SetItem(FixKindKey, MoveBeforeMaterializationFixKind);
        }

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, invocation.TargetMethod.Name));
    }

    private static bool TryCreateRedundantDiagnostic(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        IOperation receiver,
        out Diagnostic diagnostic)
    {
        diagnostic = null!;
        if (!IsMaterializingMethod(invocation.TargetMethod)) return false;

        if (!TryResolveMaterializationOrigin(
                receiver,
                invocation.Syntax.SpanStart,
                context.Operation.FindOwningExecutableRoot(),
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                out var previousMaterialization))
        {
            return false;
        }

        var properties = CreateProperties(
            RedundantDiagnosticKind,
            previousMaterialization.OriginKind,
            invocation.TargetMethod.Name,
            previousMaterialization.MaterializerName);

        if (CanOfferRemoveRedundantMaterializationFix(invocation.TargetMethod.Name, previousMaterialization))
        {
            properties = properties.SetItem(FixKindKey, RemoveRedundantMaterializationFixKind);
        }

        diagnostic = Diagnostic.Create(
            RedundantRule,
            invocation.Syntax.GetLocation(),
            properties,
            invocation.TargetMethod.Name,
            previousMaterialization.MaterializerName);
        return true;
    }

    private static ImmutableDictionary<string, string?> CreateProperties(
        string diagnosticKind,
        string originKind,
        string currentMethod,
        string materializer)
    {
        return ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticKindKey, diagnosticKind)
            .Add(OriginKindKey, originKind)
            .Add(CurrentMethodKey, currentMethod)
            .Add(MaterializerKey, materializer);
    }

    private static bool TryResolveMaterializationOrigin(
        IOperation operation,
        int position,
        IOperation? executableRoot,
        HashSet<ILocalSymbol> visitedLocals,
        out MaterializationOrigin origin)
    {
        origin = default;
        var unwrapped = operation.UnwrapConversions();

        if (unwrapped is ILocalReferenceOperation localReference)
        {
            if (executableRoot == null) return false;

            if (!TryResolveSingleAssignedValue(executableRoot, localReference.Local, position, visitedLocals, out var assignedValue))
                return false;

            if (TryResolveMaterializationOrigin(
                    assignedValue,
                    position,
                    executableRoot,
                    visitedLocals,
                    out var localOrigin))
            {
                origin = new MaterializationOrigin(LocalOriginKind, localOrigin.MaterializerName);
                return true;
            }

            return false;
        }

        if (unwrapped is IInvocationOperation materializerInvocation &&
            IsMaterializingMethod(materializerInvocation.TargetMethod) &&
            TryResolveQueryableOrMaterializedSource(
                materializerInvocation.GetInvocationReceiver(),
                position,
                executableRoot,
                visitedLocals))
        {
            origin = new MaterializationOrigin(InlineInvocationOriginKind, materializerInvocation.TargetMethod.Name);
            return true;
        }

        if (unwrapped is IObjectCreationOperation objectCreation &&
            objectCreation.Constructor != null &&
            IsMaterializingConstructor(objectCreation.Constructor) &&
            objectCreation.Arguments.Length > 0 &&
            TryResolveQueryableOrMaterializedSource(
                objectCreation.Arguments[0].Value,
                position,
                executableRoot,
                visitedLocals))
        {
            origin = new MaterializationOrigin(ConstructorOriginKind, objectCreation.Constructor.ContainingType.Name);
            return true;
        }

        return false;
    }

    private static bool TryResolveQueryableOrMaterializedSource(
        IOperation? operation,
        int position,
        IOperation? executableRoot,
        HashSet<ILocalSymbol> visitedLocals)
    {
        if (operation == null) return false;

        var unwrapped = operation.UnwrapConversions();

        if (unwrapped.Type?.IsIQueryable() == true) return true;

        if (TryResolveMaterializationOrigin(unwrapped, position, executableRoot, visitedLocals, out _))
        {
            return true;
        }

        if (unwrapped is not ILocalReferenceOperation localReference || executableRoot == null)
        {
            return false;
        }

        return TryResolveSingleAssignedValue(executableRoot, localReference.Local, position, visitedLocals, out var assignedValue) &&
               TryResolveQueryableOrMaterializedSource(assignedValue, position, executableRoot, visitedLocals);
    }

    private static bool TryResolveSingleAssignedValue(
        IOperation executableRoot,
        ILocalSymbol local,
        int position,
        HashSet<ILocalSymbol> visitedLocals,
        out IOperation value)
    {
        value = null!;
        if (!visitedLocals.Add(local)) return false;

        IOperation? latestValue = null;
        var latestPosition = -1;
        var assignmentCount = 0;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant.Syntax.SpanStart >= position) continue;

            switch (descendant)
            {
                case IVariableDeclaratorOperation declarator when
                    SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                    declarator.Initializer != null &&
                    declarator.Syntax.SpanStart > latestPosition:
                    latestValue = declarator.Initializer.Value;
                    latestPosition = declarator.Syntax.SpanStart;
                    assignmentCount++;
                    break;

                case ISimpleAssignmentOperation assignment when
                    assignment.Target is ILocalReferenceOperation targetLocal &&
                    SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                    assignment.Syntax.SpanStart > latestPosition:
                    latestValue = assignment.Value;
                    latestPosition = assignment.Syntax.SpanStart;
                    assignmentCount++;
                    break;
            }
        }

        if (latestValue == null || assignmentCount != 1)
        {
            return false;
        }

        value = latestValue.UnwrapConversions();
        return true;
    }

    private static bool IsApprovedContinuationMethod(IMethodSymbol method, out bool returnsSequence)
    {
        returnsSequence = false;

        if (method.ContainingType.Name != "Enumerable" ||
            method.ContainingNamespace?.ToString() != "System.Linq")
        {
            return false;
        }

        if (HasUnsupportedComparerOverload(method) || HasUnsupportedIndexAwareLambda(method))
        {
            return false;
        }

        if (SequenceContinuationMethods.Contains(method.Name))
        {
            returnsSequence = true;
            return true;
        }

        if (TerminalContinuationMethods.Contains(method.Name))
        {
            return true;
        }

        return false;
    }

    private static bool HasUnsupportedComparerOverload(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0) return false;

        var lastParameterType = method.Parameters[method.Parameters.Length - 1].Type;
        return lastParameterType.Name is "IEqualityComparer" or "IComparer" &&
               lastParameterType.ContainingNamespace?.ToString() == "System.Collections.Generic";
    }

    private static bool HasUnsupportedIndexAwareLambda(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0) return false;

        var lastParameterType = method.Parameters[method.Parameters.Length - 1].Type as INamedTypeSymbol;
        var invokeMethod = lastParameterType?.DelegateInvokeMethod;
        return invokeMethod != null && invokeMethod.Parameters.Length > 1;
    }

    private static bool CanOfferMoveBeforeMaterializationFix(IMethodSymbol continuationMethod, MaterializationOrigin origin)
    {
        if (origin.OriginKind != InlineInvocationOriginKind) return false;
        if (!IsApprovedContinuationMethod(continuationMethod, out _)) return false;

        return origin.MaterializerName is
            "ToList" or
            "ToArray" or
            "AsEnumerable" or
            "ToImmutableList" or
            "ToImmutableArray";
    }

    private static bool CanOfferRemoveRedundantMaterializationFix(string currentMaterializer, MaterializationOrigin previousMaterialization)
    {
        if (previousMaterialization.OriginKind != InlineInvocationOriginKind) return false;

        return currentMaterializer == "AsEnumerable" ||
               previousMaterialization.MaterializerName == "AsEnumerable" ||
               (IsDirectCollectionMaterializer(currentMaterializer) &&
                IsDirectCollectionMaterializer(previousMaterialization.MaterializerName));
    }

    private static bool IsMaterializingMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        if (ns is not ("System.Linq" or "Microsoft.EntityFrameworkCore" or "System.Collections.Immutable")) return false;

        if (method.Name == "AsEnumerable") return true;

        return method.Name == "ToList" ||
               method.Name == "ToListAsync" ||
               method.Name == "ToArray" ||
               method.Name == "ToArrayAsync" ||
               method.Name == "ToDictionary" ||
               method.Name == "ToDictionaryAsync" ||
               method.Name == "ToHashSet" ||
               method.Name == "ToHashSetAsync" ||
               method.Name == "ToLookup" ||
               method.Name.StartsWith("ToImmutable", StringComparison.Ordinal);
    }

    private static bool IsDirectCollectionMaterializer(string methodName)
    {
        return methodName is
            "ToList" or
            "ToArray" or
            "ToHashSet" or
            "ToImmutableList" or
            "ToImmutableArray" or
            "ToImmutableHashSet";
    }

    private static bool IsMaterializingConstructor(IMethodSymbol constructor)
    {
        var type = constructor.ContainingType;
        if (type.ContainingNamespace?.ToString() != "System.Collections.Generic") return false;

        return type.Name == "List" ||
               type.Name == "HashSet" ||
               type.Name == "Dictionary" ||
               type.Name == "SortedDictionary" ||
               type.Name == "SortedList" ||
               type.Name == "LinkedList" ||
               type.Name == "Queue" ||
               type.Name == "Stack";
    }

    private readonly struct MaterializationOrigin
    {
        public MaterializationOrigin(string originKind, string materializerName)
        {
            OriginKind = originKind;
            MaterializerName = materializerName;
        }

        public string OriginKind { get; }

        public string MaterializerName { get; }
    }
}
