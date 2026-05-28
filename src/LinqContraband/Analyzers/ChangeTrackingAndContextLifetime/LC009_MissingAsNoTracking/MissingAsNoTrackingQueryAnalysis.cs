using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
    private static ChainAnalysis AnalyzeQueryChain(IInvocationOperation invocation)
    {
        var result = new ChainAnalysis();
        var current = invocation.GetInvocationReceiver();

        while (current != null)
        {
            current = current.UnwrapConversions();

            switch (current)
            {
                case IInvocationOperation prevInvocation:
                    var method = prevInvocation.TargetMethod;

                    if (method.Name == "AsNoTracking" || method.Name == "AsNoTrackingWithIdentityResolution")
                        result.HasAsNoTracking = true;
                    if (method.Name == "AsTracking")
                        result.HasAsTracking = true;
                    if (method.Name == "Select")
                        result.HasSelect = true;

                    // An invocation whose return type is a DbSet (e.g. DbContext.Set<T>(), the
                    // generic-repository read path) is itself the EF source. Without this the
                    // walker steps past it to the DbContext receiver and misses the query.
                    if (prevInvocation.Type.IsDbSet())
                    {
                        result.IsEfQuery = true;
                        return result;
                    }

                    current = prevInvocation.Instance ??
                              (prevInvocation.Arguments.Length > 0 ? prevInvocation.Arguments[0].Value : null);
                    continue;

                case IPropertyReferenceOperation propRef:
                    if (propRef.Type.IsDbSet())
                        result.IsEfQuery = true;
                    return result;

                case IFieldReferenceOperation fieldRef:
                    if (fieldRef.Type.IsDbSet())
                        result.IsEfQuery = true;
                    return result;

                case IParameterReferenceOperation paramRef:
                    if (paramRef.Type.IsDbSet() || paramRef.Type.IsIQueryable())
                        result.IsAmbiguousSource = true;
                    return result;

                case ILocalReferenceOperation localRef:
                    if (localRef.Type.IsDbSet() || localRef.Type.IsIQueryable())
                        result.IsAmbiguousSource = true;
                    return result;

                default:
                    if (current.Type.IsDbSet())
                        result.IsEfQuery = true;
                    else if (current.Type.IsIQueryable())
                        result.IsAmbiguousSource = true;
                    return result;
            }
        }

        return result;
    }
}
