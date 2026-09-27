using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC039_NestedSaveChanges;

public sealed partial class NestedSaveChangesAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool AreMutuallyExclusiveBranches(SyntaxNode left, SyntaxNode right)
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
                    leftSection != rightSection)
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

                var receiver = tryStatement.Catches.FirstOrDefault(catchClause =>
                    catchClause.Filter == null && DefinitelyReceives(catchClause, thrownType, semanticModel));
                if (receiver == null)
                    continue;

                if (container.Span.Contains(tryStatement.Span))
                    return false;

                return MayContinueAfter(receiver.Block, right, thrownType, semanticModel);
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

            for (var type = thrownType; type != null; type = type.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(type, catchType))
                    return true;
            }

            return false;
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
