using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool IsRequiredOnPathFrom(IOperation start, IOperation required, IOperation later)
    {
        if (!start.SharesOwningExecutableRoot(required) ||
            !required.SharesOwningExecutableRoot(later) ||
            start.Syntax.SpanStart >= required.Syntax.SpanStart ||
            required.Syntax.SpanStart >= later.Syntax.SpanStart ||
            !BlockReaches(start, required) ||
            !BlockReaches(required, later))
        {
            return false;
        }

        if (HasReachableBranchSkippingRequired(start, required, later))
            return false;

        if (HasCaughtThrowSkippingRequired(start, required, later))
            return false;

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

                case SwitchSectionSyntax switchSection:
                    if (switchSection.Span.Contains(start.Syntax.SpanStart)) continue;
                    return false;

                case WhileStatementSyntax whileStatement:
                    if (whileStatement.Statement.Span.Contains(start.Syntax.SpanStart)) continue;
                    return false;

                case DoStatementSyntax doStatement:
                    if (doStatement.Statement.Span.Contains(start.Syntax.SpanStart)) continue;
                    return false;

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

        foreach (var throwSyntax in root.Syntax.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            if (throwSyntax.SpanStart <= start.Syntax.SpanStart ||
                throwSyntax.SpanStart >= required.Syntax.SpanStart ||
                !StartCanReachSyntax(start.Syntax, throwSyntax))
            {
                continue;
            }

            var throwOperation = semanticModel.GetOperation(throwSyntax) as IThrowOperation;
            var thrownType = throwOperation?.Exception?.Type;

            foreach (var trySyntax in throwSyntax.Ancestors().OfType<TryStatementSyntax>())
            {
                if (!trySyntax.Block.Span.Contains(throwSyntax.SpanStart)) continue;
                if (!trySyntax.Block.Span.Contains(required.Syntax.SpanStart)) continue;
                if (later.Syntax.SpanStart <= trySyntax.Span.End) continue;

                foreach (var catchClause in trySyntax.Catches)
                {
                    if (catchClause.Filter?.FilterExpression is { } filterExpression)
                    {
                        var constant = semanticModel.GetConstantValue(filterExpression);
                        if (constant.HasValue && constant.Value is false) continue;
                    }

                    var caughtType = catchClause.Declaration == null
                        ? null
                        : semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
                    if (!CanCatch(thrownType, caughtType, throwSyntax, catchClause)) continue;
                    if (StatementSkipsLater(catchClause.Block, later.Syntax)) continue;

                    return true;
                }
            }
        }

        return false;
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

        return true;
    }

    private static bool CanCatch(
        ITypeSymbol? thrownType,
        ITypeSymbol? caughtType,
        ThrowStatementSyntax throwSyntax,
        CatchClauseSyntax catchClause)
    {
        if (catchClause.Declaration == null) return true;

        if (throwSyntax.Expression is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.Type.ToString() == catchClause.Declaration.Type.ToString())
        {
            return true;
        }

        if (caughtType == null || thrownType == null) return false;

        for (var current = thrownType as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, caughtType) ||
                (current.MetadataName == caughtType.MetadataName &&
                 current.ContainingNamespace?.ToDisplayString() == caughtType.ContainingNamespace?.ToDisplayString()))
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

            if (
                ancestor.IsKind(SyntaxKind.SwitchStatement) ||
                ancestor.IsKind(SyntaxKind.SwitchSection) ||
                ancestor.IsKind(SyntaxKind.WhileStatement) ||
                ancestor.IsKind(SyntaxKind.DoStatement) ||
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
