using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
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
}
