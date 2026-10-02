using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionAnalyzer
{
    /// <summary>
    /// <c>var q = db.X.Include(a =&gt; a.As).Include(b =&gt; b.Bs);</c> is not a cartesian query by itself when every use
    /// of <c>q</c> in the method composes <c>AsSplitQuery()</c> onto it (and no later <c>AsSingleQuery()</c>) before
    /// the query runs. Any other use, such as returning <c>q</c>, passing it to a method, running it directly or
    /// reassigning it, keeps the report on the Include chain.
    /// </summary>
    private static bool IsIncludeLocalSplitBeforeEveryUse(IInvocationOperation outermostInvocation)
    {
        // Deferred query operators after the Include chain (`.Where(...)`, `.OrderBy(...)`) still build the same
        // query, so the local may hold the chain plus those. A materializer in between runs it, so it stops here.
        IOperation node = outermostInvocation;
        while (true)
        {
            while (node.Parent is IConversionOperation conversion)
                node = conversion;

            var next = GetChainedCall(node);
            if (next == null || !IsQueryable(next.Type) || !IsKnownQueryOperator(next.TargetMethod))
                break;

            node = next;
        }

        if (node.Parent is not IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator })
            return false;

        var local = declarator.Symbol;
        if (local.IsRef)
            return false;

        var root = declarator.FindOwningExecutableRoot();
        if (root == null)
            return false;

        var useCount = 0;
        foreach (var descendant in root.Descendants())
        {
            if (descendant is not ILocalReferenceOperation reference ||
                !SymbolEqualityComparer.Default.Equals(reference.Local, local))
            {
                continue;
            }

            if (!IsSplitDownstream(reference))
                return false;

            useCount++;
        }

        return useCount > 0;
    }

    private static bool IsSplitDownstream(ILocalReferenceOperation reference)
    {
        var mode = QuerySplittingMode.None;
        IOperation current = reference;

        while (true)
        {
            var parent = current.Parent;
            if (parent is IConversionOperation)
            {
                current = parent;
                continue;
            }

            var next = GetChainedCall(current);
            if (next == null)
                return mode == QuerySplittingMode.Split;

            if (IsRelevantQueryOperator(next.TargetMethod))
            {
                if (next.TargetMethod.Name == "AsSplitQuery")
                    mode = QuerySplittingMode.Split;
                else if (next.TargetMethod.Name == "AsSingleQuery")
                    mode = QuerySplittingMode.Single;
            }

            // A call that leaves IQueryable (ToList, AsEnumerable, ...) runs or hands off the query here, so a later
            // AsSplitQuery() on its result no longer applies to it.
            // A project or third-party helper may run the query before handing it back, so only LINQ and EF Core
            // operators are followed.
            if (!IsQueryable(next.Type) || !IsKnownQueryOperator(next.TargetMethod))
                return mode == QuerySplittingMode.Split;

            current = next;
        }
    }

    /// <summary>The call that uses <paramref name="operation"/> as its receiver (instance or extension <c>this</c>).</summary>
    private static IInvocationOperation? GetChainedCall(IOperation operation)
    {
        var parent = operation.Parent;
        if (parent is IInvocationOperation instanceCall && ReferenceEquals(instanceCall.Instance, operation))
            return instanceCall;

        if (parent is IArgumentOperation { Parent: IInvocationOperation extensionCall } argument &&
            extensionCall.TargetMethod.IsExtensionMethod &&
            extensionCall.Arguments.Length > 0 &&
            ReferenceEquals(extensionCall.Arguments[0], argument))
        {
            return extensionCall;
        }

        return null;
    }

    private static bool IsKnownQueryOperator(IMethodSymbol method)
    {
        return method.ContainingNamespace?.ToDisplayString() is "System.Linq" or "Microsoft.EntityFrameworkCore";
    }

    private static bool IsQueryable(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (IsQueryableDefinition(type))
            return true;

        foreach (var candidate in type.AllInterfaces)
        {
            if (IsQueryableDefinition(candidate))
                return true;
        }

        return false;
    }

    private static bool IsQueryableDefinition(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { Name: "IQueryable", Arity: 1 } named &&
               named.ContainingNamespace?.ToDisplayString() == "System.Linq";
    }
}
