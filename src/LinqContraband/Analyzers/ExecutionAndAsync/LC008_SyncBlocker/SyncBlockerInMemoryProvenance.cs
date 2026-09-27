using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC008_SyncBlocker;

public sealed partial class SyncBlockerAnalyzer
{
    /// <summary>
    /// Proves that a query runs on LINQ to Objects, so a sync terminal on it does no I/O and the
    /// async rewrite would throw ("The source IQueryable doesn't implement IAsyncEnumerable").
    /// Beyond the shared <see cref="InMemoryQueryableProvenance"/> walk it follows a local through
    /// every write in its method (each must be in-memory-rooted or composed from the same local)
    /// and follows calls to non-overridable source helpers whose returned query is composed only
    /// from one <c>IQueryable</c> parameter, continuing with that call's argument, and library helpers
    /// handed only an in-memory query (see <see cref="WalkLibraryHelperCall"/>). Anything else
    /// (a parameter, field, property, <c>DbSet</c>, or an unfollowable helper) is not proven.
    /// </summary>
    private sealed class InMemoryQueryProvenance
    {
        private const int MaxDepth = 32;
        private const int NotPassThrough = -2;
        private const int NoParameterNeeded = -1;

        private readonly Compilation compilation;
        private readonly ConcurrentDictionary<IMethodSymbol, int> helperSummaries =
            new(SymbolEqualityComparer.Default);

        public InMemoryQueryProvenance(Compilation compilation)
        {
            this.compilation = compilation;
        }

        public bool IsProvablyInMemory(IOperation? operation)
        {
            return Walk(operation, null, new HashSet<ISymbol>(SymbolEqualityComparer.Default), new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default), 0);
        }

        /// <param name="passThroughParameter">Inside a helper body: the parameter whose incoming value counts as proven.</param>
        /// <param name="inProgress">Locals and parameters whose writes are being checked; a read of one is the induction step.</param>
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

                switch (current)
                {
                    case IInvocationOperation invocation:
                    {
                        var method = invocation.TargetMethod;
                        var receiver = invocation.GetInvocationReceiver();

                        if (IsQueryableAsQueryable(method))
                        {
                            if (receiver?.Type.IsIQueryable() == true)
                            {
                                current = receiver;
                                continue;
                            }

                            return InMemoryQueryableProvenance.IsInMemorySequenceSource(receiver, IsNonEntitySequence);
                        }

                        if (!invocation.Type.IsIQueryable())
                            return false;

                        if (IsOwnedSourceMethod(method))
                            return WalkHelperCall(invocation, passThroughParameter, inProgress, helpersInProgress, depth);

                        // Library query operators (Queryable.Where, EF's AsNoTracking, ...) keep the provider of their source.
                        if (method.IsExtensionMethod && receiver?.Type.IsIQueryable() == true)
                        {
                            current = receiver;
                            continue;
                        }

                        return WalkLibraryHelperCall(invocation, passThroughParameter, inProgress, helpersInProgress, depth);
                    }

                    case IObjectCreationOperation creation:
                        return creation.Type is INamedTypeSymbol { Name: "EnumerableQuery", Arity: 1 } created &&
                               created.ContainingNamespace?.ToDisplayString() == "System.Linq";

                    case IConditionalOperation conditional when conditional.WhenFalse != null:
                        return Walk(conditional.WhenTrue, passThroughParameter, inProgress, helpersInProgress, depth + 1) &&
                               Walk(conditional.WhenFalse, passThroughParameter, inProgress, helpersInProgress, depth + 1);

                    case ILocalReferenceOperation localReference:
                        return WalkWrites(localReference.Local, localReference, passThroughParameter, inProgress, helpersInProgress, depth);

                    case IParameterReferenceOperation parameterReference:
                    {
                        if (passThroughParameter == null ||
                            !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, passThroughParameter) ||
                            passThroughParameter.RefKind is RefKind.Ref or RefKind.Out)
                            return false;

                        return WalkWrites(parameterReference.Parameter, parameterReference, passThroughParameter, inProgress, helpersInProgress, depth);
                    }

                    default:
                        return false;
                }
            }

            return false;
        }

        /// <summary>
        /// A local (or the pass-through parameter) is proven when every write to it anywhere in its
        /// method is proven, treating reads of itself as proven (<c>q = q.Where(...)</c>). A parameter
        /// also carries its incoming argument, which the caller checks.
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

            // A local with no write we can see (foreach, pattern or out variable) is not followed.
            if (variable is ILocalSymbol && writes.Count == 0)
                return false;

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

        /// <summary>
        /// A read, or the target of a simple assignment (whose value joins <paramref name="writes"/>).
        /// Any other write (ref/out argument, <c>??=</c>, deconstruction, ref alias) is not followed.
        /// </summary>
        private static bool IsPlainReadOrSimpleWrite(IOperation reference, List<IOperation> writes)
        {
            if (reference is ILocalReferenceOperation { IsDeclaration: true })
                return false;

            var parent = reference.Parent;
            switch (parent)
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

        /// <summary>
        /// A static or extension helper without source (a referenced project or package, such as
        /// SimpleIdServer's <c>scimFilter.EvaluateAttributes(attributes.AsQueryable(), false)</c>) that
        /// returns a query. It is proven when at least one argument is a query proven in-memory, every other
        /// sequence argument is an in-memory sequence, and every other argument is inert (see
        /// <see cref="IsInertArgumentType"/>): nothing it is given can hand it an EF query. The helper itself
        /// must not be able to start one: a helper from EF Core or from an assembly that references it, or one
        /// declared in this project whose body the walk cannot see, is not followed.
        /// </summary>
        private bool WalkLibraryHelperCall(
            IInvocationOperation invocation,
            IParameterSymbol? passThroughParameter,
            HashSet<ISymbol> inProgress,
            HashSet<IMethodSymbol> helpersInProgress,
            int depth)
        {
            if (invocation.Instance != null)
                return false;

            var helperAssembly = invocation.TargetMethod.ContainingAssembly;
            if (helperAssembly == null ||
                SymbolEqualityComparer.Default.Equals(helperAssembly, compilation.Assembly) ||
                ReferencesEntityFramework(helperAssembly))
                return false;

            var provenQuery = false;
            foreach (var argument in invocation.Arguments)
            {
                if (argument.ArgumentKind == ArgumentKind.ParamArray)
                    return false;

                if (argument.ArgumentKind == ArgumentKind.DefaultValue)
                    continue;

                var value = argument.Value.UnwrapConversions();
                var type = value.Type;
                if (type == null)
                    continue;

                if (type.IsIQueryable())
                {
                    if (!Walk(value, passThroughParameter, inProgress, helpersInProgress, depth + 1))
                        return false;

                    provenQuery = true;
                    continue;
                }

                if (type.SpecialType != SpecialType.System_String && IsSequence(type))
                {
                    if (!InMemoryQueryableProvenance.IsInMemorySequenceSource(value, IsNonEntitySequence))
                        return false;

                    continue;
                }

                if (!IsInertArgumentType(type, 0))
                    return false;
            }

            return provenQuery;
        }

        private const int MaxInertMemberDepth = 3;
        private const int MaxReferencedAssemblies = 256;

        /// <summary>
        /// An argument that cannot give a library helper a way to reach EF: a primitive, string or enum; a
        /// <c>System</c> struct (<c>Guid</c>, <c>DateTime</c>, ..., generic ones only when every type argument is
        /// inert); or a class or struct from an assembly that neither is this project nor reaches EF Core, whose
        /// instance fields and properties (base types included, to <see cref="MaxInertMemberDepth"/> levels) are
        /// all inert too, such as SimpleIdServer's <c>SCIMExpression</c>. A member typed as a query or sequence,
        /// <c>object</c>, <c>dynamic</c>, a delegate, an <c>Expression</c>, a type parameter or an interface
        /// could carry <c>db.Users</c> (a <c>QueryBox&lt;T&gt;</c> with an <c>IQueryable&lt;T&gt;</c> property), and so
        /// could a delegate argument, so they block the proof, as do a <c>DbContext</c> and any type of this project.
        /// </summary>
        private bool IsInertArgumentType(ITypeSymbol type, int depth)
        {
            return IsInertType(type, depth, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        }

        private bool IsInertType(ITypeSymbol type, int depth, HashSet<ITypeSymbol> visiting)
        {
            if (type.SpecialType == SpecialType.System_Object ||
                type.TypeKind is TypeKind.Dynamic or TypeKind.TypeParameter or TypeKind.Error or TypeKind.Delegate or TypeKind.Interface ||
                type.IsDbContext() || type.IsIQueryable())
                return false;

            if (type.SpecialType == SpecialType.System_String || type.TypeKind == TypeKind.Enum)
                return true;

            if (type.SpecialType != SpecialType.None)
                return type.IsValueType;

            if (type is not INamedTypeSymbol named || IsSequence(named) || IsLinqExpression(named))
                return false;

            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                return IsInertType(named.TypeArguments[0], depth, visiting);

            if (named.TypeKind == TypeKind.Struct && IsInSystemNamespace(named))
                return named.TypeArguments.All(t => IsInertType(t, depth, visiting));

            if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                return false;

            var assembly = named.ContainingAssembly;
            if (assembly == null ||
                SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly) ||
                ReferencesEntityFramework(assembly))
                return false;

            // A type already being checked higher up (SCIMExpression.Child) is judged by that check.
            if (!visiting.Add(named))
                return true;

            try
            {
                if (depth >= MaxInertMemberDepth)
                    return false;

                for (var current = named; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
                {
                    if (!SymbolEqualityComparer.Default.Equals(current, named) &&
                        (current.ContainingAssembly == null ||
                         SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, compilation.Assembly) ||
                         ReferencesEntityFramework(current.ContainingAssembly)))
                        return false;

                    foreach (var member in current.GetMembers())
                    {
                        if (member.IsStatic)
                            continue;

                        var memberType = member switch
                        {
                            IFieldSymbol field => field.Type,
                            IPropertySymbol property => property.Type,
                            _ => null
                        };

                        if (memberType != null && !IsInertType(memberType, depth + 1, visiting))
                            return false;
                    }
                }

                return true;
            }
            finally
            {
                visiting.Remove(named);
            }
        }

        private static bool IsLinqExpression(INamedTypeSymbol type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.Name == "Expression" &&
                    current.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions")
                    return true;
            }

            return false;
        }

        private static bool IsInSystemNamespace(INamedTypeSymbol type)
        {
            var ns = type.ContainingNamespace?.ToDisplayString();
            return ns != null && (ns == "System" || ns.StartsWith("System.", System.StringComparison.Ordinal));
        }

        private readonly ConcurrentDictionary<IAssemblySymbol, bool> efAwareAssemblies =
            new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Whether the assembly could build an EF query on its own: it is EF Core, or EF Core is reachable
        /// through its references, followed transitively (a facade over EF Core counts). A reference that
        /// cannot be resolved, or a graph larger than <see cref="MaxReferencedAssemblies"/>, is not trusted.
        /// Framework assemblies (<c>System.*</c>, <c>netstandard</c>, ...) never reference EF Core and are not
        /// followed.
        /// </summary>
        private bool ReferencesEntityFramework(IAssemblySymbol assembly)
        {
            return efAwareAssemblies.GetOrAdd(assembly, static root =>
            {
                var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default) { root };
                var pending = new Stack<IAssemblySymbol>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    if (IsEntityFrameworkAssemblyName(current.Identity.Name))
                        return true;

                    foreach (var module in current.Modules)
                    {
                        var identities = module.ReferencedAssemblies;
                        var symbols = module.ReferencedAssemblySymbols;
                        if (identities.Length != symbols.Length)
                            return true;

                        for (var i = 0; i < symbols.Length; i++)
                        {
                            var name = identities[i].Name;
                            if (IsEntityFrameworkAssemblyName(name))
                                return true;

                            if (IsFrameworkAssemblyName(name))
                                continue;

                            var referenced = symbols[i];
                            if (referenced == null || IsUnresolved(referenced))
                                return true;

                            if (!visited.Add(referenced))
                                continue;

                            if (visited.Count > MaxReferencedAssemblies)
                                return true;

                            pending.Push(referenced);
                        }
                    }
                }

                return false;
            });
        }

        /// <summary>
        /// Roslyn stands in for a reference it cannot resolve with an empty assembly symbol.
        /// </summary>
        private static bool IsUnresolved(IAssemblySymbol assembly)
        {
            return assembly.Modules.All(m => !m.GlobalNamespace.GetMembers().Any());
        }

        private static bool IsFrameworkAssemblyName(string name)
        {
            return name is "System" or "mscorlib" or "netstandard" or "WindowsBase" or "Microsoft.CSharp" or "Microsoft.VisualBasic" ||
                   name.StartsWith("System.", System.StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.Win32.", System.StringComparison.Ordinal);
        }

        private static bool IsEntityFrameworkAssemblyName(string name)
        {
            return name.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal);
        }

        private static bool IsSequence(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol)
                return true;

            if (type.SpecialType == SpecialType.System_Collections_IEnumerable ||
                type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                return true;

            return type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
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
                if (ordinal == NotPassThrough)
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

        /// <summary>
        /// Which parameter a helper's returned query is composed from: its ordinal,
        /// <see cref="NoParameterNeeded"/> when every return is in-memory by itself, or
        /// <see cref="NotPassThrough"/>.
        /// </summary>
        private int GetHelperSummary(IMethodSymbol method, HashSet<IMethodSymbol> helpersInProgress)
        {
            if (helperSummaries.TryGetValue(method, out var cached))
                return cached;

            var summary = ComputeHelperSummary(method, helpersInProgress);

            // A summary computed while another helper was on the stack may have been cut short by
            // the recursion guard; only cache top-level results.
            if (helpersInProgress.Count == 1)
                helperSummaries.TryAdd(method, summary);

            return summary;
        }

        private int ComputeHelperSummary(IMethodSymbol method, HashSet<IMethodSymbol> helpersInProgress)
        {
            if (method.IsVirtual || method.IsAbstract || method.IsOverride || method.IsAsync ||
                method.IsExtern || method.MethodKind != MethodKind.Ordinary)
                return NotPassThrough;

            var body = GetBodyOperation(method);
            if (body == null)
                return NotPassThrough;

            var returns = body.Descendants()
                .OfType<IReturnOperation>()
                .Where(r => r.Kind == OperationKind.Return && BelongsToBody(r, body))
                .ToList();

            if (returns.Count == 0 || returns.Any(r => r.ReturnedValue == null))
                return NotPassThrough;

            // The helper body starts its own walk at depth 0 so the cached summary does not depend on
            // how deep the caller's chain was; the in-progress sets guarantee termination.
            if (ReturnsProven(returns, null, helpersInProgress))
                return NoParameterNeeded;

            foreach (var parameter in method.Parameters)
            {
                if (!parameter.Type.IsIQueryable() || parameter.RefKind is RefKind.Ref or RefKind.Out)
                    continue;

                if (ReturnsProven(returns, parameter, helpersInProgress))
                    return parameter.Ordinal;
            }

            return NotPassThrough;
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
                    continue;

                if (!compilation.TryGetOwnedSemanticModel(declaration.SyntaxTree, out var model))
                    return null;

                return model.GetOperation(declaration);
            }

            return null;
        }

        /// <summary>
        /// Declared in this compilation. A helper from a referenced project has syntax references when the
        /// IDE loads that project as a compilation reference, but it is metadata in a command-line build, so
        /// both are treated as library helpers.
        /// </summary>
        private bool IsOwnedSourceMethod(IMethodSymbol method)
        {
            return method.OriginalDefinition.DeclaringSyntaxReferences.Any(r => compilation.ContainsSyntaxTree(r.SyntaxTree));
        }

        /// <summary>
        /// An interface-typed sequence (<c>IEnumerable&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, ...) could be a
        /// <c>DbSet&lt;T&gt;</c> at run time only when <c>T</c> is an entity. When no <c>DbContext</c> in this
        /// project or its references exposes a <c>DbSet&lt;T&gt;</c>, it is an in-memory sequence, such as
        /// VirtoCommerce's <c>AllRegisteredSettings.AsQueryable()</c>.
        /// </summary>
        private bool IsNonEntitySequence(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Interface } named || named.IsIQueryable())
                return false;

            var enumerable = named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                ? named
                : named.AllInterfaces.FirstOrDefault(i =>
                    i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
            if (enumerable == null)
                return false;

            var element = enumerable.TypeArguments[0];
            if (element.TypeKind is TypeKind.TypeParameter or TypeKind.Error || element.SpecialType == SpecialType.System_Object)
                return false;

            return !GetEntityTypes().Contains(element);
        }

        private HashSet<ITypeSymbol>? entityTypes;

        private HashSet<ITypeSymbol> GetEntityTypes()
        {
            var existing = entityTypes;
            if (existing != null)
                return existing;

            var found = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            CollectEntityTypes(compilation.Assembly.GlobalNamespace, found);
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                if (assembly.Name.StartsWith("Microsoft.", System.StringComparison.Ordinal) ||
                    assembly.Name.StartsWith("System.", System.StringComparison.Ordinal) ||
                    assembly.Name is "System" or "mscorlib" or "netstandard")
                    continue;

                CollectEntityTypes(assembly.GlobalNamespace, found);
            }

            entityTypes = found;
            return found;
        }

        private static void CollectEntityTypes(INamespaceSymbol ns, HashSet<ITypeSymbol> found)
        {
            foreach (var member in ns.GetMembers())
            {
                if (member is INamespaceSymbol child)
                {
                    CollectEntityTypes(child, found);
                }
                else if (member is INamedTypeSymbol type)
                {
                    CollectEntityTypes(type, found);
                }
            }
        }

        private static void CollectEntityTypes(INamedTypeSymbol type, HashSet<ITypeSymbol> found)
        {
            foreach (var nested in type.GetTypeMembers())
                CollectEntityTypes(nested, found);

            if (type.TypeKind != TypeKind.Class || !type.IsDbContext())
                return;

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.Type is INamedTypeSymbol { TypeArguments.Length: 1 } set && set.IsDbSet())
                    found.Add(set.TypeArguments[0]);
            }
        }

        private static bool IsQueryableAsQueryable(IMethodSymbol method)
        {
            var original = method.ReducedFrom ?? method;
            return original.Name == "AsQueryable" &&
                   original.ContainingType is { Name: "Queryable" } containingType &&
                   containingType.ContainingNamespace?.ToDisplayString() == "System.Linq";
        }
    }
}
