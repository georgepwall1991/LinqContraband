using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
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

        return LocalAssignmentCache.TryGetSingleAssignedValueBefore(
            executableRoot,
            local,
            position,
            out value);
    }
}
