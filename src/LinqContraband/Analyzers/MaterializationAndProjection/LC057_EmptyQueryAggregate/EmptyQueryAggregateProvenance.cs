using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC057_EmptyQueryAggregate;

public sealed partial class EmptyQueryAggregateAnalyzer
{
    /// <summary>
    /// Proves that a query is EF Core-backed: its chain starts at a <c>DbSet</c>, <c>DbContext.Set&lt;T&gt;()</c> or
    /// <c>Database.SqlQuery</c>, through LINQ and EF Core query operators, locals whose every write is proven, and
    /// non-overridable project helpers whose every return is proven (by itself, or from the one
    /// <c>IQueryable</c> parameter the call passes a proven query to). A parameter, field, property, interface call
    /// or in-memory <c>AsQueryable()</c> is not proven, so the rule stays quiet on it.
    /// </summary>
    private sealed class EfQueryProvenance
    {
        private const int MaxDepth = 32;
        private const int NotProven = -2;
        private const int NoParameterNeeded = -1;

        private readonly Compilation compilation;
        private readonly ConcurrentDictionary<IMethodSymbol, int> helperSummaries =
            new(SymbolEqualityComparer.Default);

        public EfQueryProvenance(Compilation compilation)
        {
            this.compilation = compilation;
        }

        public bool IsProvablyEfQuery(IOperation operation)
        {
            return Walk(
                operation,
                null,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                0);
        }

        private bool Walk(
            IOperation? operation,
            IParameterSymbol? passThroughParameter,
            HashSet<ISymbol> inProgress,
            HashSet<IMethodSymbol> helpersInProgress,
            int depth)
        {
            var current = operation;
            for (; current != null && depth < MaxDepth; depth++)
            {
                current = current.UnwrapConversions();
                while (current is ITranslatedQueryOperation query)
                    current = query.Operation.UnwrapConversions();

                if (current is IParameterReferenceOperation parameterReference &&
                    passThroughParameter != null &&
                    SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, passThroughParameter))
                {
                    return WalkWrites(parameterReference.Parameter, parameterReference, passThroughParameter, inProgress, helpersInProgress, depth);
                }

                if (current.Type.IsDbSet())
                    return true;

                switch (current)
                {
                    case IInvocationOperation invocation:
                    {
                        var method = invocation.TargetMethod;
                        var receiver = invocation.GetInvocationReceiver();

                        if (IsQueryableAsQueryable(method))
                        {
                            if (receiver?.Type.IsIQueryable() != true)
                                return false;

                            current = receiver;
                            continue;
                        }

                        if (!invocation.Type.IsIQueryable())
                            return false;

                        if (IsEfRawQueryRoot(method))
                            return true;

                        if (method.OriginalDefinition.DeclaringSyntaxReferences.Length > 0)
                            return WalkHelperCall(invocation, passThroughParameter, inProgress, helpersInProgress, depth);

                        if (IsLibraryQueryOperator(method) && receiver?.Type.IsIQueryable() == true)
                        {
                            current = receiver;
                            continue;
                        }

                        return false;
                    }

                    case IConditionalOperation conditional when conditional.WhenFalse != null:
                        return Walk(conditional.WhenTrue, passThroughParameter, inProgress, helpersInProgress, depth + 1) &&
                               Walk(conditional.WhenFalse, passThroughParameter, inProgress, helpersInProgress, depth + 1);

                    case ILocalReferenceOperation localReference:
                        return WalkWrites(localReference.Local, localReference, passThroughParameter, inProgress, helpersInProgress, depth);

                    default:
                        return false;
                }
            }

            return false;
        }

        /// <summary>
        /// A local is proven when every write to it in its method is proven, treating reads of itself as proven
        /// (<c>q = q.Where(...)</c>). A local with a write the walk cannot follow is not proven.
        /// </summary>
        private bool WalkWrites(
            ISymbol variable,
            IOperation reference,
            IParameterSymbol? passThroughParameter,
            HashSet<ISymbol> inProgress,
            HashSet<IMethodSymbol> helpersInProgress,
            int depth)
        {
            if (inProgress.Contains(variable))
                return true;

            if (variable is ILocalSymbol { RefKind: not RefKind.None })
                return false;

            var root = reference;
            while (root.Parent != null)
                root = root.Parent;

            var writes = new List<IOperation>();
            foreach (var descendant in root.DescendantsAndSelf())
            {
                switch (descendant)
                {
                    case IVariableDeclaratorOperation declarator
                        when SymbolEqualityComparer.Default.Equals(declarator.Symbol, variable):
                        if (declarator.Initializer != null)
                            writes.Add(declarator.Initializer.Value);
                        break;

                    case ILocalReferenceOperation local
                        when SymbolEqualityComparer.Default.Equals(local.Local, variable):
                        if (!IsPlainReadOrSimpleWrite(local, writes))
                            return false;
                        break;

                    case IParameterReferenceOperation parameter
                        when SymbolEqualityComparer.Default.Equals(parameter.Parameter, variable):
                        if (!IsPlainReadOrSimpleWrite(parameter, writes))
                            return false;
                        break;
                }
            }

            if (variable is ILocalSymbol && writes.Count == 0)
                return false;

            // A parameter also carries its incoming argument, which the caller of the helper checks.
            if (variable is IParameterSymbol && writes.Count == 0)
                return true;

            inProgress.Add(variable);
            try
            {
                foreach (var write in writes)
                {
                    if (!Walk(write, passThroughParameter, inProgress, helpersInProgress, depth + 1))
                        return false;
                }
            }
            finally
            {
                inProgress.Remove(variable);
            }

            return true;
        }

        private static bool IsPlainReadOrSimpleWrite(IOperation reference, List<IOperation> writes)
        {
            if (reference is ILocalReferenceOperation { IsDeclaration: true })
                return false;

            switch (reference.Parent)
            {
                case ISimpleAssignmentOperation assignment when assignment.Target == reference:
                    if (assignment.IsRef) return false;
                    writes.Add(assignment.Value);
                    return true;
                case IAssignmentOperation assignment when assignment.Target == reference:
                    return false;
                case ICoalesceAssignmentOperation coalesce when coalesce.Target == reference:
                    return false;
                case IArgumentOperation argument:
                    return argument.Parameter?.RefKind is null or RefKind.None or RefKind.In;
                case ITupleOperation:
                case IDeconstructionAssignmentOperation:
                    return false;
                case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } }:
                    return false;
                default:
                    return true;
            }
        }

        private bool WalkHelperCall(
            IInvocationOperation invocation,
            IParameterSymbol? passThroughParameter,
            HashSet<ISymbol> inProgress,
            HashSet<IMethodSymbol> helpersInProgress,
            int depth)
        {
            var method = invocation.TargetMethod.OriginalDefinition;
            if (!helpersInProgress.Add(method))
                return false;

            try
            {
                var ordinal = GetHelperSummary(method, helpersInProgress);
                if (ordinal == NotProven)
                    return false;

                if (ordinal == NoParameterNeeded)
                    return true;

                var argument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == ordinal);
                return argument != null &&
                       argument.ArgumentKind == ArgumentKind.Explicit &&
                       Walk(argument.Value, passThroughParameter, inProgress, helpersInProgress, depth + 1);
            }
            finally
            {
                helpersInProgress.Remove(method);
            }
        }

        private int GetHelperSummary(IMethodSymbol method, HashSet<IMethodSymbol> helpersInProgress)
        {
            if (helperSummaries.TryGetValue(method, out var cached))
                return cached;

            var summary = ComputeHelperSummary(method, helpersInProgress);

            // A summary computed while another helper was on the stack may have been cut short by the recursion
            // guard; only cache top-level results.
            if (helpersInProgress.Count == 1)
                helperSummaries.TryAdd(method, summary);

            return summary;
        }

        private int ComputeHelperSummary(IMethodSymbol method, HashSet<IMethodSymbol> helpersInProgress)
        {
            if (method.IsVirtual || method.IsAbstract || method.IsOverride || method.IsAsync ||
                method.IsExtern || method.MethodKind != MethodKind.Ordinary)
            {
                return NotProven;
            }

            var body = GetBodyOperation(method);
            if (body == null)
                return NotProven;

            var returns = body.Descendants()
                .OfType<IReturnOperation>()
                .Where(r => r.Kind == OperationKind.Return && BelongsToBody(r, body))
                .ToList();

            if (returns.Count == 0 || returns.Any(r => r.ReturnedValue == null))
                return NotProven;

            if (ReturnsProven(returns, null, helpersInProgress))
                return NoParameterNeeded;

            foreach (var parameter in method.Parameters)
            {
                if (!parameter.Type.IsIQueryable() || parameter.RefKind is RefKind.Ref or RefKind.Out)
                    continue;

                if (ReturnsProven(returns, parameter, helpersInProgress))
                    return parameter.Ordinal;
            }

            return NotProven;
        }

        private bool ReturnsProven(
            List<IReturnOperation> returns,
            IParameterSymbol? parameter,
            HashSet<IMethodSymbol> helpersInProgress)
        {
            var inProgress = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            return returns.All(r => Walk(r.ReturnedValue, parameter, inProgress, helpersInProgress, 0));
        }

        private static bool BelongsToBody(IOperation operation, IOperation body)
        {
            for (var current = operation.Parent; current != null && current != body; current = current.Parent)
            {
                if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                    return false;
            }

            return true;
        }

        private IOperation? GetBodyOperation(IMethodSymbol method)
        {
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not MethodDeclarationSyntax declaration ||
                    (declaration.Body == null && declaration.ExpressionBody == null))
                {
                    continue;
                }

                if (!compilation.TryGetOwnedSemanticModel(declaration.SyntaxTree, out var model))
                    return null;

                return model.GetOperation(declaration);
            }

            return null;
        }

        private static bool IsQueryableAsQueryable(IMethodSymbol method)
        {
            var original = method.ReducedFrom ?? method;
            return original.Name == "AsQueryable" &&
                   original.ContainingType is { Name: "Queryable" } containingType &&
                   containingType.ContainingNamespace?.ToDisplayString() == "System.Linq";
        }

        private static bool IsLibraryQueryOperator(IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            if (containingType == null || !method.IsStatic)
                return false;

            var ns = containingType.ContainingNamespace?.ToDisplayString();
            return (containingType.Name == "Queryable" && ns == "System.Linq") ||
                   (ns != null && (ns == "Microsoft.EntityFrameworkCore" ||
                                   ns.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal)));
        }

        /// <summary><c>Database.SqlQuery&lt;T&gt;()</c> and <c>SqlQueryRaw&lt;T&gt;()</c> start an EF Core query.</summary>
        private static bool IsEfRawQueryRoot(IMethodSymbol method)
        {
            var ns = method.ContainingType?.ContainingNamespace?.ToDisplayString();
            return method.Name is "SqlQuery" or "SqlQueryRaw" &&
                   ns != null &&
                   (ns == "Microsoft.EntityFrameworkCore" ||
                    ns.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal));
        }
    }
}
