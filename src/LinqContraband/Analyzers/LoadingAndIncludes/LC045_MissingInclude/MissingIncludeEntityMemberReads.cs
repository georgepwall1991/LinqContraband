using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private const int MaxEntityMemberChainLength = 8;

    /// <summary>
    /// One chain of properties a computed entity member reads on <c>this</c>:
    /// <c>Customer.Address.City</c> is <c>[Customer, Address, City]</c>. Which of them are
    /// navigations depends on the context the query runs on, so that is decided at the read.
    /// </summary>
    private readonly struct EntityMemberRead
    {
        public EntityMemberRead(
            ImmutableArray<IPropertySymbol> chain,
            bool isWrite,
            bool isCollectionMutatorReceiver
        )
        {
            Chain = chain;
            IsWrite = isWrite;
            IsCollectionMutatorReceiver = isCollectionMutatorReceiver;
        }

        public ImmutableArray<IPropertySymbol> Chain { get; }

        public bool IsWrite { get; }

        public bool IsCollectionMutatorReceiver { get; }
    }

    /// <summary>
    /// What the getter of a computed property the entity declares itself reads on <c>this</c>:
    /// <c>public string CustomerLabel =&gt; Customer.Name;</c> reads <c>Customer</c>. Reading
    /// <c>order.CustomerLabel</c> runs that getter, so the caller reads <c>order.Customer</c>
    /// exactly as if it had written it.
    ///
    /// Empty when the body is not certain or could load the navigation itself: an auto-property,
    /// an overridable property (a derived entity may run another body), a declaration in
    /// another compilation, or a getter that uses <c>this</c> for anything but reading its own
    /// properties and fields. Calling a method on itself or handing itself to one is how an
    /// entity loads its own navigations (<c>ILazyLoader.Load(this, ref _customer)</c>).
    /// </summary>
    private static ImmutableArray<EntityMemberRead> FindEntityMemberReads(
        IPropertySymbol property,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var none = ImmutableArray<EntityMemberRead>.Empty;
        if (
            property.IsStatic
            || property.IsIndexer
            || property.IsVirtual
            || property.IsAbstract
            || property.IsOverride
            || property.GetMethod is not { DeclaringSyntaxReferences: { Length: 1 } references }
        )
        {
            return none;
        }

        var declaration = references[0].GetSyntax(cancellationToken);
        var bodySyntax = declaration switch
        {
            ArrowExpressionClauseSyntax arrow => arrow.Expression,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body
                ?? accessor.ExpressionBody?.Expression,
            _ => null,
        };

        if (
            bodySyntax == null
            || !compilation.TryGetOwnedSemanticModel(declaration.SyntaxTree, out var semanticModel)
            || semanticModel.GetOperation(bodySyntax, cancellationToken) is not { } body
        )
        {
            return none;
        }

        var reads = ImmutableArray.CreateBuilder<EntityMemberRead>();
        foreach (var operation in body.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IInstanceReferenceOperation
                {
                    ReferenceKind: InstanceReferenceKind.ContainingTypeInstance
                } self when !IsOwnMemberReceiver(self):
                    return none;

                case IPropertyReferenceOperation read
                    when !IsInsideNameOf(read) && TryGetOwnPropertyChain(read, out var chain):
                    reads.Add(
                        new EntityMemberRead(
                            chain,
                            IsWriteTarget(read),
                            IsCollectionMutatorReceiver(read)
                        )
                    );
                    break;
            }
        }

        return reads.ToImmutable();
    }

    /// <summary>
    /// True when <c>this</c> is only the receiver of a property or field read:
    /// <c>Customer</c>, <c>this.Customer</c>, <c>_customer</c>.
    /// </summary>
    private static bool IsOwnMemberReceiver(IInstanceReferenceOperation self)
    {
        IOperation child = self;
        var parent = self.Parent;
        while (parent is IConversionOperation or IParenthesizedOperation)
        {
            child = parent;
            parent = parent.Parent;
        }

        return parent is IPropertyReferenceOperation or IFieldReferenceOperation
            && ReferenceEquals(((IMemberReferenceOperation)parent).Instance, child);
    }

    /// <summary>
    /// The properties from <c>this</c> down to <paramref name="read"/>, outermost first.
    /// </summary>
    private static bool TryGetOwnPropertyChain(
        IPropertyReferenceOperation read,
        out ImmutableArray<IPropertySymbol> chain
    )
    {
        chain = default;
        var reversed = new List<IPropertySymbol>();
        IOperation? current = read;
        while (current is IPropertyReferenceOperation property)
        {
            // The bound also ends a conditional-access resolution that hands back the property
            // it was asked about.
            if (reversed.Count == MaxEntityMemberChainLength)
                return false;

            reversed.Add(property.Property);
            var instance = property.Instance?.UnwrapConversions();
            if (instance is IConditionalAccessInstanceOperation)
                instance = ResolveConditionalAccessReceiver(property)?.UnwrapConversions();

            current = instance;
        }

        if (
            current
            is not IInstanceReferenceOperation
            {
                ReferenceKind: InstanceReferenceKind.ContainingTypeInstance
            }
        )
        {
            return false;
        }

        reversed.Reverse();
        chain = reversed.ToImmutableArray();
        return true;
    }

    /// <summary>
    /// The navigation path a chain of the entity's own properties spells on this context:
    /// every step has to be a navigation, and only the last may be a collection.
    /// </summary>
    private static bool TryGetNavigationPath(
        ImmutableArray<IPropertySymbol> chain,
        INamedTypeSymbol ownerEntityType,
        HashSet<INamedTypeSymbol> entityTypes,
        out string path,
        out bool endsInCollection
    )
    {
        path = string.Empty;
        endsInCollection = false;
        var currentEntity = ownerEntityType;
        for (var index = 0; index < chain.Length; index++)
        {
            var step = chain[index];
            if (
                !IsPropertyOfEntity(step, currentEntity)
                || !TryGetNavigationTarget(step, entityTypes, out var target, out var isCollection)
            )
            {
                return false;
            }

            path = CombineNavigationPath(path, step.Name);
            if (isCollection)
            {
                endsInCollection = true;
                return index == chain.Length - 1;
            }

            currentEntity = target;
        }

        return chain.Length > 0;
    }
}
