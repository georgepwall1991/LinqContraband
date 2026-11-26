using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A mediator call (<c>mediator.Send(new GetCustomerName(id))</c>, MediatR's <c>Send</c>, Wolverine's
/// <c>InvokeAsync</c>) runs the one handler registered for the request's type. When the request's static type is a
/// concrete class or struct and this compilation declares between one and <see cref="MaxImplementations"/>
/// handlers for it (a type implementing a generic <c>*Handler*</c> interface whose first type argument is the
/// request type), the call reaches those handlers' methods that take the request, not the mediator's own body.
/// Callers then require every one of them to run a query or save, as for an interface call.
/// </summary>
internal static partial class NPlusOneLooperAnalysis
{
    private static ImmutableArray<IMethodSymbol> GetMediatorHandlerTargets(
        IInvocationOperation invocation,
        NPlusOneLooperHelperCache cache,
        CancellationToken cancellationToken)
    {
        if (invocation.TargetMethod.Name is not ("Send" or "SendAsync" or "InvokeAsync") ||
            invocation.Arguments.Length == 0)
        {
            return ImmutableArray<IMethodSymbol>.Empty;
        }

        var requestType = GetRequestType(invocation);
        if (requestType == null)
            return ImmutableArray<IMethodSymbol>.Empty;

        var handlerTypes = cache.GetSourceTypes(cancellationToken).GetHandlers(requestType);
        if (handlerTypes.Count == 0 || handlerTypes.Count > MaxImplementations)
            return ImmutableArray<IMethodSymbol>.Empty;

        var targets = ImmutableArray.CreateBuilder<IMethodSymbol>(handlerTypes.Count);
        foreach (var handlerType in handlerTypes)
        {
            var found = false;
            foreach (var @interface in handlerType.AllInterfaces)
            {
                if (!NPlusOneLooperSourceTypeIndex.IsHandlerInterfaceFor(@interface, requestType))
                    continue;

                foreach (var member in @interface.GetMembers())
                {
                    if (member is not IMethodSymbol { Parameters.Length: > 0 } interfaceMethod ||
                        !SymbolEqualityComparer.Default.Equals(interfaceMethod.Parameters[0].Type, requestType))
                    {
                        continue;
                    }

                    if (handlerType.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol handle ||
                        handle.IsAbstract ||
                        handle.DeclaringSyntaxReferences.Length == 0)
                    {
                        return ImmutableArray<IMethodSymbol>.Empty;
                    }

                    AddTarget(targets, handle);
                    found = true;
                }
            }

            if (!found)
                return ImmutableArray<IMethodSymbol>.Empty;
        }

        return targets.ToImmutable();
    }

    /// <summary>The concrete class or struct type of the first argument (the request object), or null.</summary>
    private static ITypeSymbol? GetRequestType(IInvocationOperation invocation)
    {
        var request = invocation.Arguments[0].Value.UnwrapConversions();
        return request.Type is INamedTypeSymbol
        {
            TypeKind: TypeKind.Class or TypeKind.Struct,
            IsAbstract: false,
            SpecialType: SpecialType.None
        } type
            ? type
            : null;
    }
}
