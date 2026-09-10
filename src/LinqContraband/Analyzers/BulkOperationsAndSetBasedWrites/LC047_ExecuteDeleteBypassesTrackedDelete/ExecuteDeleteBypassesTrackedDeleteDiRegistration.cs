using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

internal sealed partial class TrackedDeletePipelineEvidence
{
    private void ScanDiInterceptorRegistrations(
        Dictionary<INamedTypeSymbol, ConversionScan> interceptorConversions,
        CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tree.GetRoot(cancellationToken) is not CompilationUnitSyntax unit ||
                !ContainsDiRegistrationName(unit))
            {
                continue;
            }

            var model = compilation.GetSemanticModel(tree);
            if (HasTopLevelStatements(unit))
            {
                var compilationOperation = model.GetOperation(unit, cancellationToken);
                if (compilationOperation != null)
                    ScanDiOperations(compilationOperation, interceptorConversions, cancellationToken);
            }

            foreach (var declaration in unit.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ContainsDiRegistrationName(declaration))
                    continue;

                var operation = model.GetOperation(declaration, cancellationToken);
                if (operation == null)
                    continue;

                ScanDiOperations(operation, interceptorConversions, cancellationToken);
            }
        }
    }

    private void ScanDiOperations(
        IOperation executableRoot,
        Dictionary<INamedTypeSymbol, ConversionScan> interceptorConversions,
        CancellationToken cancellationToken)
    {
        foreach (var child in EnumerateOperations(executableRoot))
        {
            if (child is not IInvocationOperation invocation ||
                !TryGetDiContextType(invocation, out var contextType))
            {
                continue;
            }

            foreach (var lambda in GetInlineOptionsLambdas(invocation))
            {
                foreach (var interceptorType in GetLambdaInterceptorTypes(
                             lambda,
                             executableRoot,
                             cancellationToken))
                {
                    if (!TryGetInterceptorConversion(interceptorType, interceptorConversions, out var conversion))
                        continue;

                    ApplyConversion(contextType, conversion);
                }
            }
        }
    }

    private static bool TryGetDiContextType(IInvocationOperation invocation, out INamedTypeSymbol contextType)
    {
        contextType = null!;
        if (!IsEntityFrameworkServiceCollectionMethod(invocation.TargetMethod) ||
            invocation.TargetMethod.TypeArguments.Length == 0 ||
            invocation.TargetMethod.TypeArguments[0] is not INamedTypeSymbol candidate ||
            !candidate.IsDbContext() ||
            IsFrameworkDbContext(candidate.OriginalDefinition))
        {
            return false;
        }

        contextType = candidate;
        return true;
    }

    private static bool IsEntityFrameworkServiceCollectionMethod(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        return definition.Name is "AddDbContext" or "AddDbContextPool" or "AddDbContextFactory" &&
               definition.ContainingType.Name == "EntityFrameworkServiceCollectionExtensions" &&
               definition.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" &&
               definition.Parameters.Length > 0 &&
               definition.Parameters[0].Type.Name == "IServiceCollection" &&
               definition.Parameters[0].Type.ContainingNamespace?.ToDisplayString() ==
               "Microsoft.Extensions.DependencyInjection";
    }

    private static IEnumerable<IAnonymousFunctionOperation> GetInlineOptionsLambdas(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            var current = argument.Value.UnwrapConversions();
            if (current is IDelegateCreationOperation creation)
                current = creation.Target.UnwrapConversions();

            if (current is IAnonymousFunctionOperation lambda)
                yield return lambda;
        }
    }

    private IEnumerable<INamedTypeSymbol> GetLambdaInterceptorTypes(
        IAnonymousFunctionOperation lambda,
        IOperation executableRoot,
        CancellationToken cancellationToken)
    {
        foreach (var child in EnumerateOperations(lambda))
        {
            if (child is not IInvocationOperation invocation ||
                !IsDbContextOptionsBuilderAddInterceptors(invocation.TargetMethod) ||
                !AddInterceptorsReceiverIsLambdaOptions(invocation, lambda, cancellationToken))
            {
                continue;
            }

            foreach (var argument in invocation.Arguments)
            {
                foreach (var interceptorType in GetArgumentInterceptorTypes(
                             argument.Value,
                             executableRoot,
                             cancellationToken))
                {
                    yield return interceptorType;
                }
            }
        }
    }

    private static bool IsInsideNestedExecutable(SyntaxNode node, SyntaxNode root)
    {
        for (var current = node.Parent; current != null && current != root; current = current.Parent)
        {
            if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
                return true;
        }

        return false;
    }

    private static bool LocalHasUncachedWrite(IOperation root, ILocalSymbol local, int beforePosition)
    {
        foreach (var operation in root.Descendants())
        {
            // Writes at or after the registration cannot undo it — unless they
            // sit in a nested function, whose calls need not follow source order.
            if (operation.Syntax.SpanStart >= beforePosition &&
                !IsInsideNestedExecutable(operation.Syntax, root.Syntax))
                continue;

            if (operation is IArgumentOperation argument &&
                argument.Parameter?.RefKind != RefKind.None &&
                ReferencesLocal(argument.Value, local))
            {
                return true;
            }

            if (operation is IDeconstructionAssignmentOperation deconstruction &&
                ReferencesLocal(deconstruction.Target, local))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferencesLocal(IOperation operation, ILocalSymbol local) =>
        operation is ILocalReferenceOperation reference &&
            SymbolEqualityComparer.Default.Equals(reference.Local, local) ||
        operation.Descendants().OfType<ILocalReferenceOperation>()
            .Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.Local, local));

    private static bool AddInterceptorsReceiverIsLambdaOptions(
        IInvocationOperation invocation,
        IAnonymousFunctionOperation lambda,
        CancellationToken cancellationToken)
    {
        // The interceptor only configures this registration when the call runs
        // on the options builder handed to the lambda: a detached builder's
        // interceptors are discarded with it. Single-assignment locals
        // initialized from the parameter still denote it.
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var current = invocation.Instance?.UnwrapConversions();
        while (current != null)
        {
            switch (current)
            {
                case IInvocationOperation nested:
                    current = nested.Instance?.UnwrapConversions();
                    continue;
                case IPropertyReferenceOperation property when property.Instance != null:
                    current = property.Instance.UnwrapConversions();
                    continue;
                case IParameterReferenceOperation parameterReference:
                    return lambda.Symbol.Parameters.Any(parameter =>
                        SymbolEqualityComparer.Default.Equals(
                            parameter.OriginalDefinition,
                            parameterReference.Parameter.OriginalDefinition));
                case ILocalReferenceOperation localReference:
                    // Ref/out arguments and deconstruction targets bypass the
                    // assignment cache: either one can rebind the alias.
                    if (LocalHasUncachedWrite(lambda, localReference.Local, invocation.Syntax.SpanStart) ||
                        !seen.Add(localReference.Local) ||
                        !LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                            lambda,
                            localReference.Local,
                            invocation.Syntax.SpanStart,
                            out var assignedValue,
                            cancellationToken))
                    {
                        return false;
                    }

                    current = assignedValue?.UnwrapConversions();
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool IsDbContextOptionsBuilderAddInterceptors(IMethodSymbol method)
    {
        return method.Name == "AddInterceptors" &&
               method.ContainingType.Name == "DbContextOptionsBuilder" &&
               method.ContainingType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool ContainsDiRegistrationName(SyntaxNode node)
    {
        foreach (var name in node.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            if (name.Identifier.ValueText is "AddDbContext" or "AddDbContextPool" or "AddDbContextFactory")
                return true;
        }

        return false;
    }

    private static bool HasTopLevelStatements(CompilationUnitSyntax unit)
    {
        foreach (var member in unit.Members)
        {
            if (member is GlobalStatementSyntax)
                return true;
        }

        return false;
    }
}
