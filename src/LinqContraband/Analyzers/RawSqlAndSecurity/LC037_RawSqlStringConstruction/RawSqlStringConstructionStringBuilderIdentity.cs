using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
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
}
