using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal static partial class NPlusOneLooperAnalysis
{
    private static readonly ConditionalWeakTable<IOperation, LocalWriteCache> LocalWriteCaches = new();

    private static bool TryGetSingleAssignedLocalValue(
        ILocalSymbol local,
        IOperation analysisScope,
        CancellationToken cancellationToken,
        out IOperation valueOperation)
    {
        valueOperation = null!;

        if (local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declarator = local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) as VariableDeclaratorSyntax;
        if (declarator?.Initializer?.Value == null)
            return false;

        var semanticModel = analysisScope.SemanticModel;
        var executableRoot = analysisScope.FindOwningExecutableRoot();
        if (semanticModel == null || executableRoot == null)
            return false;

        var localWrites = LocalWriteCaches.GetValue(
            executableRoot,
            root => new LocalWriteCache(root.Syntax, semanticModel));

        if (localWrites.HasWrite(local, cancellationToken))
            return false;

        var operation = semanticModel.GetOperation(declarator.Initializer.Value, cancellationToken);
        if (operation == null)
            return false;

        valueOperation = operation;
        return true;
    }

    private sealed class LocalWriteCache
    {
        private readonly SyntaxNode executableRootSyntax;
        private readonly SemanticModel semanticModel;
        private readonly object syncRoot = new();
        private HashSet<ILocalSymbol>? writtenLocals;

        public LocalWriteCache(SyntaxNode executableRootSyntax, SemanticModel semanticModel)
        {
            this.executableRootSyntax = executableRootSyntax;
            this.semanticModel = semanticModel;
        }

        public bool HasWrite(ILocalSymbol local, CancellationToken cancellationToken)
        {
            return GetWrittenLocals(cancellationToken).Contains(local);
        }

        private HashSet<ILocalSymbol> GetWrittenLocals(CancellationToken cancellationToken)
        {
            if (writtenLocals != null)
                return writtenLocals;

            lock (syncRoot)
            {
                writtenLocals ??= BuildWrittenLocals(cancellationToken);
                return writtenLocals;
            }
        }

        private HashSet<ILocalSymbol> BuildWrittenLocals(CancellationToken cancellationToken)
        {
            var locals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

            foreach (var node in executableRootSyntax.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                ExpressionSyntax? target = node switch
                {
                    AssignmentExpressionSyntax assignment => assignment.Left,
                    PrefixUnaryExpressionSyntax prefix when
                        prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                        prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                    PostfixUnaryExpressionSyntax postfix when
                        postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                        postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                    _ => null
                };

                if (target == null)
                    continue;

                if (semanticModel.GetSymbolInfo(target, cancellationToken).Symbol is ILocalSymbol local)
                    locals.Add(local);
            }

            return locals;
        }
    }
}
