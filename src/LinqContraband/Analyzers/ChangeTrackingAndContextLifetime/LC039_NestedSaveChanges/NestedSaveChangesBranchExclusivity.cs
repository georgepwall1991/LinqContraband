using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC039_NestedSaveChanges;

public sealed partial class NestedSaveChangesAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static readonly ConditionalWeakTable<SwitchStatementSyntax, bool[,]> SwitchSectionReachability = new();

        /// <summary>
        /// Whether <paramref name="left"/> and <paramref name="right"/> can never both run in one call. Two switch
        /// sections stop counting as exclusive when a <c>goto</c> can carry control from one to the other. A try
        /// block and its catch are exclusive only as whole branches: part of the try block may run before the catch, so
        /// callers asking whether something definitely did not run pass <paramref name="tryCatchIsExclusive"/> false.
        /// </summary>
        private static bool AreMutuallyExclusiveBranches(SyntaxNode left, SyntaxNode right, SemanticModel? semanticModel, bool tryCatchIsExclusive = true)
        {
            foreach (var ifStatement in left.AncestorsAndSelf().OfType<IfStatementSyntax>())
            {
                if (!ifStatement.Span.Contains(right.SpanStart))
                    continue;

                var leftBranch = GetContainingBranch(ifStatement, left);
                var rightBranch = GetContainingBranch(ifStatement, right);

                if (leftBranch != null &&
                    rightBranch != null &&
                    leftBranch != rightBranch)
                {
                    return true;
                }
            }

            foreach (var switchStatement in left.AncestorsAndSelf().OfType<SwitchStatementSyntax>())
            {
                if (!switchStatement.Span.Contains(right.SpanStart))
                    continue;

                var leftSection = GetContainingSwitchSection(switchStatement, left);
                var rightSection = GetContainingSwitchSection(switchStatement, right);

                if (leftSection != null &&
                    rightSection != null &&
                    leftSection != rightSection &&
                    !AreJoinedByGoto(switchStatement, leftSection, rightSection, semanticModel))
                {
                    return true;
                }
            }

            foreach (var switchExpression in left.AncestorsAndSelf().OfType<SwitchExpressionSyntax>())
            {
                if (!switchExpression.Span.Contains(right.SpanStart))
                    continue;

                var leftArm = GetContainingSwitchExpressionArm(switchExpression, left);
                var rightArm = GetContainingSwitchExpressionArm(switchExpression, right);

                if (leftArm != null &&
                    rightArm != null &&
                    leftArm != rightArm)
                {
                    return true;
                }
            }

            if (!tryCatchIsExclusive)
                return false;

            // SaveChanges in try and catch branches are mutually exclusive; finally is not exclusive.
            foreach (var tryStatement in left.AncestorsAndSelf().OfType<TryStatementSyntax>())
            {
                if (!tryStatement.Span.Contains(right.SpanStart))
                    continue;

                var leftTryBranch = GetContainingTryBranch(tryStatement, left);
                var rightTryBranch = GetContainingTryBranch(tryStatement, right);

                if (leftTryBranch != null &&
                    rightTryBranch != null &&
                    leftTryBranch != rightTryBranch)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// <c>case 0: save; goto case 1; case 1: save; break;</c> runs both saves. Sections are joined when a goto in one
        /// can carry control to the other, directly or through further sections.
        /// </summary>
        private static bool AreJoinedByGoto(SwitchStatementSyntax switchStatement, SwitchSectionSyntax left, SwitchSectionSyntax right, SemanticModel? semanticModel)
        {
            var reachability = SwitchSectionReachability.GetValue(
                switchStatement,
                statement => BuildSectionReachability(statement, semanticModel));
            var leftIndex = switchStatement.Sections.IndexOf(left);
            var rightIndex = switchStatement.Sections.IndexOf(right);

            return reachability[leftIndex, rightIndex] || reachability[rightIndex, leftIndex];
        }

        private static bool[,] BuildSectionReachability(SwitchStatementSyntax switchStatement, SemanticModel? semanticModel)
        {
            var sections = switchStatement.Sections;
            var count = sections.Count;
            var reachable = new bool[count, count];

            for (var from = 0; from < count; from++)
            {
                var gotos = sections[from].Statements
                    .SelectMany(statement => statement.DescendantNodesAndSelf(node =>
                        node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
                    .OfType<GotoStatementSyntax>();

                foreach (var gotoStatement in gotos)
                {
                    foreach (var to in GetGotoTargets(switchStatement, gotoStatement, semanticModel))
                    {
                        if (to != from)
                            reachable[from, to] = true;
                    }
                }
            }

            for (var via = 0; via < count; via++)
            {
                for (var from = 0; from < count; from++)
                {
                    if (!reachable[from, via])
                        continue;

                    for (var to = 0; to < count; to++)
                    {
                        if (reachable[via, to])
                            reachable[from, to] = true;
                    }
                }
            }

            return reachable;
        }

        /// <summary>
        /// The sections of <paramref name="switchStatement"/> a goto can land in. <c>goto case</c> and
        /// <c>goto default</c> belong to their nearest switch; an unresolved target joins every section. A labelled goto
        /// lands in the section holding its label, or nowhere in this switch when the label is outside it.
        /// </summary>
        private static IEnumerable<int> GetGotoTargets(SwitchStatementSyntax switchStatement, GotoStatementSyntax gotoStatement, SemanticModel? semanticModel)
        {
            var sections = switchStatement.Sections;

            if (gotoStatement.IsKind(SyntaxKind.GotoStatement))
            {
                if (gotoStatement.Expression is not IdentifierNameSyntax labelName)
                    return Enumerable.Empty<int>();

                var label = switchStatement.Sections
                    .SelectMany(section => section.DescendantNodes())
                    .OfType<LabeledStatementSyntax>()
                    .FirstOrDefault(statement => statement.Identifier.ValueText == labelName.Identifier.ValueText);
                if (label == null)
                    return Enumerable.Empty<int>();

                return new[] { sections.IndexOf(GetContainingSwitchSection(switchStatement, label)!) };
            }

            if (gotoStatement.FirstAncestorOrSelf<SwitchStatementSyntax>() != switchStatement)
                return Enumerable.Empty<int>();

            if (gotoStatement.IsKind(SyntaxKind.GotoDefaultStatement))
            {
                var defaultIndex = IndexOfSection(sections, section => section.Labels.Any(label => label is DefaultSwitchLabelSyntax));
                return defaultIndex >= 0 ? new[] { defaultIndex } : Enumerable.Range(0, sections.Count);
            }

            if (gotoStatement.Expression == null ||
                semanticModel == null ||
                gotoStatement.SyntaxTree != semanticModel.SyntaxTree)
            {
                return Enumerable.Range(0, sections.Count);
            }

            var target = semanticModel.GetConstantValue(gotoStatement.Expression);
            if (!target.HasValue)
                return Enumerable.Range(0, sections.Count);

            var caseIndex = IndexOfSection(sections, section => section.Labels
                .OfType<CaseSwitchLabelSyntax>()
                .Any(label =>
                {
                    var value = semanticModel.GetConstantValue(label.Value);
                    return value.HasValue && Equals(value.Value, target.Value);
                }));

            return caseIndex >= 0 ? new[] { caseIndex } : Enumerable.Range(0, sections.Count);
        }

        private static int IndexOfSection(SyntaxList<SwitchSectionSyntax> sections, System.Func<SwitchSectionSyntax, bool> predicate)
        {
            for (var i = 0; i < sections.Count; i++)
            {
                if (predicate(sections[i]))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// <c>if (entity is null) { db.Add(e); await db.SaveChangesAsync(); return 1; } entity.Count++; await db.SaveChangesAsync();</c>:
        /// the first save sits in a branch that always leaves the method, so the later save never runs after it.
        /// </summary>
        private static bool LeavesMethodBefore(SyntaxNode left, SyntaxNode right, SemanticModel? semanticModel)
        {
            foreach (var ifStatement in left.Ancestors().OfType<IfStatementSyntax>())
            {
                if (ifStatement.Span.Contains(right.SpanStart))
                    return false;

                var branch = GetContainingBranch(ifStatement, left);
                if (branch != null && EndsWithMethodExit(branch, right, semanticModel) && !IsInFinallyAround(left, right))
                    return true;
            }

            return false;
        }

        private static bool EndsWithMethodExit(StatementSyntax branch, SyntaxNode right, SemanticModel? semanticModel)
        {
            return !MayContinueAfter(branch, right, null, semanticModel);
        }

        /// <summary>
        /// Whether control can leave <paramref name="block"/> other than by <c>return</c> or by a throw that nothing
        /// catches before <paramref name="right"/>: falling off its end, <c>break</c>, <c>continue</c> or <c>goto</c>
        /// out of it, or a throw a later catch swallows. <c>catch { if (retry) return; else throw; }</c> cannot.
        /// Anything the control-flow analysis cannot answer counts as continuing.
        /// </summary>
        private static bool MayContinueAfter(StatementSyntax block, SyntaxNode right, ITypeSymbol? rethrownType, SemanticModel? semanticModel)
        {
            if (semanticModel == null || block.SyntaxTree != semanticModel.SyntaxTree)
                return true;

            var controlFlow = semanticModel.AnalyzeControlFlow(block);
            if (controlFlow == null || !controlFlow.Succeeded || controlFlow.EndPointIsReachable)
                return true;

            if (controlFlow.ExitPoints.Any(exitPoint =>
                    exitPoint is not ReturnStatementSyntax &&
                    !exitPoint.IsKind(SyntaxKind.YieldBreakStatement)))
            {
                return true;
            }

            foreach (var throwNode in GetThrows(block))
            {
                var thrownType = GetThrownType(throwNode, semanticModel) ?? (IsRethrow(throwNode) ? rethrownType : null);
                if (IsCaughtBefore(throwNode, block, right, thrownType, semanticModel))
                    return true;
            }

            return false;
        }

        private static IEnumerable<SyntaxNode> GetThrows(SyntaxNode block)
        {
            return block
                .DescendantNodes(node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                .Where(node => node is ThrowStatementSyntax or ThrowExpressionSyntax);
        }

        private static bool IsRethrow(SyntaxNode throwNode)
        {
            return throwNode is ThrowStatementSyntax { Expression: null };
        }

        /// <summary>
        /// The static type of the thrown expression. A bare <c>throw;</c> or an unknown type yields null, which only
        /// untyped catches, <c>catch (Exception)</c> and <c>catch (object)</c> definitely receive.
        /// </summary>
        private static ITypeSymbol? GetThrownType(SyntaxNode throwNode, SemanticModel semanticModel)
        {
            var expression = throwNode switch
            {
                ThrowStatementSyntax statement => statement.Expression,
                ThrowExpressionSyntax throwExpression => throwExpression.Expression,
                _ => null
            };

            return expression == null ? null : semanticModel.GetTypeInfo(expression).Type;
        }

        /// <summary>
        /// <c>try { if (flag) { db.SaveChanges(); throw ...; } } catch { } db.SaveChanges();</c>: the catch swallows the
        /// throw and the later save still runs. Only a try whose try block also holds the later save is skipped by the throw.
        /// Catches are tried in order: the first one that definitely receives the exception decides, and the throw is
        /// caught when control can leave that catch other than by returning or throwing on out of the method.
        /// A filtered catch, or one whose type might only match at run time, may not handle it and is passed over.
        /// A try inside <paramref name="container"/> that receives the throw keeps it inside, where the container's
        /// own control-flow analysis already accounts for what happens next.
        /// </summary>
        private static bool IsCaughtBefore(SyntaxNode throwNode, SyntaxNode container, SyntaxNode right, ITypeSymbol? thrownType, SemanticModel semanticModel)
        {
            foreach (var ancestor in throwNode.Ancestors())
            {
                if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax)
                    return false;

                if (ancestor is not TryStatementSyntax tryStatement ||
                    !tryStatement.Block.Span.Contains(throwNode.SpanStart) ||
                    tryStatement.Block.Span.Contains(right.SpanStart))
                {
                    continue;
                }

                // Every clause that could receive the throw, filtered or not, up to the first unfiltered one that
                // definitely does, is a possible handler. The throw is caught only when every possible handler can
                // resume; if any of them returns or throws on, the path may leave the method and LC039 stays quiet.
                var handlers = new List<CatchClauseSyntax>();
                var definitelyHandled = false;
                foreach (var catchClause in tryStatement.Catches)
                {
                    if (!PossiblyReceives(catchClause, thrownType, semanticModel))
                        continue;

                    handlers.Add(catchClause);
                    if (catchClause.Filter == null && DefinitelyReceives(catchClause, thrownType, semanticModel))
                    {
                        definitelyHandled = true;
                        break;
                    }
                }

                if (handlers.Count == 0)
                    continue;

                if (container.Span.Contains(tryStatement.Span))
                    return false;

                if (!handlers.All(handler => MayContinueAfter(handler.Block, right, thrownType, semanticModel)))
                    return false;

                if (definitelyHandled)
                    return true;
            }

            return false;
        }

        private static bool PossiblyReceives(CatchClauseSyntax catchClause, ITypeSymbol? thrownType, SemanticModel semanticModel)
        {
            if (catchClause.Declaration == null || thrownType == null)
                return true;

            var catchType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
            if (catchType == null)
                return true;

            return InheritsFromOrEquals(thrownType, catchType) || InheritsFromOrEquals(catchType, thrownType);
        }

        private static bool InheritsFromOrEquals(ITypeSymbol type, ITypeSymbol baseType)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                    return true;
            }

            return false;
        }

        private static bool DefinitelyReceives(CatchClauseSyntax catchClause, ITypeSymbol? thrownType, SemanticModel semanticModel)
        {
            if (catchClause.Declaration == null)
                return true;

            var catchType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
            if (catchType == null)
                return false;

            if (catchType.SpecialType == SpecialType.System_Object ||
                catchType.ToDisplayString() == "System.Exception")
            {
                return true;
            }

            return thrownType != null && InheritsFromOrEquals(thrownType, catchType);
        }

        private static bool IsInFinallyAround(SyntaxNode left, SyntaxNode right)
        {
            return left.Ancestors().OfType<TryStatementSyntax>().Any(tryStatement =>
                tryStatement.Finally?.Block.Span.Contains(right.SpanStart) == true);
        }

        private static SyntaxNode? GetContainingTryBranch(TryStatementSyntax tryStatement, SyntaxNode node)
        {
            if (tryStatement.Block.Span.Contains(node.SpanStart))
                return tryStatement.Block;

            foreach (var catchClause in tryStatement.Catches)
            {
                if (catchClause.Block.Span.Contains(node.SpanStart))
                    return catchClause;
            }

            return null;
        }

        private static StatementSyntax? GetContainingBranch(IfStatementSyntax ifStatement, SyntaxNode node)
        {
            if (ifStatement.Statement.Span.Contains(node.SpanStart))
                return ifStatement.Statement;

            if (ifStatement.Else?.Statement.Span.Contains(node.SpanStart) == true)
                return ifStatement.Else.Statement;

            return null;
        }

        private static SwitchSectionSyntax? GetContainingSwitchSection(SwitchStatementSyntax switchStatement, SyntaxNode node)
        {
            return switchStatement.Sections.FirstOrDefault(section => section.Span.Contains(node.SpanStart));
        }

        private static SwitchExpressionArmSyntax? GetContainingSwitchExpressionArm(SwitchExpressionSyntax switchExpression, SyntaxNode node)
        {
            return switchExpression.Arms.FirstOrDefault(arm => arm.Expression.Span.Contains(node.SpanStart));
        }
    }
}
