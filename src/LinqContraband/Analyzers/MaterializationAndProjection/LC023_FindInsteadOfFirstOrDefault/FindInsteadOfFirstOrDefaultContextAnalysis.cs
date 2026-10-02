using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public sealed partial class FindInsteadOfFirstOrDefaultAnalyzer
{
    /// <summary>
    /// The DbContext the DbSet receiver was read from (db.Users, db.Set&lt;User&gt;()), or null
    /// when the receiver is a bare DbSet parameter, field or local. Sets
    /// <paramref name="isFreshFactoryContext"/> when that context is a local created by
    /// IDbContextFactory.CreateDbContext/CreateDbContextAsync.
    /// </summary>
    private static ITypeSymbol? TryGetReceiverContext(
        IOperation receiver,
        CancellationToken cancellationToken,
        out bool isFreshFactoryContext)
    {
        isFreshFactoryContext = false;

        var instance = receiver switch
        {
            IPropertyReferenceOperation property => property.Instance,
            IFieldReferenceOperation field => field.Instance,
            IInvocationOperation { TargetMethod: { Name: "Set" } } setCall => setCall.Instance,
            _ => null
        };

        instance = instance?.UnwrapConversions();
        if (instance?.Type is not { } contextType || !contextType.IsDbContext())
            return null;

        if (instance is ILocalReferenceOperation localReference)
            isFreshFactoryContext = IsInitializedFromDbContextFactory(localReference, cancellationToken);

        return contextType;
    }

    private static bool IsInitializedFromDbContextFactory(
        ILocalReferenceOperation localReference,
        CancellationToken cancellationToken)
    {
        var semanticModel = localReference.SemanticModel;
        if (semanticModel == null)
            return false;

        foreach (var reference in localReference.Local.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree != semanticModel.SyntaxTree ||
                reference.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax { Initializer: { } initializer })
            {
                continue;
            }

            var value = semanticModel.GetOperation(initializer.Value, cancellationToken)?.UnwrapConversions();
            if (value is IAwaitOperation awaitOperation)
                value = awaitOperation.Operation.UnwrapConversions();

            if (value is IInvocationOperation invocation && IsDbContextFactoryCreate(invocation.TargetMethod))
                return true;
        }

        return false;
    }

    private static bool IsDbContextFactoryCreate(IMethodSymbol method)
    {
        if (method.Name is not ("CreateDbContext" or "CreateDbContextAsync"))
            return false;

        var containingType = method.ContainingType;
        return IsDbContextFactoryDefinition(containingType) ||
               containingType.AllInterfaces.Any(IsDbContextFactoryDefinition);
    }

    private static bool IsDbContextFactoryDefinition(INamedTypeSymbol type)
    {
        return type.Name == "IDbContextFactory" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }
}
