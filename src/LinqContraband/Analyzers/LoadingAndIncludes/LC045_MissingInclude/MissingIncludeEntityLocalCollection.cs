using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static HashSet<ILocalSymbol> CollectEntityLocals(
        IOperation executableRoot,
        ILocalSymbol resultLocal,
        bool returnsCollection,
        CancellationToken cancellationToken)
    {
        var entityLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        if (!returnsCollection)
        {
            entityLocals.Add(resultLocal);
            return entityLocals;
        }

        foreach (var descendant in executableRoot.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (descendant)
            {
                case IForEachLoopOperation forEach when
                    forEach.Collection.UnwrapConversions() is ILocalReferenceOperation collectionRef &&
                    SymbolEqualityComparer.Default.Equals(collectionRef.Local, resultLocal):
                    foreach (var loopLocal in forEach.Locals)
                        entityLocals.Add(loopLocal);
                    break;

                case IVariableDeclaratorOperation declarator when
                    declarator.Initializer != null &&
                    IsIndexedAccessOf(declarator.Initializer.Value, resultLocal) &&
                    LocalAssignmentCache.GetAssignments(executableRoot, declarator.Symbol, cancellationToken).Count == 1:
                    entityLocals.Add(declarator.Symbol);
                    break;

                case ISimpleAssignmentOperation assignmentOperation when
                    assignmentOperation.Target is ILocalReferenceOperation targetLocal &&
                    IsIndexedAccessOf(assignmentOperation.Value, resultLocal) &&
                    LocalAssignmentCache.GetAssignments(executableRoot, targetLocal.Local, cancellationToken).Count == 1:
                    entityLocals.Add(targetLocal.Local);
                    break;
            }
        }

        return entityLocals;
    }
}
