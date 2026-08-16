using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

internal sealed partial class TrackedDeletePipelineEvidence
{
    private IEnumerable<INamedTypeSymbol> GetRegisteredInterceptorTypes(
        INamedTypeSymbol contextType,
        CancellationToken cancellationToken)
    {
        foreach (var member in contextType.GetMembers("OnConfiguring"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method)
                continue;

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
                    continue;

                var model = compilation.GetSemanticModel(declaration.SyntaxTree);
                var operation = model.GetOperation(declaration, cancellationToken);
                if (operation == null)
                    continue;

                foreach (var child in EnumerateOperations(operation))
                {
                    if (child is not IInvocationOperation invocation ||
                        invocation.TargetMethod.Name != "AddInterceptors")
                    {
                        continue;
                    }

                    foreach (var argument in invocation.Arguments)
                    {
                        foreach (var interceptorType in GetArgumentInterceptorTypes(
                                     argument.Value,
                                     operation,
                                     cancellationToken))
                            yield return interceptorType;
                    }
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetArgumentInterceptorTypes(
        IOperation argument,
        IOperation executableRoot,
        CancellationToken cancellationToken)
    {
        var current = argument.UnwrapConversions();
        switch (current)
        {
            case IObjectCreationOperation creation when creation.Type is INamedTypeSymbol created:
                yield return created;
                yield break;
            case IConversionOperation conversion:
                foreach (var type in GetArgumentInterceptorTypes(conversion.Operand, executableRoot, cancellationToken))
                    yield return type;
                yield break;
            case IArrayCreationOperation arrayCreation when arrayCreation.Initializer != null:
                foreach (var element in arrayCreation.Initializer.ElementValues)
                {
                    foreach (var type in GetArgumentInterceptorTypes(element, executableRoot, cancellationToken))
                        yield return type;
                }

                yield break;
            case ILocalReferenceOperation local:
                if (LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        local.Local,
                        local.Syntax.SpanStart,
                        out var assigned,
                        cancellationToken))
                {
                    foreach (var type in GetArgumentInterceptorTypes(assigned, executableRoot, cancellationToken))
                        yield return type;
                    yield break;
                }

                if (current.Type is INamedTypeSymbol localType)
                    yield return localType;
                yield break;
            default:
                if (current.Type is INamedTypeSymbol named)
                    yield return named;
                yield break;
        }
    }
}
