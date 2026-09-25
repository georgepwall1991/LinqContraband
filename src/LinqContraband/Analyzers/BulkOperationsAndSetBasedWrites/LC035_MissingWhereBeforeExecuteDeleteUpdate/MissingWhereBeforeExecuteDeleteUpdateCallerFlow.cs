using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public sealed partial class MissingWhereBeforeExecuteDeleteUpdateAnalyzer
{
    // A helper such as `DeleteInternalAsync(IQueryable<T> query) => query.ExecuteDeleteAsync()` cannot
    // tell from its own body whether the query is filtered; its callers can. Executes rooted at such a
    // parameter wait for compilation end, when every call site of the helper in the compilation is known.
    private sealed class CallerFlowState
    {
        private readonly ConcurrentBag<DeferredExecute> _executes = new();
        private readonly ConcurrentDictionary<(IMethodSymbol Method, int Ordinal), ConcurrentBag<CallSite>> _callSites =
            new(new ParameterKeyComparer());
        private readonly ConcurrentDictionary<IMethodSymbol, byte> _methodGroupReferences =
            new(SymbolEqualityComparer.Default);

        public void AddExecute(Location location, string methodName, IEnumerable<IParameterSymbol> parameters)
        {
            _executes.Add(new DeferredExecute(location, methodName, parameters.Select(ToKey).ToArray()));
        }

        public void RecordCallSites(IInvocationOperation invocation, CancellationToken cancellationToken)
        {
            var method = NormalizeMethod(invocation.TargetMethod);
            if (!method.Locations.Any(location => location.IsInSource))
                return;

            foreach (var argument in invocation.Arguments)
            {
                var parameter = argument.Parameter;
                if (parameter == null ||
                    argument.ArgumentKind != ArgumentKind.Explicit ||
                    !IsQueryType(parameter.Type))
                {
                    continue;
                }

                var state = new LocalFlowState(trackParameters: true);
                var filtered = HasWhereInChain(argument.Value, cancellationToken, state);
                var site = new CallSite(
                    argument.Value.Syntax.GetLocation(),
                    invocation.TargetMethod.Name,
                    filtered,
                    state.ParameterRoots!.Select(ToKey).ToArray());

                _callSites.GetOrAdd((method, parameter.Ordinal), _ => new ConcurrentBag<CallSite>()).Add(site);
            }
        }

        public void RecordMethodReference(OperationAnalysisContext context)
        {
            var reference = (IMethodReferenceOperation)context.Operation;
            _methodGroupReferences.TryAdd(NormalizeMethod(reference.Method), 0);
        }

        public void Report(CompilationAnalysisContext context)
        {
            var resolver = new Resolver(this);
            var reported = new HashSet<Location>();

            foreach (var execute in _executes)
            {
                var blame = new List<(Location Location, string MethodName)>();
                foreach (var parameter in execute.Parameters)
                    resolver.CollectBlame(parameter, execute, blame, new HashSet<(IMethodSymbol, int)>(new ParameterKeyComparer()));

                foreach (var (location, methodName) in blame)
                {
                    if (reported.Add(location))
                        context.ReportDiagnostic(Diagnostic.Create(CallerFlowRule, location, methodName));
                }
            }
        }

        private sealed class Resolver
        {
            private readonly CallerFlowState _state;
            private readonly Dictionary<(IMethodSymbol, int), bool> _safe = new(new ParameterKeyComparer());
            private readonly HashSet<(IMethodSymbol, int)> _inProgress = new(new ParameterKeyComparer());

            public Resolver(CallerFlowState state) => _state = state;

            // A parameter is safe when its callers are all visible and every one passes a filtered query.
            // A cycle (a recursive helper passing its own parameter) adds no new source, so it counts as safe.
            public bool IsSafe((IMethodSymbol Method, int Ordinal) key)
            {
                if (_safe.TryGetValue(key, out var known))
                    return known;

                if (!_inProgress.Add(key))
                    return true;

                var result = HasVisibleCallers(key, out var sites) &&
                             sites.All(site => site.Filtered && site.Dependencies.All(IsSafe));

                _inProgress.Remove(key);
                _safe[key] = result;
                return result;
            }

            public void CollectBlame(
                (IMethodSymbol Method, int Ordinal) key,
                DeferredExecute execute,
                List<(Location, string)> blame,
                HashSet<(IMethodSymbol, int)> visited)
            {
                if (IsSafe(key) || !visited.Add(key))
                    return;

                if (!HasVisibleCallers(key, out var sites))
                {
                    blame.Add((execute.Location, execute.MethodName));
                    return;
                }

                foreach (var site in sites)
                {
                    if (!site.Filtered)
                    {
                        blame.Add((site.Location, site.MethodName));
                        continue;
                    }

                    foreach (var dependency in site.Dependencies)
                        CollectBlame(dependency, execute, blame, visited);
                }
            }

            private bool HasVisibleCallers((IMethodSymbol Method, int Ordinal) key, out CallSite[] sites)
            {
                sites = System.Array.Empty<CallSite>();
                if (_state._methodGroupReferences.ContainsKey(key.Method) || CanBeCalledThroughAnotherSymbol(key.Method))
                    return false;

                if (!_state._callSites.TryGetValue(key, out var bag))
                    return false;

                sites = bag.ToArray();
                return sites.Length > 0;
            }
        }

        private static (IMethodSymbol Method, int Ordinal) ToKey(IParameterSymbol parameter)
        {
            return (NormalizeMethod((IMethodSymbol)parameter.ContainingSymbol), parameter.Ordinal);
        }
    }

    private sealed class DeferredExecute
    {
        public DeferredExecute(Location location, string methodName, (IMethodSymbol, int)[] parameters)
        {
            Location = location;
            MethodName = methodName;
            Parameters = parameters;
        }

        public Location Location { get; }
        public string MethodName { get; }
        public (IMethodSymbol Method, int Ordinal)[] Parameters { get; }
    }

    private sealed class CallSite
    {
        public CallSite(Location location, string methodName, bool filtered, (IMethodSymbol, int)[] dependencies)
        {
            Location = location;
            MethodName = methodName;
            Filtered = filtered;
            Dependencies = dependencies;
        }

        public Location Location { get; }
        public string MethodName { get; }
        public bool Filtered { get; }
        public (IMethodSymbol Method, int Ordinal)[] Dependencies { get; }
    }

    private sealed class ParameterKeyComparer : IEqualityComparer<(IMethodSymbol Method, int Ordinal)>
    {
        public bool Equals((IMethodSymbol Method, int Ordinal) x, (IMethodSymbol Method, int Ordinal) y) =>
            x.Ordinal == y.Ordinal && SymbolEqualityComparer.Default.Equals(x.Method, y.Method);

        public int GetHashCode((IMethodSymbol Method, int Ordinal) obj) =>
            SymbolEqualityComparer.Default.GetHashCode(obj.Method) * 31 + obj.Ordinal;
    }

    private static IMethodSymbol NormalizeMethod(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        return definition.PartialDefinitionPart ?? definition;
    }

    private static bool IsQueryType(ITypeSymbol? type) => type.IsIQueryable() || type.IsDbSet();

    // Virtual, override and interface methods are also reached through another symbol's call sites,
    // which this analysis does not collect.
    private static bool CanBeCalledThroughAnotherSymbol(IMethodSymbol method)
    {
        if (method.IsStatic || method.MethodKind == MethodKind.LocalFunction)
            return false;

        if (method.IsVirtual || method.IsAbstract || method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0)
            return true;

        var type = method.ContainingType;
        if (type == null)
            return false;

        foreach (var @interface in type.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(method.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), method))
                    return true;
            }
        }

        return false;
    }

    // A parameter of an ordinary method or local function, never reassigned in its body, holds what
    // the caller passed; its call sites can be found in the compilation.
    private static bool IsCallerTrackedParameter(IParameterReferenceOperation reference, CancellationToken cancellationToken)
    {
        var parameter = reference.Parameter;
        if (parameter.RefKind != RefKind.None ||
            parameter.IsParams ||
            !parameter.Type.IsIQueryable() ||
            parameter.Type.IsDbSet() ||
            parameter.ContainingSymbol is not IMethodSymbol
            {
                MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction
            } method ||
            !method.Locations.Any(location => location.IsInSource))
        {
            return false;
        }

        var body = FindDeclaringBody(reference, method);
        if (body == null)
            return false;

        foreach (var descendant in body.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (descendant is not IParameterReferenceOperation other ||
                !SymbolEqualityComparer.Default.Equals(other.Parameter, parameter))
            {
                continue;
            }

            if (IsWrite(other))
                return false;
        }

        return true;
    }

    private static IOperation? FindDeclaringBody(IOperation reference, IMethodSymbol method)
    {
        var current = reference;
        while (current != null)
        {
            if (current is ILocalFunctionOperation localFunction &&
                SymbolEqualityComparer.Default.Equals(localFunction.Symbol, method))
            {
                return localFunction;
            }

            if (current.Parent == null)
                return method.MethodKind == MethodKind.LocalFunction ? null : current;

            current = current.Parent;
        }

        return null;
    }

    private static bool IsWrite(IParameterReferenceOperation reference)
    {
        return reference.Parent switch
        {
            IAssignmentOperation assignment => ReferenceEquals(assignment.Target, reference),
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            ITupleOperation => true,
            _ => false
        };
    }

    // A project helper such as `ApplyExpiredFilter(IQueryable<Grant> q) => q.Where(...)` filters when
    // every value its body returns carries a Where.
    private static bool IsFilterHelper(
        IInvocationOperation invocation,
        CancellationToken cancellationToken,
        LocalFlowState visitedLocals)
    {
        var method = NormalizeMethod(invocation.TargetMethod);
        if (!IsQueryType(method.ReturnType) ||
            method.IsAbstract ||
            invocation.SemanticModel == null ||
            method.DeclaringSyntaxReferences.Length != 1 ||
            !visitedLocals.HelpersInProgress.Add(method))
        {
            return false;
        }

        try
        {
            var declaration = method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            if (declaration is not (MethodDeclarationSyntax or LocalFunctionStatementSyntax) ||
                !invocation.SemanticModel.Compilation.TryGetOwnedSemanticModel(declaration.SyntaxTree, out var model))
            {
                return false;
            }

            var body = model.GetOperation(declaration, cancellationToken);
            if (body == null)
                return false;

            var returns = body.Descendants()
                .OfType<IReturnOperation>()
                .Where(returnOperation => returnOperation.Kind == OperationKind.Return &&
                                          ReferenceEquals(returnOperation.FindOwningExecutableRoot(), body))
                .ToArray();

            var helperState = new LocalFlowState(trackParameters: false, visitedLocals.HelpersInProgress);
            return returns.Length > 0 &&
                   returns.All(returnOperation =>
                       returnOperation.ReturnedValue != null &&
                       HasWhereInChain(returnOperation.ReturnedValue, cancellationToken, helperState));
        }
        finally
        {
            visitedLocals.HelpersInProgress.Remove(method);
        }
    }
}
