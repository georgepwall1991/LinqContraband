using System.Collections.Generic;
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
        foreach (var type in EnumerateSourceTypes(compilation.GlobalNamespace, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in type.GetMembers())
            {
                if (member is not IMethodSymbol method)
                    continue;

                ScanDiInterceptorMethod(method, interceptorConversions, cancellationToken);
            }
        }
    }

    private void ScanDiInterceptorMethod(
        IMethodSymbol method,
        Dictionary<INamedTypeSymbol, ConversionScan> interceptorConversions,
        CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
                continue;

            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            var operation = model.GetOperation(declaration, cancellationToken);
            if (operation == null)
                continue;

            foreach (var child in EnumerateOperations(operation))
            {
                if (child is not IInvocationOperation invocation ||
                    !TryGetDiContextType(invocation, out var contextType))
                {
                    continue;
                }

                foreach (var lambda in GetInlineOptionsLambdas(invocation))
                {
                    foreach (var interceptorType in GetLambdaInterceptorTypes(lambda, cancellationToken))
                    {
                        if (!TryGetInterceptorConversion(interceptorType, interceptorConversions, out var conversion))
                            continue;

                        ApplyConversion(contextType, conversion);
                    }
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
        return method.Name is "AddDbContext" or "AddDbContextPool" or "AddDbContextFactory" &&
               method.ContainingType.Name == "EntityFrameworkServiceCollectionExtensions" &&
               method.ContainingType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
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
                             lambda,
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
}
