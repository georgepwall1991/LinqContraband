using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC014_AvoidStringCaseConversion;

public sealed partial class AvoidStringCaseConversionAnalyzer
{
    private static bool TryGetArgumentValue(
        IInvocationOperation invocation,
        string parameterName,
        out IOperation value)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == parameterName)
            {
                value = argument.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static bool HasEntityFrameworkQuerySource(IOperation? operation)
    {
        var current = operation;
        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current.Type.IsDbSet())
                return true;

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (invocation.TargetMethod.Name == "Set" && invocation.TargetMethod.ContainingType.IsDbContext())
                        return true;

                    current = invocation.GetInvocationReceiver();
                    continue;

                case IPropertyReferenceOperation propertyReference:
                    if (propertyReference.Type.IsDbSet())
                        return true;

                    current = propertyReference.Instance;
                    continue;

                case IFieldReferenceOperation fieldReference:
                    if (fieldReference.Type.IsDbSet())
                        return true;

                    current = fieldReference.Instance;
                    continue;

                case ILocalReferenceOperation localReference:
                    if (localReference.Type.IsDbSet())
                        return true;

                    if (TryResolveLocalValue(localReference.Local, localReference, localReference.FindOwningExecutableRoot(), out var resolvedValue))
                    {
                        current = resolvedValue;
                        continue;
                    }

                    return false;

                case IParameterReferenceOperation parameterReference:
                    return parameterReference.Type.IsDbSet();

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryResolveLocalValue(ILocalSymbol local, IOperation reference, IOperation? executableRoot, out IOperation value)
    {
        value = null!;

        if (executableRoot == null)
            return false;

        var referenceStart = reference.Syntax.SpanStart;
        var bestWriteStart = -1;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) || declarator.Initializer == null)
                        continue;

                    var writeStart = declarator.Syntax.SpanStart;
                    if (writeStart >= referenceStart || writeStart <= bestWriteStart)
                        continue;

                    bestWriteStart = writeStart;
                    value = declarator.Initializer.Value;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
            {
                var writeStart = assignment.Syntax.SpanStart;
                if (writeStart >= referenceStart || writeStart <= bestWriteStart)
                    continue;

                bestWriteStart = writeStart;
                value = assignment.Value;
            }
        }

        return bestWriteStart >= 0;
    }
}
