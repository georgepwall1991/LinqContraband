using System;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
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
}
