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
                !IsDbContextOptionsBuilderAddInterceptors(invocation.TargetMethod))
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
