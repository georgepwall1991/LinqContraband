using System.Linq;
using System.Text;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private const int MaxLocalResolutionDepth = 32;

    private static bool IsOwnedBySpecificRawSqlAnalyzer(IOperation operation, IOperation? executableRoot)
    {
        var current = operation.UnwrapConversions();

        if (current is IInterpolatedStringOperation)
            return true;

        return current is IBinaryOperation binary &&
               binary.OperatorKind == BinaryOperatorKind.Add &&
               IsConcatWithNonConstant(binary, executableRoot, depth: 0);
    }

    private static bool IsConstructedRawSql(IOperation operation, IOperation? executableRoot)
    {
        return IsConstructedRawSql(operation, executableRoot, depth: 0);
    }

    private static bool IsConstructedRawSql(IOperation operation, IOperation? executableRoot, int depth)
    {
        var current = operation.UnwrapConversions();

        if (current.ConstantValue.HasValue)
            return false;

        return current switch
        {
            IInterpolatedStringOperation => true,
            IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add => IsConcatWithNonConstant(binary, executableRoot, depth + 1),
            IInvocationOperation invocation => IsSuspiciousInvocation(invocation, executableRoot, depth + 1),
            ILocalReferenceOperation localReference => TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue) &&
                                                        depth < MaxLocalResolutionDepth &&
                                                        IsConstructedRawSql(resolvedValue, executableRoot, depth + 1),
            _ => false
        };
    }

    private static bool IsConcatWithNonConstant(IBinaryOperation binary, IOperation? executableRoot, int depth)
    {
        return IsNonConstant(binary.LeftOperand, executableRoot, depth + 1) ||
               IsNonConstant(binary.RightOperand, executableRoot, depth + 1);
    }

    private static bool IsNonConstant(IOperation operation, IOperation? executableRoot, int depth)
    {
        var current = operation.UnwrapConversions();
        if (current.ConstantValue.HasValue)
            return false;

        return current switch
        {
            IInterpolatedStringOperation => true,
            IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add => IsConcatWithNonConstant(binary, executableRoot, depth + 1),
            IInvocationOperation invocation => IsSuspiciousInvocation(invocation, executableRoot, depth + 1),
            ILocalReferenceOperation localReference => TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue) &&
                                                        depth < MaxLocalResolutionDepth &&
                                                        IsConstructedRawSql(resolvedValue, executableRoot, depth + 1),
            IFieldReferenceOperation => true,
            IPropertyReferenceOperation => true,
            _ => true
        };
    }

    private static bool IsStringBuilderAppendArgumentNonConstant(IOperation operation, IOperation? executableRoot, int depth)
    {
        var current = operation.UnwrapConversions();
        if (current.ConstantValue.HasValue)
            return false;

        if (current is ILocalReferenceOperation localReference)
        {
            if (depth >= MaxLocalResolutionDepth)
                return true;

            if (HasLatestNonConstantLocalWriteBeforeReference(localReference.Local, localReference, executableRoot, depth + 1))
                return true;

            if (executableRoot != null &&
                HasGuaranteedConstantLocalWriteInSameIterationBeforeReference(localReference.Local, localReference, executableRoot, depth + 1))
            {
                return false;
            }

            if (HasNonConstantLoopCarriedLocalWrite(localReference.Local, localReference, executableRoot, depth + 1))
                return true;

            if (HasOnlyConstantLocalWritesBeforeReference(localReference.Local, localReference, executableRoot, depth + 1))
                return false;

            if (executableRoot != null &&
                TryResolveGuaranteedLocalValue(localReference.Local, localReference, executableRoot, out var guaranteedValue, out _, out _))
            {
                return IsStringBuilderAppendArgumentNonConstant(guaranteedValue, executableRoot, depth + 1);
            }

            if (TryResolveLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue))
                return IsStringBuilderAppendArgumentNonConstant(resolvedValue, executableRoot, depth + 1);

            return true;
        }

        if (current is IInvocationOperation invocation)
        {
            if (IsStringConcat(invocation.TargetMethod) ||
                IsStringFormat(invocation.TargetMethod) ||
                IsStringBuilderToString(invocation))
            {
                return IsSuspiciousInvocation(invocation, executableRoot, depth + 1);
            }

            return true;
        }

        return IsNonConstant(current, executableRoot, depth + 1);
    }

    private static bool HasOnlyConstantLocalWritesBeforeReference(
        ILocalSymbol local,
        IOperation reference,
        IOperation? executableRoot,
        int depth)
    {
        if (executableRoot == null || depth >= MaxLocalResolutionDepth)
            return false;

        var sawWrite = false;
        var referenceStart = reference.Syntax.SpanStart;
        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                        declarator.Initializer == null ||
                        !IsRelevantWriteForReference(declarator, reference) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        !CanRelevantWriteReachReference(declarator, reference, referenceStart))
                        continue;

                    if (IsStringBuilderAppendArgumentNonConstant(declarator.Initializer.Value, executableRoot, depth + 1))
                        return false;

                    sawWrite = true;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                IsRelevantWriteForReference(assignment, reference) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                CanRelevantWriteReachReference(assignment, reference, referenceStart))
            {
                if (IsStringBuilderAppendArgumentNonConstant(assignment.Value, executableRoot, depth + 1))
                    return false;

                sawWrite = true;
            }

            if (descendant is ICompoundAssignmentOperation compoundAssignment &&
                compoundAssignment.Target.UnwrapConversions() is ILocalReferenceOperation compoundTargetLocal &&
                SymbolEqualityComparer.Default.Equals(compoundTargetLocal.Local, local) &&
                IsRelevantWriteForReference(compoundAssignment, reference) &&
                ReferenceEquals(compoundAssignment.FindOwningExecutableRoot(), executableRoot) &&
                CanRelevantWriteReachReference(compoundAssignment, reference, referenceStart))
            {
                if (IsStringBuilderAppendArgumentNonConstant(GetCompoundAssignmentRightValue(compoundAssignment), executableRoot, depth + 1) ||
                    !HasOnlyConstantLocalWritesBeforeReference(local, compoundAssignment, executableRoot, depth + 1))
                {
                    return false;
                }

                sawWrite = true;
            }
        }

        return sawWrite;
    }

    private static bool HasLatestNonConstantLocalWriteBeforeReference(
        ILocalSymbol local,
        IOperation reference,
        IOperation? executableRoot,
        int depth)
    {
        if (executableRoot == null || depth >= MaxLocalResolutionDepth)
            return false;

        var referenceStart = reference.Syntax.SpanStart;
        var latestWriteStart = -1;
        var latestWriteIsNonConstant = false;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                        declarator.Initializer == null ||
                        !IsWriteBeforeReference(declarator, referenceStart) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        !CanOperationReachReference(declarator, referenceStart))
                    {
                        continue;
                    }

                    TrackWrite(
                        declarator,
                        IsStringBuilderAppendArgumentNonConstant(declarator.Initializer.Value, executableRoot, depth + 1));
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                IsWriteBeforeReference(assignment, referenceStart) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                CanOperationReachReference(assignment, referenceStart))
            {
                TrackWrite(
                    assignment,
                    IsStringBuilderAppendArgumentNonConstant(assignment.Value, executableRoot, depth + 1));
            }

            if (descendant is ICompoundAssignmentOperation compoundAssignment &&
                compoundAssignment.Target.UnwrapConversions() is ILocalReferenceOperation compoundTargetLocal &&
                SymbolEqualityComparer.Default.Equals(compoundTargetLocal.Local, local) &&
                IsWriteBeforeReference(compoundAssignment, referenceStart) &&
                ReferenceEquals(compoundAssignment.FindOwningExecutableRoot(), executableRoot) &&
                CanOperationReachReference(compoundAssignment, referenceStart))
            {
                TrackWrite(
                    compoundAssignment,
                    IsStringBuilderAppendArgumentNonConstant(GetCompoundAssignmentRightValue(compoundAssignment), executableRoot, depth + 1) ||
                    !HasOnlyConstantLocalWritesBeforeReference(local, compoundAssignment, executableRoot, depth + 1));
            }
        }

        return latestWriteIsNonConstant;

        void TrackWrite(IOperation writeOperation, bool isNonConstant)
        {
            var writeStart = writeOperation.Syntax.SpanStart;
            if (writeStart <= latestWriteStart)
                return;

            latestWriteStart = writeStart;
            latestWriteIsNonConstant = isNonConstant;
        }
    }

    private static bool HasNonConstantLoopCarriedLocalWrite(
        ILocalSymbol local,
        IOperation reference,
        IOperation? executableRoot,
        int depth)
    {
        if (executableRoot == null || depth >= MaxLocalResolutionDepth)
            return false;

        if (HasGuaranteedConstantLocalWriteInSameIterationBeforeReference(local, reference, executableRoot, depth + 1) &&
            !HasLatestNonConstantLocalWriteBeforeReference(local, reference, executableRoot, depth + 1))
        {
            return false;
        }

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ISimpleAssignmentOperation assignment ||
                assignment.Target.UnwrapConversions() is not ILocalReferenceOperation targetLocal ||
                !SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) ||
                !ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) ||
                !IsLoopCarriedWriteForReference(assignment, reference) ||
                !CanWriteReachLaterLoopIteration(assignment, reference))
            {
                continue;
            }

            if (IsStringBuilderAppendArgumentNonConstant(assignment.Value, executableRoot, depth + 1))
                return true;
        }

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not ICompoundAssignmentOperation compoundAssignment ||
                compoundAssignment.Target.UnwrapConversions() is not ILocalReferenceOperation targetLocal ||
                !SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) ||
                !ReferenceEquals(compoundAssignment.FindOwningExecutableRoot(), executableRoot) ||
                !IsLoopCarriedWriteForReference(compoundAssignment, reference) ||
                !CanWriteReachLaterLoopIteration(compoundAssignment, reference))
            {
                continue;
            }

            if (IsStringBuilderAppendArgumentNonConstant(GetCompoundAssignmentRightValue(compoundAssignment), executableRoot, depth + 1) ||
                !HasOnlyConstantLocalWritesBeforeReference(local, compoundAssignment, executableRoot, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGuaranteedConstantLocalWriteInSameIterationBeforeReference(
        ILocalSymbol local,
        IOperation reference,
        IOperation executableRoot,
        int depth)
    {
        foreach (var loop in reference.Syntax.Ancestors().Where(IsLoopSyntax))
        {
            foreach (var descendant in executableRoot.Descendants())
            {
                if (descendant is IVariableDeclarationOperation declaration)
                {
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                            declarator.Initializer == null ||
                            !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                            !IsWriteBeforeReference(declarator, reference.Syntax.SpanStart) ||
                            !ContainsNode(loop, declarator.Syntax) ||
                            IsStringBuilderAppendArgumentNonConstant(declarator.Initializer.Value, executableRoot, depth + 1))
                        {
                            continue;
                        }

                        if (IsEarlierStatementInSameBlock(declarator.Syntax, reference.Syntax))
                            return true;
                    }
                }

                if (descendant is ISimpleAssignmentOperation assignment &&
                    assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                    SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                    ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                    IsWriteBeforeReference(assignment, reference.Syntax.SpanStart) &&
                    ContainsNode(loop, assignment.Syntax) &&
                    !IsStringBuilderAppendArgumentNonConstant(assignment.Value, executableRoot, depth + 1) &&
                    IsEarlierStatementInSameBlock(assignment.Syntax, reference.Syntax))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IOperation GetCompoundAssignmentRightValue(ICompoundAssignmentOperation compoundAssignment)
    {
        if (compoundAssignment.Syntax is AssignmentExpressionSyntax assignmentSyntax)
        {
            var right = assignmentSyntax.Right;
            var rightOperation = compoundAssignment
                .Descendants()
                .FirstOrDefault(operation => ReferenceEquals(operation.Syntax, right));

            if (rightOperation != null)
                return rightOperation;
        }

        return compoundAssignment.Value;
    }

    private static bool IsSuspiciousInvocation(IInvocationOperation invocation, IOperation? executableRoot, int depth)
    {
        var method = invocation.TargetMethod;

        if (IsStringFormat(method))
            return invocation.Arguments.Any(arg => !arg.Value.UnwrapConversions().ConstantValue.HasValue);

        if (IsStringConcat(method))
            return invocation.Arguments.Any(arg => IsNonConstant(arg.Value, executableRoot, depth + 1));

        if (IsStringBuilderToString(invocation))
            return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot, depth + 1);

        return false;
    }

    private static bool IsStringFormat(IMethodSymbol method)
    {
        return method.Name == "Format" &&
               method.ContainingType.Name == "String" &&
               method.ContainingNamespace?.ToString() == "System";
    }

    private static bool IsStringConcat(IMethodSymbol method)
    {
        return method.Name == "Concat" &&
               method.ContainingType.Name == "String" &&
               method.ContainingNamespace?.ToString() == "System";
    }

    private static bool IsStringBuilderToString(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "ToString" &&
               invocation.GetInvocationReceiver()?.Type is INamedTypeSymbol receiverType &&
               receiverType.Name == "StringBuilder" &&
               receiverType.ContainingNamespace?.ToString() == "System.Text";
    }

    private static bool ContainsSuspiciousStringBuilderAppend(IOperation? receiver, IOperation? executableRoot, int depth)
    {
        if (receiver == null)
            return false;

        var current = receiver.UnwrapConversions();

        if (current is IInvocationOperation invocation)
        {
            if (IsStringBuilderAppend(invocation.TargetMethod))
            {
                if (invocation.Arguments.Any(arg => IsStringBuilderAppendArgumentNonConstant(arg.Value, executableRoot, depth + 1)))
                    return true;

                return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot, depth + 1);
            }

            if (IsStringBuilderClear(invocation.TargetMethod))
                return false;

            return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot, depth + 1);
        }

        if (current is ILocalReferenceOperation localReference)
        {
            return ContainsSuspiciousStringBuilderStatementAppend(localReference, executableRoot, depth + 1, localReference.Syntax.SpanStart);
        }

        if (current is IConditionalOperation conditional)
        {
            return (conditional.WhenTrue != null &&
                    ContainsSuspiciousStringBuilderAppend(conditional.WhenTrue, executableRoot, depth + 1)) ||
                   (conditional.WhenFalse != null &&
                    ContainsSuspiciousStringBuilderAppend(conditional.WhenFalse, executableRoot, depth + 1));
        }

        if (current is IObjectCreationOperation objectCreation &&
            objectCreation.Type is INamedTypeSymbol objectType &&
            objectType.Name == nameof(StringBuilder) &&
            objectType.ContainingNamespace?.ToString() == "System.Text")
        {
            return objectCreation.Arguments.Any(arg =>
                arg.Parameter?.Type.SpecialType == SpecialType.System_String &&
                IsStringBuilderAppendArgumentNonConstant(arg.Value, executableRoot, depth + 1));
        }

        return false;
    }

    private static bool IsRelevantWriteForReference(IOperation writeOperation, IOperation reference)
    {
        var referenceStart = reference.Syntax.SpanStart;
        return IsWriteBeforeReference(writeOperation, referenceStart) ||
               IsLoopCarriedWriteForReference(writeOperation, reference);
    }

    private static bool IsLoopCarriedWriteForReference(IOperation writeOperation, IOperation reference)
    {
        if (writeOperation.Syntax.SpanStart <= reference.Syntax.SpanStart)
            return false;

        var current = reference.Parent;
        while (current != null)
        {
            if (current is ILoopOperation loop &&
                ContainsNode(loop.Syntax, writeOperation.Syntax))
            {
                return true;
            }

            current = current.Parent;
        }

        foreach (var loop in reference.Syntax.Ancestors().Where(IsLoopSyntax))
        {
            if (ContainsNode(loop, writeOperation.Syntax))
                return true;
        }

        return false;
    }

    private static bool CanRelevantWriteReachReference(IOperation writeOperation, IOperation reference, int referenceStart)
    {
        return CanOperationReachReference(writeOperation, referenceStart) ||
               IsLoopCarriedWriteForReference(writeOperation, reference);
    }

    private static bool IsLoopSyntax(SyntaxNode node)
    {
        return node is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
    }

    private static bool CanWriteReachLaterLoopIteration(IOperation writeOperation, IOperation reference)
    {
        foreach (var loop in reference.Syntax.Ancestors().Where(IsLoopSyntax))
        {
            if (!ContainsNode(loop, writeOperation.Syntax))
                continue;

            foreach (var block in writeOperation.Syntax.Ancestors().OfType<BlockSyntax>())
            {
                if (!ContainsNode(loop, block) ||
                    !BlockTerminatesAfterNode(
                        block,
                        writeOperation.Syntax,
                        loop.Span.End))
                    continue;

                return false;
            }

            foreach (var switchSection in writeOperation.Syntax.Ancestors().OfType<SwitchSectionSyntax>())
            {
                if (!ContainsNode(loop, switchSection) ||
                    !SwitchSectionTerminatesAfterNode(
                        switchSection,
                        writeOperation.Syntax,
                        loop.Span.End))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        return false;
    }

    private static bool ContainsSuspiciousStringBuilderStatementAppend(
        ILocalReferenceOperation builderReference,
        IOperation? executableRoot,
        int depth,
        int referenceStart)
    {
        if (executableRoot == null || depth >= MaxLocalResolutionDepth)
            return false;

        var latestGuaranteedResetEnd = GetLatestGuaranteedStringBuilderReset(
            builderReference,
            executableRoot,
            referenceStart,
            out var resetIsValueWrite);

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is not IInvocationOperation invocation)
                continue;

            if (!IsStringBuilderAppend(invocation.TargetMethod))
                continue;

            if (invocation.Syntax.Span.End > referenceStart ||
                invocation.Syntax.Span.End <= latestGuaranteedResetEnd ||
                !ReferenceEquals(invocation.FindOwningExecutableRoot(), executableRoot) ||
                !CanOperationReachReference(invocation, referenceStart))
                continue;

            if (!IsInvocationOnStringBuilderLocal(invocation, builderReference, executableRoot, depth + 1))
                continue;

            if (invocation.Arguments.Any(arg => IsStringBuilderAppendArgumentNonConstant(arg.Value, executableRoot, depth + 1)))
                return true;
        }

        if (!resetIsValueWrite ||
            !TryResolveLocalValue(builderReference.Local, builderReference, executableRoot, out var resolvedValue))
            return false;

        if (resolvedValue.UnwrapConversions() is ILocalReferenceOperation resolvedLocalReference)
            return ContainsSuspiciousStringBuilderStatementAppend(resolvedLocalReference, executableRoot, depth + 1, referenceStart);

        return ContainsSuspiciousStringBuilderAppend(resolvedValue, executableRoot, depth + 1);
    }

    private static bool CanOperationReachReference(IOperation operation, int referenceStart)
    {
        foreach (var block in operation.Syntax.Ancestors().OfType<BlockSyntax>())
        {
            if (block.Span.End > referenceStart)
                continue;

            if (BlockTerminatesAfterNode(
                    block,
                    operation.Syntax,
                    referenceStart))
                return false;
        }

        foreach (var switchSection in operation.Syntax.Ancestors().OfType<SwitchSectionSyntax>())
        {
            if (switchSection.Span.End <= referenceStart &&
                SwitchSectionTerminatesAfterNode(
                    switchSection,
                    operation.Syntax,
                    referenceStart))
                return false;
        }

        return true;
    }

    private static StatementSyntax? GetEnclosingIfBranch(IfStatementSyntax ifStatement, SyntaxNode node)
    {
        if (ContainsNode(ifStatement.Statement, node))
            return ifStatement.Statement;

        var elseStatement = ifStatement.Else?.Statement;
        return elseStatement != null && ContainsNode(elseStatement, node) ? elseStatement : null;
    }

    private static bool BlockTerminatesAfterNode(BlockSyntax block, SyntaxNode node, int referenceStart)
    {
        for (var i = 0; i < block.Statements.Count; i++)
        {
            var statement = block.Statements[i];
            if (!ContainsNode(statement, node))
                continue;

            return block.Statements
                .Skip(i + 1)
                .Any(statement => StatementExitsMethod(statement, referenceStart));
        }

        return false;
    }

    private static bool SwitchSectionTerminatesAfterNode(
        SwitchSectionSyntax switchSection,
        SyntaxNode node,
        int referenceStart)
    {
        for (var i = 0; i < switchSection.Statements.Count; i++)
        {
            var statement = switchSection.Statements[i];
            if (!ContainsNode(statement, node))
                continue;

            return switchSection.Statements
                .Skip(i + 1)
                .Any(statement => StatementExitsMethod(statement, referenceStart));
        }

        return false;
    }

    private static bool ThrowCanContinueThroughCatch(ThrowStatementSyntax throwStatement, int referenceStart)
    {
        foreach (var tryStatement in throwStatement.Ancestors().OfType<TryStatementSyntax>())
        {
            if (tryStatement.Catches.Count > 0 &&
                tryStatement.Span.End <= referenceStart &&
                ContainsNode(tryStatement.Block, throwStatement) &&
                CatchSequenceCanContinueThroughThrow(tryStatement, throwStatement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CatchSequenceCanContinueThroughThrow(TryStatementSyntax tryStatement, ThrowStatementSyntax throwStatement)
    {
        foreach (var catchClause in tryStatement.Catches)
        {
            if (!CatchMayHandleThrow(catchClause, throwStatement))
                continue;

            var filterValue = GetCatchFilterConstantValue(catchClause.Filter);
            if (filterValue == false)
                continue;

            if (CatchCanReachReference(catchClause))
                return true;

            if (filterValue == true)
                return false;
        }

        return false;
    }

    private static bool CatchCanReachReference(CatchClauseSyntax catchClause)
    {
        return !StatementExitsMethod(catchClause.Block, catchClause.Block.Span.End);
    }

    private static bool CatchMayHandleThrow(CatchClauseSyntax catchClause, ThrowStatementSyntax throwStatement)
    {
        var catchType = GetResolvedSimpleTypeName(catchClause.Declaration?.Type, catchClause);
        if (catchType is null or "Exception")
            return true;

        if (throwStatement.Expression is not ObjectCreationExpressionSyntax objectCreation)
            return true;

        var thrownType = GetResolvedSimpleTypeName(objectCreation.Type, throwStatement);
        return thrownType == null ||
               catchType == thrownType ||
               IsKnownExceptionBase(catchType, thrownType) ||
               HasLocalExceptionBase(thrownType, catchType, throwStatement, depth: 0);
    }

    private static bool? GetCatchFilterConstantValue(CatchFilterClauseSyntax? filter)
    {
        return filter == null ? true : GetBooleanConstantValue(filter.FilterExpression);
    }

    private static bool? GetBooleanConstantValue(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.TrueLiteralExpression) => true,
            LiteralExpressionSyntax literal when literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.FalseLiteralExpression) => false,
            ParenthesizedExpressionSyntax parenthesized => GetBooleanConstantValue(parenthesized.Expression),
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalNotExpression) =>
                GetBooleanConstantValue(prefix.Operand) is { } value ? !value : null,
            _ => null
        };
    }

    private static string? GetSimpleTypeName(TypeSyntax? type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName => GetSimpleTypeName(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName => GetSimpleTypeName(aliasQualifiedName.Name),
            _ => null
        };
    }

    private static string? GetResolvedSimpleTypeName(TypeSyntax? type, SyntaxNode context)
    {
        var simpleName = GetSimpleTypeName(type);
        if (simpleName == null)
            return null;

        var root = context.SyntaxTree.GetRoot();
        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (usingDirective.Alias?.Name.Identifier.ValueText == simpleName)
                return GetSimpleTypeName(usingDirective.Name) ?? simpleName;
        }

        return simpleName;
    }

    private static bool HasLocalExceptionBase(
        string thrownType,
        string catchType,
        SyntaxNode context,
        int depth)
    {
        if (depth >= MaxLocalResolutionDepth)
            return false;

        var root = context.SyntaxTree.GetRoot();
        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (classDeclaration.Identifier.ValueText != thrownType ||
                classDeclaration.BaseList == null)
            {
                continue;
            }

            foreach (var baseType in classDeclaration.BaseList.Types)
            {
                var baseTypeName = GetResolvedSimpleTypeName(baseType.Type, classDeclaration);
                if (baseTypeName == null)
                    continue;

                if (baseTypeName == catchType ||
                    IsKnownExceptionBase(catchType, baseTypeName) ||
                    HasLocalExceptionBase(baseTypeName, catchType, context, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsKnownExceptionBase(string catchType, string thrownType)
    {
        return catchType switch
        {
            "SystemException" => thrownType is
                "AccessViolationException" or
                "ArgumentException" or
                "ArithmeticException" or
                "ArrayTypeMismatchException" or
                "BadImageFormatException" or
                "CannotUnloadAppDomainException" or
                "DivideByZeroException" or
                "DuplicateWaitObjectException" or
                "EndOfStreamException" or
                "ExecutionEngineException" or
                "FileLoadException" or
                "FileNotFoundException" or
                "FormatException" or
                "IndexOutOfRangeException" or
                "InsufficientMemoryException" or
                "InvalidCastException" or
                "InvalidOperationException" or
                "InvalidProgramException" or
                "IOException" or
                "MemberAccessException" or
                "NotFiniteNumberException" or
                "NotImplementedException" or
                "NullReferenceException" or
                "OperationCanceledException" or
                "OutOfMemoryException" or
                "OverflowException" or
                "RankException" or
                "StackOverflowException" or
                "SystemException" or
                "TimeoutException" or
                "TypeLoadException" or
                "UnauthorizedAccessException",
            "ArgumentException" => thrownType is
                "ArgumentNullException" or
                "ArgumentOutOfRangeException" or
                "DuplicateWaitObjectException",
            "ArithmeticException" => thrownType is
                "DivideByZeroException" or
                "NotFiniteNumberException" or
                "OverflowException",
            "IOException" => thrownType is
                "DirectoryNotFoundException" or
                "DriveNotFoundException" or
                "EndOfStreamException" or
                "FileLoadException" or
                "FileNotFoundException" or
                "PathTooLongException",
            "OperationCanceledException" => thrownType is "TaskCanceledException",
            _ => false
        };
    }

    private static bool ContainsNode(SyntaxNode container, SyntaxNode node)
    {
        return container.SpanStart <= node.SpanStart &&
               container.Span.End >= node.Span.End;
    }

    private static bool IsEarlierStatementInSameBlock(SyntaxNode writeNode, SyntaxNode referenceNode)
    {
        var writeStatement = writeNode.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        var referenceStatement = referenceNode.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (writeStatement?.Parent is not BlockSyntax block ||
            !ReferenceEquals(referenceStatement?.Parent, block))
        {
            return false;
        }

        var writeIndex = block.Statements.IndexOf(writeStatement);
        var referenceIndex = block.Statements.IndexOf(referenceStatement);
        return writeIndex >= 0 &&
               referenceIndex >= 0 &&
               writeIndex < referenceIndex;
    }

    private static int GetLatestGuaranteedStringBuilderReset(
        ILocalReferenceOperation builderReference,
        IOperation executableRoot,
        int referenceStart,
        out bool isValueWrite)
    {
        var local = builderReference.Local;
        var latestResetEnd = -1;
        var latestResetIsValueWrite = false;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                        declarator.Initializer == null ||
                        !IsWriteBeforeReference(declarator, referenceStart) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        !IsResetGuaranteedBeforeReference(declarator, executableRoot, referenceStart))
                        continue;

                    TrackReset(declarator.Syntax.Span.End, valueWrite: true);
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                IsWriteBeforeReference(assignment, referenceStart) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                IsResetGuaranteedBeforeReference(assignment, executableRoot, referenceStart))
            {
                var previousIdentity = ResolveLocalIdentity(targetLocal, executableRoot, depth: 0);
                if (MayReferenceIdentity(assignment.Value, previousIdentity, executableRoot, depth: 0))
                    continue;

                TrackReset(assignment.Syntax.Span.End, valueWrite: true);
            }

            if (descendant is IInvocationOperation invocation &&
                IsStringBuilderClear(invocation.TargetMethod) &&
                invocation.Syntax.Span.End <= referenceStart &&
                ReferenceEquals(invocation.FindOwningExecutableRoot(), executableRoot) &&
                IsResetGuaranteedBeforeReference(invocation, executableRoot, referenceStart) &&
                IsInvocationOnStringBuilderLocal(invocation, builderReference, executableRoot, depth: 0, allowMayAlias: false))
            {
                TrackReset(invocation.Syntax.Span.End, valueWrite: false);
            }
        }

        isValueWrite = latestResetIsValueWrite;
        return latestResetEnd;

        void TrackReset(int resetEnd, bool valueWrite)
        {
            if (resetEnd <= latestResetEnd)
                return;

            latestResetEnd = resetEnd;
            latestResetIsValueWrite = valueWrite;
        }
    }

    private static bool IsInvocationOnStringBuilderLocal(
        IInvocationOperation invocation,
        ILocalReferenceOperation builderReference,
        IOperation executableRoot,
        int depth,
        bool allowMayAlias = true)
    {
        var receiver = GetInvocationReceiverForAliasResolution(invocation);

        while (receiver is IInvocationOperation receiverInvocation &&
               (IsStringBuilderAppend(receiverInvocation.TargetMethod) || IsStringBuilderClear(receiverInvocation.TargetMethod)))
        {
            receiver = GetInvocationReceiverForAliasResolution(receiverInvocation);
        }

        return IsSameLocalOrAlias(receiver, builderReference, executableRoot, depth, allowMayAlias);
    }

    private static IOperation? GetInvocationReceiverForAliasResolution(IInvocationOperation invocation)
    {
        var receiver = invocation.GetInvocationReceiver()?.UnwrapConversions();
        if (receiver is not IConditionalAccessInstanceOperation)
            return receiver;

        var parent = invocation.Parent;
        while (parent != null)
        {
            if (parent is IConditionalAccessOperation conditionalAccess &&
                OperationContains(conditionalAccess.WhenNotNull, invocation))
            {
                return conditionalAccess.Operation.UnwrapConversions();
            }

            parent = parent.Parent;
        }

        return receiver;
    }

    private static bool OperationContains(IOperation container, IOperation operation)
    {
        return ReferenceEquals(container, operation) ||
               container.Descendants().Any(descendant => ReferenceEquals(descendant, operation));
    }

    private static bool IsSameLocalOrAlias(
        IOperation? operation,
        ILocalReferenceOperation builderReference,
        IOperation executableRoot,
        int depth,
        bool allowMayAlias)
    {
        if (operation?.UnwrapConversions() is not ILocalReferenceOperation localReference)
            return false;

        var receiverIdentity = ResolveLocalIdentity(localReference, executableRoot, depth);
        var builderIdentity = ResolveLocalIdentity(builderReference, executableRoot, depth);
        if (receiverIdentity.Equals(builderIdentity))
        {
            if (!allowMayAlias &&
                !SymbolEqualityComparer.Default.Equals(localReference.Local, builderReference.Local) &&
                (HasNonGuaranteedWriteAfterLatestGuaranteed(localReference, executableRoot) ||
                 HasNonGuaranteedWriteAfterLatestGuaranteed(builderReference, executableRoot)))
            {
                return false;
            }

            return true;
        }

        return allowMayAlias &&
               (MayResolveToIdentity(localReference, builderIdentity, executableRoot, depth) ||
                MayResolveToIdentity(builderReference, receiverIdentity, executableRoot, depth));
    }

    private static bool HasNonGuaranteedWriteAfterLatestGuaranteed(
        ILocalReferenceOperation localReference,
        IOperation executableRoot)
    {
        var referenceStart = localReference.Syntax.SpanStart;
        var latestGuaranteedWriteStart = -1;
        TryResolveGuaranteedLocalValue(localReference.Local, localReference, executableRoot, out _, out _, out latestGuaranteedWriteStart);

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, localReference.Local) ||
                        declarator.Initializer == null ||
                        declarator.Syntax.SpanStart <= latestGuaranteedWriteStart ||
                        !IsWriteBeforeReference(declarator, referenceStart) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        IsGuaranteedBeforeReference(declarator, executableRoot))
                        continue;

                    return true;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, localReference.Local) &&
                assignment.Syntax.SpanStart > latestGuaranteedWriteStart &&
                IsWriteBeforeReference(assignment, referenceStart) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                !IsGuaranteedBeforeReference(assignment, executableRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static LocalIdentity ResolveLocalIdentity(
        ILocalReferenceOperation localReference,
        IOperation executableRoot,
        int depth)
    {
        if (depth >= MaxLocalResolutionDepth)
            return new LocalIdentity(localReference.Local, writeEnd: -1);

        if (TryResolveGuaranteedLocalValue(localReference.Local, localReference, executableRoot, out var resolvedValue, out var writeEnd, out _) &&
            resolvedValue.UnwrapConversions() is ILocalReferenceOperation resolvedLocalReference)
        {
            return ResolveLocalIdentity(resolvedLocalReference, executableRoot, depth + 1);
        }

        return new LocalIdentity(localReference.Local, writeEnd);
    }

    private static bool MayResolveToIdentity(
        ILocalReferenceOperation localReference,
        LocalIdentity identity,
        IOperation executableRoot,
        int depth)
    {
        if (depth >= MaxLocalResolutionDepth)
            return false;

        if (ResolveLocalIdentity(localReference, executableRoot, depth).Equals(identity))
            return true;

        var latestGuaranteedWriteStart = -1;
        if (TryResolveGuaranteedLocalValue(localReference.Local, localReference, executableRoot, out var guaranteedValue, out _, out latestGuaranteedWriteStart) &&
            MayReferenceIdentity(guaranteedValue, identity, executableRoot, depth + 1))
        {
            return true;
        }

        var referenceStart = localReference.Syntax.SpanStart;
        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, localReference.Local) ||
                        declarator.Initializer == null ||
                        declarator.Syntax.SpanStart <= latestGuaranteedWriteStart ||
                        !IsWriteBeforeReference(declarator, referenceStart) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        IsGuaranteedBeforeReference(declarator, executableRoot))
                        continue;

                    if (MayReferenceIdentity(declarator.Initializer.Value, identity, executableRoot, depth + 1))
                        return true;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, localReference.Local) &&
                assignment.Syntax.SpanStart > latestGuaranteedWriteStart &&
                IsWriteBeforeReference(assignment, referenceStart) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                !IsGuaranteedBeforeReference(assignment, executableRoot) &&
                MayReferenceIdentity(assignment.Value, identity, executableRoot, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MayReferenceIdentity(
        IOperation operation,
        LocalIdentity identity,
        IOperation executableRoot,
        int depth)
    {
        if (depth >= MaxLocalResolutionDepth)
            return false;

        var current = operation.UnwrapConversions();

        if (current is ILocalReferenceOperation localReference)
            return MayResolveToIdentity(localReference, identity, executableRoot, depth + 1);

        if (current is IConditionalOperation conditional)
        {
            return (conditional.WhenTrue != null &&
                    MayReferenceIdentity(conditional.WhenTrue, identity, executableRoot, depth + 1)) ||
                   (conditional.WhenFalse != null &&
                    MayReferenceIdentity(conditional.WhenFalse, identity, executableRoot, depth + 1));
        }

        if (current is IBinaryOperation binary)
        {
            return MayReferenceIdentity(binary.LeftOperand, identity, executableRoot, depth + 1) ||
                   MayReferenceIdentity(binary.RightOperand, identity, executableRoot, depth + 1);
        }

        if (current is IInterpolatedStringOperation interpolatedString)
        {
            return interpolatedString.Parts
                .OfType<IInterpolationOperation>()
                .Any(interpolation => MayReferenceIdentity(interpolation.Expression, identity, executableRoot, depth + 1));
        }

        if (current is IObjectCreationOperation objectCreation)
        {
            return objectCreation.Arguments.Any(arg => MayReferenceIdentity(arg.Value, identity, executableRoot, depth + 1)) ||
                   (objectCreation.Initializer?.Initializers.Any(initializer => MayReferenceIdentity(initializer, identity, executableRoot, depth + 1)) == true);
        }

        if (current is IInvocationOperation invocation)
        {
            var receiver = invocation.GetInvocationReceiver();
            return (receiver != null && MayReferenceIdentity(receiver, identity, executableRoot, depth + 1)) ||
                   invocation.Arguments.Any(arg => MayReferenceIdentity(arg.Value, identity, executableRoot, depth + 1));
        }

        return false;
    }

    private static bool IsDefinitelyExecutedBeforeReference(IOperation operation, IOperation executableRoot)
    {
        return IsGuaranteedBeforeReference(operation, executableRoot) &&
               !IsInsideShortCircuitRightOperand(operation, executableRoot);
    }

    private static bool IsResetGuaranteedBeforeReference(
        IOperation operation,
        IOperation executableRoot,
        int referenceStart)
    {
        return IsDefinitelyExecutedBeforeReference(operation, executableRoot) ||
               IsInsideGuaranteedFinallyBeforeReference(operation, executableRoot, referenceStart) ||
               IsInsideOnlySurvivingIfBranches(operation.Syntax, referenceStart);
    }

    private static bool IsInsideGuaranteedFinallyBeforeReference(
        IOperation operation,
        IOperation executableRoot,
        int referenceStart)
    {
        var current = operation.Parent;
        while (current != null && !ReferenceEquals(current, executableRoot))
        {
            if (current is ITryOperation tryOperation &&
                tryOperation.Finally != null &&
                ContainsNode(tryOperation.Finally.Syntax, operation.Syntax))
            {
                var nested = operation.Parent;
                while (nested != null && !ReferenceEquals(nested, tryOperation))
                {
                    if (nested is IConditionalOperation or ISwitchOperation or ILoopOperation or ITryOperation)
                        return false;

                    nested = nested.Parent;
                }

                return tryOperation.Syntax.Span.End <= referenceStart &&
                       IsDefinitelyExecutedBeforeReference(tryOperation, executableRoot);
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsInsideOnlySurvivingIfBranches(SyntaxNode node, int referenceStart)
    {
        var sawControllingIf = false;
        foreach (var ifStatement in node.Ancestors().OfType<IfStatementSyntax>())
        {
            if (ifStatement.Span.End > referenceStart)
                continue;

            var branch = GetEnclosingIfBranch(ifStatement, node);
            if (branch == null)
                continue;

            if (HasPotentiallySkippingAncestor(ifStatement, referenceStart))
                return false;

            var otherBranch = ReferenceEquals(branch, ifStatement.Statement)
                ? ifStatement.Else?.Statement
                : ifStatement.Statement;

            if (!StatementTerminates(otherBranch, node, referenceStart))
                return false;

            sawControllingIf = true;
        }

        return sawControllingIf;
    }

    private static bool HasPotentiallySkippingAncestor(SyntaxNode node, int referenceStart)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                return false;

            if ((IsLoopSyntax(ancestor) || ancestor is SwitchStatementSyntax or SwitchSectionSyntax) &&
                ContainsPosition(ancestor, referenceStart))
            {
                continue;
            }

            if (IsLoopSyntax(ancestor) || ancestor is
                SwitchStatementSyntax or SwitchSectionSyntax or ConditionalExpressionSyntax or
                TryStatementSyntax or CatchClauseSyntax or FinallyClauseSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPosition(SyntaxNode container, int position)
    {
        return container.SpanStart <= position && container.Span.End >= position;
    }

    private static bool StatementTerminates(StatementSyntax? statement, SyntaxNode survivingNode, int referenceStart)
    {
        return statement switch
        {
            ReturnStatementSyntax or ThrowStatementSyntax => true,
            ContinueStatementSyntax continueStatement => JumpSkipsReference(continueStatement, survivingNode, referenceStart, allowSwitch: false),
            BreakStatementSyntax breakStatement => JumpSkipsReference(breakStatement, survivingNode, referenceStart, allowSwitch: true),
            BlockSyntax block when block.Statements.Count > 0 => StatementTerminates(block.Statements.Last(), survivingNode, referenceStart),
            IfStatementSyntax ifStatement when ifStatement.Else != null =>
                StatementTerminates(ifStatement.Statement, survivingNode, referenceStart) &&
                StatementTerminates(ifStatement.Else.Statement, survivingNode, referenceStart),
            _ => false
        };
    }

    private static bool StatementExitsMethod(StatementSyntax? statement, int referenceStart)
    {
        return statement switch
        {
            ReturnStatementSyntax => true,
            ThrowStatementSyntax throwStatement => !ThrowCanContinueThroughCatch(throwStatement, referenceStart),
            BlockSyntax block when block.Statements.Count > 0 => StatementExitsMethod(block.Statements.Last(), referenceStart),
            IfStatementSyntax ifStatement when ifStatement.Else != null =>
                StatementExitsMethod(ifStatement.Statement, referenceStart) &&
                StatementExitsMethod(ifStatement.Else.Statement, referenceStart),
            _ => false
        };
    }

    private static bool JumpSkipsReference(SyntaxNode jumpStatement, SyntaxNode survivingNode, int referenceStart, bool allowSwitch)
    {
        if (jumpStatement.SpanStart >= referenceStart)
            return false;

        foreach (var ancestor in jumpStatement.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                return false;

            if (IsLoopSyntax(ancestor) || (allowSwitch && ancestor is SwitchStatementSyntax))
                return ContainsNode(ancestor, survivingNode);
        }

        return false;
    }

    private static bool IsInsideShortCircuitRightOperand(IOperation operation, IOperation executableRoot)
    {
        var current = operation;
        var parent = current.Parent;

        while (parent != null && !ReferenceEquals(parent, executableRoot))
        {
            if (parent is IBinaryOperation binary &&
                (binary.OperatorKind == BinaryOperatorKind.ConditionalAnd ||
                 binary.OperatorKind == BinaryOperatorKind.ConditionalOr) &&
                ReferenceEquals(binary.RightOperand, current))
            {
                return true;
            }

            current = parent;
            parent = current.Parent;
        }

        return false;
    }

    private static bool TryResolveGuaranteedLocalValue(
        ILocalSymbol local,
        IOperation reference,
        IOperation executableRoot,
        out IOperation value,
        out int writeEnd,
        out int writeStart)
    {
        value = null!;
        writeEnd = -1;
        writeStart = -1;

        var referenceStart = reference.Syntax.SpanStart;
        var latestWriteStart = -1;
        IOperation? resolvedValue = null;
        var resolvedWriteEnd = -1;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                        declarator.Initializer == null ||
                        !IsWriteBeforeReference(declarator, referenceStart) ||
                        !ReferenceEquals(declarator.FindOwningExecutableRoot(), executableRoot) ||
                        !IsResetGuaranteedBeforeReference(declarator, executableRoot, referenceStart))
                        continue;

                    TrackWrite(declarator, declarator.Initializer.Value);
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                IsWriteBeforeReference(assignment, referenceStart) &&
                ReferenceEquals(assignment.FindOwningExecutableRoot(), executableRoot) &&
                IsResetGuaranteedBeforeReference(assignment, executableRoot, referenceStart))
            {
                TrackWrite(assignment, assignment.Value);
            }
        }

        if (resolvedValue == null)
            return false;

        value = resolvedValue;
        writeEnd = resolvedWriteEnd;
        writeStart = latestWriteStart;
        return true;

        void TrackWrite(IOperation writeOperation, IOperation writtenValue)
        {
            var writeStart = writeOperation.Syntax.SpanStart;
            if (writeStart <= latestWriteStart)
                return;

            latestWriteStart = writeStart;
            resolvedValue = writtenValue;
            resolvedWriteEnd = writeOperation.Syntax.Span.End;
        }
    }

    private readonly struct LocalIdentity
    {
        private readonly ILocalSymbol _local;
        private readonly int _writeEnd;

        public LocalIdentity(ILocalSymbol local, int writeEnd)
        {
            _local = local;
            _writeEnd = writeEnd;
        }

        public bool Equals(LocalIdentity other)
        {
            return _writeEnd == other._writeEnd &&
                   SymbolEqualityComparer.Default.Equals(_local, other._local);
        }
    }

    private static bool IsStringBuilderClear(IMethodSymbol method)
    {
        return method.ContainingType.Name == nameof(StringBuilder) &&
               method.ContainingNamespace?.ToString() == "System.Text" &&
               method.Name == "Clear";
    }

    private static bool IsStringBuilderAppend(IMethodSymbol method)
    {
        return method.ContainingType.Name == nameof(StringBuilder) &&
               method.ContainingNamespace?.ToString() == "System.Text" &&
               method.Name.StartsWith("Append", System.StringComparison.Ordinal);
    }
}
