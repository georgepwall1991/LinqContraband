using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool IsRequiredOnPathFrom(IOperation start, IOperation required, IOperation later)
    {
        var requiredCatch = required.Syntax.Ancestors()
            .OfType<CatchClauseSyntax>()
            .FirstOrDefault();
        var requiredReachesLater = BlockReaches(required, later) ||
                                   (requiredCatch != null &&
                                    CatchHandlerCanReachLater(requiredCatch, later));
        if (!start.SharesOwningExecutableRoot(required) ||
            !required.SharesOwningExecutableRoot(later) ||
            start.Syntax.SpanStart >= required.Syntax.SpanStart ||
            required.Syntax.SpanStart >= later.Syntax.SpanStart ||
            !BlockReaches(start, required) ||
            !requiredReachesLater)
        {
            return false;
        }

        if (HasReachableBranchSkippingRequired(start, required, later))
            return false;

        if (HasCaughtThrowSkippingRequired(start, required, later))
            return false;

        if (HasPotentiallyThrowingOperationSkippingRequired(start, required, later))
            return false;

        if (RequiredOperationCanTransferBeforeCompletion(required, later))
        {
            return false;
        }

        for (var ancestor = required.Syntax.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                case IfStatementSyntax ifStatement:
                    if (SameIfBranchContains(ifStatement, required.Syntax, start.Syntax) ||
                        IfStatementMakesBranchMandatory(ifStatement, required.Syntax, later.Syntax))
                    {
                        continue;
                    }

                    return false;

                case ElseClauseSyntax:
                case SwitchStatementSyntax:
                    continue;

                case CatchClauseSyntax catchClause:
                    if (catchClause.Block.Span.Contains(start.Syntax.SpanStart) ||
                        CatchClauseIsMandatoryFrom(catchClause, start, later))
                    {
                        continue;
                    }

                    return false;

                case SwitchSectionSyntax switchSection:
                    if (switchSection.Span.Contains(start.Syntax.SpanStart)) continue;
                    return false;

                case WhileStatementSyntax whileStatement:
                    if (whileStatement.Statement.Span.Contains(start.Syntax.SpanStart)) continue;
                    return false;

                case DoStatementSyntax:
                    continue;

                case ForStatementSyntax forStatement:
                    if (forStatement.Statement.Span.Contains(start.Syntax.SpanStart)) continue;
                    return false;

                case ForEachStatementSyntax forEachStatement:
                    if (forEachStatement.Statement.Span.Contains(start.Syntax.SpanStart)) continue;
                    return false;

                case ForEachVariableStatementSyntax forEachVariableStatement:
                    if (forEachVariableStatement.Statement.Span.Contains(start.Syntax.SpanStart)) continue;
                    return false;

                case ConditionalExpressionSyntax conditional:
                    if (SameConditionalArmContains(conditional, required.Syntax, start.Syntax)) continue;
                    return false;
            }
        }

        return true;
    }

    private static bool RequiredOperationCanTransferBeforeCompletion(
        IOperation required,
        IOperation later)
    {
        if (IsImplicitlyPotentiallyThrowingOperation(required) &&
            CanTransferToFallThroughCatch(required, later))
        {
            return true;
        }

        if (required is not ISimpleAssignmentOperation)
            return false;

        foreach (var operation in required.Descendants())
        {
            if (required.SharesOwningExecutableRoot(operation) &&
                IsImplicitlyPotentiallyThrowingOperation(operation) &&
                CanTransferToFallThroughCatch(operation, later))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CatchClauseIsMandatoryFrom(
        CatchClauseSyntax requiredCatch,
        IOperation start,
        IOperation later)
    {
        if (requiredCatch.Parent is not TryStatementSyntax tryStatement ||
            later.Syntax.SpanStart <= tryStatement.Span.End ||
            tryStatement.Block.Statements.LastOrDefault() is not ThrowStatementSyntax terminalThrow)
        {
            return false;
        }

        var semanticModel = start.SemanticModel ?? later.SemanticModel;
        if (semanticModel?.GetOperation(terminalThrow) is not IThrowOperation throwOperation ||
            !StartCanReachSyntax(start.Syntax, terminalThrow))
        {
            return false;
        }

        var thrownType = GetThrownType(throwOperation, terminalThrow, semanticModel);
        if (thrownType == null) return false;

        foreach (var catchClause in tryStatement.Catches)
        {
            if (catchClause.Filter?.FilterExpression is { } filterExpression)
            {
                var constant = semanticModel.GetConstantValue(filterExpression);
                if (constant.HasValue && constant.Value is false) continue;
                if (!constant.HasValue || constant.Value is not true) return false;
            }

            var caughtType = catchClause.Declaration == null
                ? null
                : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
            if (!CanCatch(thrownType, caughtType, throwOperation, catchClause)) continue;

            return ReferenceEquals(catchClause, requiredCatch);
        }

        return false;
    }

    private static bool HasPotentiallyThrowingOperationSkippingRequired(
        IOperation start,
        IOperation required,
        IOperation later)
    {
        var root = start.FindOwningExecutableRoot();
        if (root == null) return false;

        foreach (var operation in root.Descendants())
        {
            if (operation.Syntax.SpanStart <= start.Syntax.SpanStart ||
                operation.Syntax.SpanStart >= required.Syntax.SpanStart ||
                start.Syntax.Span.Contains(operation.Syntax.Span) ||
                operation.Syntax.AncestorsAndSelf()
                    .Any(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax) ||
                !StartCanReachSyntax(start.Syntax, operation.Syntax) ||
                !IsImplicitlyPotentiallyThrowingOperation(operation) ||
                !start.SharesOwningExecutableRoot(operation))
            {
                continue;
            }

            if (CanTransferToFallThroughCatch(operation, later, required.Syntax.SpanStart))
                return true;
        }

        return false;
    }

    private static bool IsImplicitlyPotentiallyThrowingOperation(IOperation operation) =>
        operation is IInvocationOperation or
            IObjectCreationOperation or
            IArrayElementReferenceOperation or
            IPropertyReferenceOperation ||
        operation is IFieldReferenceOperation { Instance: { } instance } &&
        instance is not IInstanceReferenceOperation &&
        instance is not IConditionalAccessInstanceOperation &&
        (instance.Type?.IsReferenceType == true ||
         instance.Type?.TypeKind == TypeKind.TypeParameter);

    private static bool CanTransferToFallThroughCatch(
        IOperation operation,
        IOperation later,
        int? requiredSpanStart = null)
    {
        foreach (var tryStatement in operation.Syntax.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(operation.Syntax.SpanStart) ||
                (requiredSpanStart.HasValue &&
                 !tryStatement.Block.Span.Contains(requiredSpanStart.Value)) ||
                later.Syntax.SpanStart <= tryStatement.Span.End)
            {
                continue;
            }

            foreach (var catchClause in tryStatement.Catches)
            {
                if (catchClause.Filter?.FilterExpression is { } filterExpression)
                {
                    var constant = operation.SemanticModel?.GetConstantValue(filterExpression);
                    if (constant is { HasValue: true, Value: false }) continue;
                }

                var semanticModel = operation.SemanticModel;
                var caughtType = catchClause.Declaration == null
                    ? semanticModel?.Compilation.GetTypeByMetadataName("System.Exception")
                    : semanticModel?.GetTypeInfo(catchClause.Declaration.Type).Type;
                if (caughtType != null &&
                    semanticModel != null &&
                    !ExactExceptionEscapesNestedTries(
                        caughtType, operation.Syntax, tryStatement, semanticModel))
                {
                    continue;
                }

                if (!StatementSkipsLater(catchClause.Block, later.Syntax))
                    return true;
            }
        }

        return false;
    }

    private static bool HasReachableBranchSkippingRequired(
        IOperation start,
        IOperation required,
        IOperation later)
    {
        var root = start.FindOwningExecutableRoot();
        if (root == null) return false;

        foreach (var branch in root.Descendants().OfType<IBranchOperation>())
        {
            if (branch.Syntax.SpanStart <= start.Syntax.SpanStart ||
                branch.Syntax.SpanStart >= required.Syntax.SpanStart)
            {
                continue;
            }

            if (branch.BranchKind != BranchKind.Break &&
                branch.BranchKind != BranchKind.Continue &&
                branch.BranchKind != BranchKind.GoTo)
            {
                continue;
            }

            if (BranchTargetSkipsRequired(branch, required) &&
                BlockReaches(start, branch) &&
                BlockReaches(branch, later))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BranchTargetSkipsRequired(IBranchOperation branch, IOperation required)
    {
        var requiredStart = required.Syntax.SpanStart;
        if (branch.BranchKind == BranchKind.Break)
        {
            var breakable = branch.Syntax.Ancestors().FirstOrDefault(ancestor =>
                ancestor.IsKind(SyntaxKind.SwitchStatement) ||
                ancestor.IsKind(SyntaxKind.WhileStatement) ||
                ancestor.IsKind(SyntaxKind.DoStatement) ||
                ancestor.IsKind(SyntaxKind.ForStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachVariableStatement));
            return breakable?.Span.Contains(requiredStart) == true;
        }

        if (branch.BranchKind == BranchKind.Continue)
        {
            var loop = branch.Syntax.Ancestors().FirstOrDefault(ancestor =>
                ancestor.IsKind(SyntaxKind.WhileStatement) ||
                ancestor.IsKind(SyntaxKind.DoStatement) ||
                ancestor.IsKind(SyntaxKind.ForStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachVariableStatement));
            return loop?.Span.Contains(requiredStart) == true;
        }

        var targetLocation = branch.Target.Locations.FirstOrDefault(location => location.IsInSource);
        return targetLocation != null && targetLocation.SourceSpan.Start > requiredStart;
    }

    private static bool HasCaughtThrowSkippingRequired(
        IOperation start,
        IOperation required,
        IOperation later)
    {
        var root = start.FindOwningExecutableRoot();
        var semanticModel = root?.SemanticModel;
        if (root == null || semanticModel == null) return false;

        var relevantSpan = TextSpan.FromBounds(
            start.Syntax.SpanStart + 1,
            required.Syntax.SpanStart);
        foreach (var throwSyntax in root.Syntax.DescendantNodes(relevantSpan)
                     .Where(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax))
        {
            if (throwSyntax.SpanStart <= start.Syntax.SpanStart ||
                throwSyntax.SpanStart >= required.Syntax.SpanStart)
            {
                continue;
            }

            var throwOperation = semanticModel.GetOperation(throwSyntax) as IThrowOperation;
            if (throwOperation == null ||
                !start.SharesOwningExecutableRoot(throwOperation) ||
                start.Syntax.Span.Contains(throwSyntax.Span) ||
                !StartCanReachSyntax(start.Syntax, throwSyntax))
            {
                continue;
            }

            if (CaughtThrowSkipsRequired(
                    required,
                    later,
                    throwOperation,
                    throwSyntax,
                    semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CaughtThrowSkipsRequired(
        IOperation required,
        IOperation later,
        IThrowOperation throwOperation,
        SyntaxNode throwSyntax,
        SemanticModel semanticModel,
        IReadOnlyList<ITypeSymbol>? inheritedExactThrownTypes = null,
        ITypeSymbol? inheritedOpenThrownType = null)
    {
        List<ITypeSymbol>? remainingExactThrownTypes = null;
        ITypeSymbol? remainingOpenThrownType = inheritedOpenThrownType;
        if (inheritedExactThrownTypes != null)
        {
            remainingExactThrownTypes = new List<ITypeSymbol>(inheritedExactThrownTypes);
            remainingOpenThrownType = null;
        }
        else if (remainingOpenThrownType == null)
        {
            var exactThrownTypes = new List<ITypeSymbol>();
            if (TryCollectExactThrownTypes(throwOperation.Exception, exactThrownTypes) &&
                exactThrownTypes.Count > 0)
            {
                remainingExactThrownTypes = exactThrownTypes;
            }
            else
            {
                remainingOpenThrownType = GetThrownType(throwOperation, throwSyntax, semanticModel);
            }
        }

        foreach (var trySyntax in throwSyntax.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!trySyntax.Block.Span.Contains(throwSyntax.SpanStart)) continue;
            if (later.Syntax.SpanStart <= trySyntax.Span.End) continue;

            var requiredInTry = trySyntax.Block.Span.Contains(required.Syntax.SpanStart);
            var requiredCatch = trySyntax.Catches.FirstOrDefault(catchClause =>
                catchClause.Block.Span.Contains(required.Syntax.SpanStart));
            foreach (var catchClause in trySyntax.Catches)
            {
                var filterDefinitelyHandles = catchClause.Filter == null;
                if (catchClause.Filter?.FilterExpression is { } filterExpression)
                {
                    var constant = semanticModel.GetConstantValue(filterExpression);
                    if (constant.HasValue && constant.Value is false) continue;
                    filterDefinitelyHandles = constant.HasValue && constant.Value is true;
                }

                var caughtType = catchClause.Declaration == null
                    ? null
                    : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
                List<ITypeSymbol>? caughtExactTypes = null;
                if (remainingExactThrownTypes != null)
                {
                    caughtExactTypes = remainingExactThrownTypes
                        .Where(candidateType => CanCatchExactType(candidateType, caughtType, catchClause))
                        .ToList();
                }

                var catchesOpenType = remainingOpenThrownType != null &&
                                      CanPossiblyCatchOpenType(
                                          remainingOpenThrownType,
                                          caughtType,
                                          catchClause);
                if ((caughtExactTypes == null || caughtExactTypes.Count == 0) && !catchesOpenType)
                    continue;

                var handlerSkipsLater = StatementSkipsLater(catchClause.Block, later.Syntax);
                var requiredInHandler = ReferenceEquals(requiredCatch, catchClause);
                var requiredInTryOrAnotherHandler =
                    requiredInTry || (requiredCatch != null && !requiredInHandler);

                if (!requiredInHandler && !handlerSkipsLater)
                {
                    if (requiredInTryOrAnotherHandler)
                        return true;

                    if (required.Syntax.SpanStart > trySyntax.Span.End &&
                        StatementSkipsLater(catchClause.Block, required.Syntax))
                    {
                        return true;
                    }
                }

                if (CatchThrowsSkipRequired(
                        catchClause,
                        semanticModel,
                        required,
                        later,
                        caughtExactTypes,
                        catchesOpenType ? BoundCaughtOpenType(remainingOpenThrownType!, caughtType) : null))
                {
                    return true;
                }

                if (!filterDefinitelyHandles) continue;

                if (caughtExactTypes != null)
                {
                    foreach (var caughtExactType in caughtExactTypes)
                        remainingExactThrownTypes!.Remove(caughtExactType);
                }

                if (catchesOpenType &&
                    CanDefinitelyCatchOpenType(remainingOpenThrownType!, caughtType, catchClause))
                {
                    remainingOpenThrownType = null;
                }
            }

            if ((remainingExactThrownTypes == null || remainingExactThrownTypes.Count == 0) &&
                remainingOpenThrownType == null)
            {
                return false;
            }
        }

        return false;
    }

    private static bool CatchThrowsSkipRequired(
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel,
        IOperation required,
        IOperation later,
        IReadOnlyList<ITypeSymbol>? caughtExactTypes,
        ITypeSymbol? caughtOpenType)
    {
        var catchOperation = semanticModel.GetOperation(catchClause.Block);
        var catchLocal = catchClause.Declaration == null
            ? null
            : semanticModel.GetDeclaredSymbol(catchClause.Declaration);
        if (catchOperation == null) return false;

        foreach (var throwSyntax in catchClause.Block.DescendantNodes()
                     .Where(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax))
        {
            if (!ReferenceEquals(
                    throwSyntax.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault(),
                    catchClause) ||
                semanticModel.GetOperation(throwSyntax) is not IThrowOperation propagatedThrow ||
                !catchOperation.SharesOwningExecutableRoot(propagatedThrow))
            {
                continue;
            }

            if (catchClause.Block.Span.Contains(required.Syntax.SpanStart) &&
                (Dominates(required, propagatedThrow) ||
                 IsRequiredOnPathFrom(catchOperation, required, propagatedThrow)))
            {
                continue;
            }

            var unwrappedException = propagatedThrow.Exception?.UnwrapConversions();
            if (propagatedThrow.Exception == null ||
                (catchLocal != null &&
                 unwrappedException is ILocalReferenceOperation localReference &&
                 SymbolEqualityComparer.Default.Equals(localReference.Local, catchLocal)))
            {
                if (caughtExactTypes is { Count: > 0 } &&
                    CaughtThrowSkipsRequired(
                        required,
                        later,
                        propagatedThrow,
                        throwSyntax,
                        semanticModel,
                        inheritedExactThrownTypes: caughtExactTypes))
                {
                    return true;
                }

                if (caughtOpenType != null &&
                    CaughtThrowSkipsRequired(
                        required,
                        later,
                        propagatedThrow,
                        throwSyntax,
                        semanticModel,
                        inheritedOpenThrownType: caughtOpenType))
                {
                    return true;
                }

                continue;
            }

            if (CaughtThrowSkipsRequired(
                    required,
                    later,
                    propagatedThrow,
                    throwSyntax,
                    semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CatchContainsCaughtThrowSkippingRequired(
        ReattachEntry required,
        IOperation later)
    {
        var catchClause = required.Operation.Syntax.Ancestors()
            .OfType<CatchClauseSyntax>()
            .FirstOrDefault();
        var semanticModel = required.Operation.SemanticModel ??
                            later.SemanticModel ??
                            later.FindOwningExecutableRoot()?.SemanticModel;
        if (catchClause == null || semanticModel == null) return false;

        var catchOperation = semanticModel.GetOperation(catchClause.Block);
        if (catchOperation == null) return false;

        foreach (var throwSyntax in catchClause.Block.DescendantNodes()
                     .Where(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax))
        {
            if (throwSyntax.SpanStart >= required.SpanStart ||
                !ReferenceEquals(
                    throwSyntax.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault(),
                    catchClause) ||
                semanticModel.GetOperation(throwSyntax) is not IThrowOperation throwOperation ||
                !catchOperation.SharesOwningExecutableRoot(throwOperation))
            {
                continue;
            }

            if (CaughtThrowSkipsRequired(
                    required.Operation,
                    later,
                    throwOperation,
                    throwSyntax,
                    semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CatchHandlerCanReachLater(
        CatchClauseSyntax sourceCatch,
        IOperation later)
    {
        var semanticModel = later.SemanticModel ??
                            later.FindOwningExecutableRoot()?.SemanticModel;
        if (semanticModel == null) return false;

        foreach (var throwSyntax in sourceCatch.Block.DescendantNodes()
                     .Where(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax))
        {
            if (!ReferenceEquals(
                    throwSyntax.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault(),
                    sourceCatch) ||
                semanticModel.GetOperation(throwSyntax) is not IThrowOperation throwOperation)
            {
                continue;
            }

            var exactThrownTypes = new List<ITypeSymbol>();
            var hasExactThrownTypes =
                TryCollectExactThrownTypes(throwOperation.Exception, exactThrownTypes) &&
                exactThrownTypes.Count > 0;
            var openThrownType = hasExactThrownTypes
                ? null
                : GetThrownType(throwOperation, throwSyntax, semanticModel);

            foreach (var tryStatement in throwSyntax.Ancestors().OfType<TryStatementSyntax>())
            {
                if (!tryStatement.Block.Span.Contains(throwSyntax.SpanStart) ||
                    later.Syntax.SpanStart <= tryStatement.Span.End)
                {
                    continue;
                }

                foreach (var catchClause in tryStatement.Catches)
                {
                    if (catchClause.Filter?.FilterExpression is { } filterExpression)
                    {
                        var constant = semanticModel.GetConstantValue(filterExpression);
                        if (constant.HasValue && constant.Value is false) continue;
                    }

                    var caughtType = catchClause.Declaration == null
                        ? null
                        : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
                    var canCatch = hasExactThrownTypes
                        ? exactThrownTypes.Any(candidate =>
                            CanCatchExactType(candidate, caughtType, catchClause))
                        : openThrownType != null &&
                          CanPossiblyCatchOpenType(openThrownType, caughtType, catchClause);
                    if (!canCatch) continue;

                    if (!StatementSkipsLater(catchClause.Block, later.Syntax) ||
                        CatchHandlerCanReachLater(catchClause, later))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool CanPossiblyCatchOpenType(
        ITypeSymbol openThrownType,
        ITypeSymbol? caughtType,
        CatchClauseSyntax catchClause)
    {
        return catchClause.Declaration == null ||
               (caughtType != null &&
                (IsSameOrDerivedFrom(openThrownType, caughtType) ||
                 IsSameOrDerivedFrom(caughtType, openThrownType)));
    }

    private static bool CanDefinitelyCatchOpenType(
        ITypeSymbol openThrownType,
        ITypeSymbol? caughtType,
        CatchClauseSyntax catchClause)
    {
        return catchClause.Declaration == null ||
               (caughtType != null && IsSameOrDerivedFrom(openThrownType, caughtType));
    }

    private static ITypeSymbol BoundCaughtOpenType(
        ITypeSymbol openThrownType,
        ITypeSymbol? caughtType)
    {
        return caughtType != null && IsSameOrDerivedFrom(caughtType, openThrownType)
            ? caughtType
            : openThrownType;
    }

    private static bool TryCollectExactThrownTypes(
        IOperation? exception,
        List<ITypeSymbol> exactTypes)
    {
        switch (exception)
        {
            case IObjectCreationOperation { Type: { } objectType }:
                AddExactType(exactTypes, objectType);
                return true;

            case IConversionOperation { OperatorMethod: null } conversion:
                return TryCollectExactThrownTypes(conversion.Operand, exactTypes);

            case IParenthesizedOperation parenthesized:
                return TryCollectExactThrownTypes(parenthesized.Operand, exactTypes);

            case IConditionalOperation conditional:
                {
                    var originalCount = exactTypes.Count;
                    if (TryCollectExactThrownTypes(conditional.WhenTrue, exactTypes) &&
                        TryCollectExactThrownTypes(conditional.WhenFalse, exactTypes))
                    {
                        return true;
                    }

                    exactTypes.RemoveRange(originalCount, exactTypes.Count - originalCount);
                    return false;
                }

            default:
                return false;
        }
    }

    private static void AddExactType(List<ITypeSymbol> exactTypes, ITypeSymbol candidate)
    {
        if (!exactTypes.Any(existing => SymbolEqualityComparer.Default.Equals(existing, candidate)))
            exactTypes.Add(candidate);
    }

    private static bool CanCatchExactType(
        ITypeSymbol exactThrownType,
        ITypeSymbol? caughtType,
        CatchClauseSyntax catchClause)
    {
        return catchClause.Declaration == null ||
               (caughtType != null && IsSameOrDerivedFrom(exactThrownType, caughtType));
    }

    private static ITypeSymbol? GetThrownType(
        IThrowOperation throwOperation,
        SyntaxNode throwSyntax,
        SemanticModel semanticModel)
    {
        if (throwOperation.Exception?.Type is { } explicitType)
            return explicitType;

        if (throwSyntax is not ThrowStatementSyntax { Expression: null })
            return null;

        var enclosingCatch = throwSyntax.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault();
        if (enclosingCatch == null)
            return null;

        return enclosingCatch.Declaration is { } declaration
            ? semanticModel.GetTypeInfo(declaration.Type).Type
            : semanticModel.Compilation.GetTypeByMetadataName("System.Exception");
    }

    private static bool StartCanReachSyntax(SyntaxNode startSyntax, SyntaxNode laterSyntax)
    {
        foreach (var ifStatement in laterSyntax.Ancestors().OfType<IfStatementSyntax>())
        {
            if (!ifStatement.Span.Contains(startSyntax.SpanStart)) continue;
            if (!SameIfBranchContains(ifStatement, laterSyntax, startSyntax)) return false;
        }

        foreach (var switchSection in laterSyntax.Ancestors().OfType<SwitchSectionSyntax>())
        {
            if (switchSection.Parent is not SwitchStatementSyntax switchStatement ||
                !switchStatement.Span.Contains(startSyntax.SpanStart))
            {
                continue;
            }

            if (!switchSection.Span.Contains(startSyntax.SpanStart)) return false;
        }

        foreach (var conditional in laterSyntax.Ancestors().OfType<ConditionalExpressionSyntax>())
        {
            if (!conditional.Span.Contains(startSyntax.SpanStart)) continue;
            if (!SameConditionalArmContains(conditional, laterSyntax, startSyntax)) return false;
        }

        foreach (var switchExpression in laterSyntax.Ancestors().OfType<SwitchExpressionSyntax>())
        {
            if (!switchExpression.Span.Contains(startSyntax.SpanStart)) continue;

            var laterArm = switchExpression.Arms.FirstOrDefault(arm =>
                arm.Span.Contains(laterSyntax.SpanStart));
            var startArm = switchExpression.Arms.FirstOrDefault(arm =>
                arm.Span.Contains(startSyntax.SpanStart));
            if (laterArm != null && startArm != null && !ReferenceEquals(laterArm, startArm))
                return false;
        }

        return true;
    }

    private static bool CanCatch(
        ITypeSymbol? thrownType,
        ITypeSymbol? caughtType,
        IThrowOperation throwOperation,
        CatchClauseSyntax catchClause)
    {
        if (catchClause.Declaration == null) return true;

        if (throwOperation.Exception?.Syntax is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.Type.ToString() == catchClause.Declaration.Type.ToString())
        {
            return true;
        }

        if (caughtType == null || thrownType == null) return false;
        return IsSameOrDerivedFrom(thrownType, caughtType);
    }

    private static bool IsSameOrDerivedFrom(ITypeSymbol candidate, ITypeSymbol expectedBase)
    {
        for (var current = candidate as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expectedBase) ||
                (current.MetadataName == expectedBase.MetadataName &&
                 current.ContainingNamespace?.ToDisplayString() == expectedBase.ContainingNamespace?.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameIfBranchContains(
        IfStatementSyntax ifStatement,
        SyntaxNode requiredSyntax,
        SyntaxNode startSyntax)
    {
        var requiredStart = requiredSyntax.SpanStart;
        if (ifStatement.Statement.Span.Contains(requiredStart))
            return ifStatement.Statement.Span.Contains(startSyntax.SpanStart);

        if (ifStatement.Else?.Statement.Span.Contains(requiredStart) == true)
            return ifStatement.Else.Statement.Span.Contains(startSyntax.SpanStart);

        return true;
    }

    private static bool SameConditionalArmContains(
        ConditionalExpressionSyntax conditional,
        SyntaxNode requiredSyntax,
        SyntaxNode startSyntax)
    {
        var requiredStart = requiredSyntax.SpanStart;
        if (conditional.WhenTrue.Span.Contains(requiredStart))
            return conditional.WhenTrue.Span.Contains(startSyntax.SpanStart);

        if (conditional.WhenFalse.Span.Contains(requiredStart))
            return conditional.WhenFalse.Span.Contains(startSyntax.SpanStart);

        return true;
    }

    private static bool IsNestedUnderOptionalControlFlow(IOperation operation, IBlockOperation enclosingBlock, IOperation later)
    {
        for (var ancestor = operation.Syntax.Parent; ancestor != null && !ReferenceEquals(ancestor, enclosingBlock.Syntax); ancestor = ancestor.Parent)
        {
            if (ancestor is IfStatementSyntax ifStatement)
            {
                if (IfStatementMakesBranchMandatory(ifStatement, operation.Syntax, later.Syntax))
                    continue;

                return true;
            }

            if (ancestor.IsKind(SyntaxKind.ElseClause))
                continue;

            if (ancestor is DoStatementSyntax doStatement)
            {
                if (DoStatementMakesOperationMandatory(doStatement, operation, later))
                    continue;

                return true;
            }

            if (
                ancestor.IsKind(SyntaxKind.SwitchStatement) ||
                ancestor.IsKind(SyntaxKind.SwitchSection) ||
                ancestor.IsKind(SyntaxKind.WhileStatement) ||
                ancestor.IsKind(SyntaxKind.ForStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachVariableStatement) ||
                ancestor.IsKind(SyntaxKind.ConditionalExpression))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoStatementMakesOperationMandatory(
        DoStatementSyntax doStatement,
        IOperation operation,
        IOperation later)
    {
        if (later.Syntax.SpanStart <= doStatement.Span.End) return false;

        var semanticModel = operation.SemanticModel ??
                            later.SemanticModel ??
                            later.FindOwningExecutableRoot()?.SemanticModel;
        if (semanticModel?.GetOperation(doStatement.Statement) is not IOperation bodyOperation)
            return false;

        if (HasReachableBranchSkippingRequired(bodyOperation, operation, later))
            return false;

        if (HasPotentiallyThrowingOperationSkippingRequired(bodyOperation, operation, later))
            return false;

        var relevantSpan = TextSpan.FromBounds(
            doStatement.Statement.SpanStart + 1,
            operation.Syntax.SpanStart);
        foreach (var throwSyntax in doStatement.Statement.DescendantNodes(relevantSpan)
                     .Where(syntax => syntax is ThrowStatementSyntax or ThrowExpressionSyntax))
        {
            if (throwSyntax.SpanStart >= operation.Syntax.SpanStart ||
                semanticModel.GetOperation(throwSyntax) is not IThrowOperation throwOperation ||
                !bodyOperation.SharesOwningExecutableRoot(throwOperation))
            {
                continue;
            }

            if (CaughtThrowSkipsRequired(
                    operation,
                    later,
                    throwOperation,
                    throwSyntax,
                    semanticModel))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IfStatementMakesBranchMandatory(
        IfStatementSyntax ifStatement,
        SyntaxNode operationSyntax,
        SyntaxNode laterSyntax)
    {
        if (laterSyntax.SpanStart <= ifStatement.Span.End) return false;

        var operationStart = operationSyntax.SpanStart;
        if (ifStatement.Statement.Span.Contains(operationStart))
            return ifStatement.Else?.Statement is { } elseStatement && StatementSkipsLater(elseStatement, laterSyntax);

        if (ifStatement.Else?.Statement.Span.Contains(operationStart) == true)
            return StatementSkipsLater(ifStatement.Statement, laterSyntax);

        return false;
    }

    private static bool StatementSkipsLater(StatementSyntax statement, SyntaxNode laterSyntax)
    {
        switch (statement)
        {
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
                return true;

            case BlockSyntax block:
                return block.Statements.Count > 0 && StatementSkipsLater(block.Statements[block.Statements.Count - 1], laterSyntax);

            case IfStatementSyntax ifStatement:
                return ifStatement.Else?.Statement is { } elseStatement &&
                       StatementSkipsLater(ifStatement.Statement, laterSyntax) &&
                       StatementSkipsLater(elseStatement, laterSyntax);

            case BreakStatementSyntax breakStatement:
                return BranchSkipsLater(breakStatement, laterSyntax, includeSwitch: true);

            case ContinueStatementSyntax continueStatement:
                return BranchSkipsLater(continueStatement, laterSyntax, includeSwitch: false);

            case GotoStatementSyntax gotoStatement:
                return GotoSkipsLater(gotoStatement, laterSyntax);

            default:
                return false;
        }
    }

    private static bool GotoSkipsLater(GotoStatementSyntax gotoStatement, SyntaxNode laterSyntax)
    {
        if (gotoStatement.Expression is not IdentifierNameSyntax targetIdentifier) return false;

        var target = gotoStatement.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<LabeledStatementSyntax>()
            .FirstOrDefault(label => label.Identifier.ValueText == targetIdentifier.Identifier.ValueText);
        return target != null && target.SpanStart > laterSyntax.SpanStart;
    }

    private static bool BranchSkipsLater(StatementSyntax branchStatement, SyntaxNode laterSyntax, bool includeSwitch)
    {
        var enclosingConstruct = branchStatement.Ancestors().FirstOrDefault(a =>
            a.IsKind(SyntaxKind.WhileStatement) ||
            a.IsKind(SyntaxKind.DoStatement) ||
            a.IsKind(SyntaxKind.ForStatement) ||
            a.IsKind(SyntaxKind.ForEachStatement) ||
            a.IsKind(SyntaxKind.ForEachVariableStatement) ||
            (includeSwitch && a.IsKind(SyntaxKind.SwitchStatement)));

        return enclosingConstruct != null &&
               laterSyntax.AncestorsAndSelf().Contains(enclosingConstruct) &&
               laterSyntax.SpanStart > branchStatement.SpanStart;
    }
}
