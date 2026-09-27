using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC062_BlockingEfAsyncCall;

public sealed partial class BlockingEfAsyncCallAnalyzer
{
    private const string EfCoreNamespace = "Microsoft.EntityFrameworkCore";
    private const string EfCoreInfrastructureNamespace = "Microsoft.EntityFrameworkCore.Infrastructure";

    /// <summary>
    /// True for an EF Core async operation that returns a task: a query operator on an EF Core query
    /// (<c>EntityFrameworkQueryableExtensions</c>, <c>RelationalQueryableExtensions</c>), <c>SaveChangesAsync</c>,
    /// <c>FindAsync</c>, <c>AddAsync</c> and <c>AddRangeAsync</c> on a <c>DbContext</c> or <c>DbSet</c>, and the async
    /// methods of <c>DatabaseFacade</c> and <c>RelationalDatabaseFacadeExtensions</c>.
    /// </summary>
    internal static bool IsEfCoreAsyncOperation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!method.Name.EndsWith("Async", System.StringComparison.Ordinal) || !IsTaskLike(method.ReturnType))
            return false;

        var original = method.ReducedFrom ?? method;
        var containingType = original.ContainingType;
        if (containingType == null)
            return false;

        var containingNamespace = containingType.ContainingNamespace?.ToDisplayString();

        if (original.IsStatic && containingNamespace == EfCoreNamespace)
        {
            switch (containingType.Name)
            {
                case "EntityFrameworkQueryableExtensions":
                case "RelationalQueryableExtensions":
                    // A LINQ to Objects query wrapped with AsQueryable() throws on these instead (LC060's case).
                    return !invocation.GetInvocationReceiver().IsProvablyInMemoryQueryable();

                case "RelationalDatabaseFacadeExtensions":
                    return true;
            }

            return false;
        }

        if (original.IsStatic)
            return false;

        if (containingType.Name == "DatabaseFacade" && containingNamespace == EfCoreInfrastructureNamespace)
            return true;

        if (method.Name is not ("SaveChangesAsync" or "FindAsync" or "AddAsync" or "AddRangeAsync"))
            return false;

        // SaveChangesAsync is often overridden in the application's context: judge the method it overrides.
        var root = original;
        while (root.OverriddenMethod != null)
            root = root.OverriddenMethod;

        var rootType = root.ContainingType;
        return rootType?.ContainingNamespace?.ToDisplayString() == EfCoreNamespace &&
               (rootType.Name == "DbContext" || (rootType.Name == "DbSet" && rootType.Arity == 1)) &&
               (method.Name != "SaveChangesAsync" || rootType.Name == "DbContext");
    }
}
