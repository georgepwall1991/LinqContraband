using System.Linq;
using Microsoft.CodeAnalysis;
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
            var last = GetLastStatement(branch);
            if (last is ReturnStatementSyntax)
                return true;

            return IsThrow(last) && !IsCaughtBefore(branch, right, GetThrownType(last!, semanticModel), semanticModel);
        }

        private static StatementSyntax? GetLastStatement(StatementSyntax statement)
        {
            return statement is BlockSyntax block ? block.Statements.LastOrDefault() : statement;
        }

        private static bool IsThrow(StatementSyntax? statement)
        {
            return statement is ThrowStatementSyntax ||
                   statement is ExpressionStatementSyntax { Expression: ThrowExpressionSyntax };
        }

        /// <summary>
        /// The static type of the thrown expression. A bare <c>throw;</c> or an unknown type yields null, which only
        /// untyped catches, <c>catch (Exception)</c> and <c>catch (object)</c> definitely receive.
        /// </summary>
        private static ITypeSymbol? GetThrownType(StatementSyntax throwStatement, SemanticModel? semanticModel)
        {
            var expression = throwStatement switch
            {
                ThrowStatementSyntax statement => statement.Expression,
                ExpressionStatementSyntax { Expression: ThrowExpressionSyntax throwExpression } => throwExpression.Expression,
                _ => null
            };

            if (expression == null || semanticModel == null || expression.SyntaxTree != semanticModel.SyntaxTree)
                return null;

            return semanticModel.GetTypeInfo(expression).Type;
        }

        /// <summary>
        /// <c>try { if (flag) { db.SaveChanges(); throw ...; } } catch { } db.SaveChanges();</c>: the catch swallows the
        /// throw and the later save still runs. Only a try whose try block also holds the later save is skipped by the throw.
        /// Catches are tried in order: the first one that definitely receives the exception decides. If it completes
        /// normally the throw is caught; if it returns the method is left; if it throws, the new exception keeps going
        /// outward. A filtered catch, or one whose type might only match at run time, may not handle it and is passed over.
        /// </summary>
        private static bool IsCaughtBefore(SyntaxNode branch, SyntaxNode right, ITypeSymbol? thrownType, SemanticModel? semanticModel)
        {
            foreach (var ancestor in branch.Ancestors())
            {
                if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax)
                    return false;

                if (ancestor is not TryStatementSyntax tryStatement ||
                    !tryStatement.Block.Span.Contains(branch.SpanStart) ||
                    tryStatement.Block.Span.Contains(right.SpanStart))
                {
                    continue;
                }

                var receiver = tryStatement.Catches.FirstOrDefault(catchClause =>
                    catchClause.Filter == null && DefinitelyReceives(catchClause, thrownType, semanticModel));
                if (receiver == null)
                    continue;

                var last = GetLastStatement(receiver.Block);
                if (last is ReturnStatementSyntax)
                    return false;

                if (!IsThrow(last))
                    return true;

                if (last is ThrowStatementSyntax { Expression: null })
                    continue;

                thrownType = GetThrownType(last!, semanticModel);
            }

            return false;
        }

        private static bool DefinitelyReceives(CatchClauseSyntax catchClause, ITypeSymbol? thrownType, SemanticModel? semanticModel)
        {
            if (catchClause.Declaration == null)
                return true;

            if (semanticModel == null || catchClause.SyntaxTree != semanticModel.SyntaxTree)
                return false;

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
