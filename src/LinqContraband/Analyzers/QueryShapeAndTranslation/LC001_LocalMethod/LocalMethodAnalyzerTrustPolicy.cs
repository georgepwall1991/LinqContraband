using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

public sealed partial class LocalMethodAnalyzer
{
    private const string TrustedAttributesOption = "dotnet_code_quality.LC001.trusted_attributes";
    private const string TrustedNamespacesOption = "dotnet_code_quality.LC001.trusted_namespaces";

    // Attributes that tell EF Core, or a query-expansion library, how to translate the method.
    private static readonly ImmutableHashSet<string> TranslationMarkerAttributes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Microsoft.EntityFrameworkCore.DbFunctionAttribute",
        "EntityFrameworkCore.Projectables.ProjectableAttribute",
        "LinqKit.ExpandableAttribute",
        "NeinLinq.InjectLambdaAttribute",
        "DelegateDecompiler.ComputedAttribute");

    private static bool IsTrustedTranslatableMethod(IMethodSymbol method)
    {
        // System and Microsoft (Linq, EF Core base) are generally translatable.
        if (method.IsFrameworkMethod()) return true;
        if (HasExplicitTranslationMarker(method, TranslationMarkerAttributes)) return true;

        var ns = method.ContainingNamespace?.ToString();
        if (ns == null) return false;

        // Specific database provider functions that are often used in IQueryable.
        if (ns.StartsWith("Npgsql", System.StringComparison.Ordinal) ||
            ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) ||
            ns.StartsWith("NetTopologySuite", System.StringComparison.Ordinal) ||
            IsInNamespace(ns, "Pgvector.EntityFrameworkCore"))
        {
            return true;
        }

        return false;
    }

    // Checked only when LC001 is about to report: attributes the project trusts through
    // .editorconfig, and methods mapped with modelBuilder.HasDbFunction(...).
    private static bool IsConfiguredTranslatableMethod(
        IMethodSymbol method,
        OperationAnalysisContext context,
        MappedDbFunctions mappedDbFunctions)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Operation.Syntax.SyntaxTree);
        if (options.TryGetValue(TrustedAttributesOption, out var value))
        {
            var trusted = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var name in AnalyzerConfigListOption.Split(value))
            {
                trusted.Add(name);
                if (!name.EndsWith("Attribute", StringComparison.Ordinal))
                    trusted.Add(name + "Attribute");
            }

            if (trusted.Count > 0 && HasExplicitTranslationMarker(method, trusted.ToImmutable()))
                return true;
        }

        // Namespaces whose methods a provider plugin or a project's own IMethodCallTranslator translates.
        if (options.TryGetValue(TrustedNamespacesOption, out var namespaces) &&
            method.ContainingNamespace?.ToString() is { } methodNamespace)
        {
            foreach (var trustedNamespace in AnalyzerConfigListOption.Split(namespaces))
            {
                if (IsInNamespace(methodNamespace, trustedNamespace))
                    return true;
            }
        }

        foreach (var candidate in EnumerateMethodVariants(method))
        {
            if (mappedDbFunctions.Contains(candidate))
                return true;
        }

        return false;
    }

    // "A.B" and "A.B.C" are in "A.B"; "A.BC" is not.
    private static bool IsInNamespace(string ns, string root) =>
        ns.StartsWith(root, StringComparison.Ordinal) &&
        (ns.Length == root.Length || ns[root.Length] == '.');

    private static bool HasExplicitTranslationMarker(IMethodSymbol method, ImmutableHashSet<string> attributeNames)
    {
        foreach (var candidate in EnumerateMethodVariants(method))
        {
            foreach (var attribute in candidate.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass != null && attributeNames.Contains(attributeClass.ToDisplayString()))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> EnumerateMethodVariants(IMethodSymbol method)
    {
        var pending = new Stack<IMethodSymbol>();
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        pending.Push(method);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
                continue;

            yield return current;

            if (current.ReducedFrom != null)
                pending.Push(current.ReducedFrom);

            if (!SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, current))
                pending.Push(current.OriginalDefinition);

            if (current.OverriddenMethod != null)
                pending.Push(current.OverriddenMethod);
        }
    }

    /// <summary>
    /// Methods this compilation maps with <c>modelBuilder.HasDbFunction(...)</c>, found on first use.
    /// Recognised arguments are <c>typeof(T).GetMethod(nameof(T.M), ...)</c> or a string name, which
    /// trusts every overload of that name on <c>T</c>, and <c>() =&gt; T.M(...)</c>. The <c>GetMethod</c>
    /// call can also sit in the initializer of a local or a readonly field that is never assigned again.
    /// </summary>
    private sealed class MappedDbFunctions
    {
        private readonly Lazy<ImmutableHashSet<IMethodSymbol>> methods;

        public MappedDbFunctions(Compilation compilation, CancellationToken cancellationToken)
        {
            methods = new Lazy<ImmutableHashSet<IMethodSymbol>>(
                () => Collect(compilation, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public bool Contains(IMethodSymbol method) => methods.Value.Contains(method.OriginalDefinition);

        private static ImmutableHashSet<IMethodSymbol> Collect(Compilation compilation, CancellationToken cancellationToken)
        {
            var result = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tree.GetText(cancellationToken).ToString().IndexOf("HasDbFunction", StringComparison.Ordinal) < 0 ||
                    !compilation.TryGetOwnedSemanticModel(tree, out var semanticModel))
                {
                    continue;
                }

                foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
                {
                    if (node is not InvocationExpressionSyntax syntax ||
                        semanticModel.GetOperation(syntax, cancellationToken) is not IInvocationOperation invocation ||
                        invocation.TargetMethod.Name != "HasDbFunction" ||
                        invocation.TargetMethod.ContainingType?.Name != "ModelBuilder" ||
                        invocation.TargetMethod.ContainingType.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
                    {
                        continue;
                    }

                    foreach (var argument in invocation.Arguments)
                    {
                        if (argument.Parameter?.Ordinal == 0)
                            AddMappedMethods(argument.Value, semanticModel, compilation, result, cancellationToken);
                    }
                }
            }

            return result.ToImmutable();
        }

        private static void AddMappedMethods(
            IOperation value,
            SemanticModel semanticModel,
            Compilation compilation,
            ImmutableHashSet<IMethodSymbol>.Builder result,
            CancellationToken cancellationToken)
        {
            value = value.UnwrapConversions();

            // var m = typeof(T).GetMethod(...); modelBuilder.HasDbFunction(m);
            if (value is ILocalReferenceOperation localReference)
            {
                if (GetSingleAssignmentInitializer(localReference.Local, semanticModel, compilation, cancellationToken) is { } localInitializer)
                    AddGetMethodTargets(localInitializer, result);
                return;
            }

            // static readonly MethodInfo M = typeof(T).GetMethod(...); modelBuilder.HasDbFunction(M);
            if (value is IFieldReferenceOperation fieldReference)
            {
                if (GetSingleAssignmentInitializer(fieldReference.Field, semanticModel, compilation, cancellationToken) is { } fieldInitializer)
                    AddGetMethodTargets(fieldInitializer, result);
                return;
            }

            if (AddGetMethodTargets(value, result))
                return;

            // () => T.M(...)
            foreach (var descendant in value.DescendantsAndSelf())
            {
                if (descendant is IAnonymousFunctionOperation lambda)
                {
                    foreach (var statement in lambda.Body.Operations)
                    {
                        if (statement is IReturnOperation { ReturnedValue: { } returned } &&
                            returned.UnwrapConversions() is IInvocationOperation mapped)
                        {
                            result.Add(mapped.TargetMethod.OriginalDefinition);
                        }
                    }

                    return;
                }
            }
        }

        // typeof(T).GetMethod(nameof(T.M), ...) or typeof(T).GetMethod("M", ...); false when value is not such a call.
        private static bool AddGetMethodTargets(IOperation value, ImmutableHashSet<IMethodSymbol>.Builder result)
        {
            value = value.UnwrapConversions();
            if (value is not IInvocationOperation { TargetMethod.Name: "GetMethod" or "GetRuntimeMethod" } getMethod ||
                getMethod.TargetMethod.ContainingType?.ToDisplayString() is not ("System.Type" or "System.Reflection.RuntimeReflectionExtensions"))
            {
                return false;
            }

            var typeOperand = getMethod.Instance?.UnwrapConversions() is ITypeOfOperation instanceTypeOf
                ? instanceTypeOf.TypeOperand
                : getMethod.Arguments.Length > 0 && getMethod.Arguments[0].Value.UnwrapConversions() is ITypeOfOperation argumentTypeOf
                    ? argumentTypeOf.TypeOperand
                    : null;
            string? name = null;
            foreach (var argument in getMethod.Arguments)
            {
                if (argument.Value.Type?.SpecialType == SpecialType.System_String &&
                    argument.Value.ConstantValue is { HasValue: true, Value: string constantName })
                {
                    name = constantName;
                    break;
                }
            }

            if (typeOperand is INamedTypeSymbol type && name != null)
            {
                foreach (var member in type.GetMembers(name))
                {
                    if (member is IMethodSymbol method)
                        result.Add(method.OriginalDefinition);
                }
            }

            return true;
        }

        /// <summary>
        /// The initializer of a local, or of a readonly field declared in this compilation, when nothing
        /// assigns it again: no assignment, deconstruction, <c>ref</c>/<c>out</c> argument or <c>ref</c> alias.
        /// A local is checked across its syntax tree, a readonly field across its type's declarations
        /// (the only places that can assign it).
        /// </summary>
        private static IOperation? GetSingleAssignmentInitializer(
            ISymbol symbol,
            SemanticModel semanticModel,
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            symbol = symbol.OriginalDefinition;
            IEnumerable<SyntaxReference> scopes;
            switch (symbol)
            {
                case ILocalSymbol { IsRef: false, IsConst: false } local:
                    scopes = local.DeclaringSyntaxReferences.Length == 1
                        ? new[] { local.DeclaringSyntaxReferences[0].SyntaxTree.GetRoot(cancellationToken).GetReference() }
                        : Array.Empty<SyntaxReference>();
                    break;
                case IFieldSymbol { IsReadOnly: true, IsConst: false } field when field.ContainingType != null:
                    scopes = field.ContainingType.DeclaringSyntaxReferences;
                    break;
                default:
                    return null;
            }

            if (symbol.DeclaringSyntaxReferences.Length != 1 ||
                symbol.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax { Initializer.Value: { } initializerSyntax } ||
                !TryGetModel(initializerSyntax.SyntaxTree, semanticModel, compilation, out var initializerModel) ||
                initializerModel.GetOperation(initializerSyntax, cancellationToken) is not { } initializer)
            {
                return null;
            }

            var any = false;
            foreach (var scope in scopes)
            {
                any = true;
                var scopeNode = scope.GetSyntax(cancellationToken);
                if (!TryGetModel(scopeNode.SyntaxTree, semanticModel, compilation, out var scopeModel))
                    return null;

                foreach (var identifier in scopeNode.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (identifier.Identifier.ValueText == symbol.Name &&
                        IsWriteTarget(identifier) &&
                        SymbolEqualityComparer.Default.Equals(
                            scopeModel.GetSymbolInfo(identifier, cancellationToken).Symbol?.OriginalDefinition, symbol))
                    {
                        return null;
                    }
                }
            }

            return any ? initializer : null;
        }

        private static bool TryGetModel(SyntaxTree tree, SemanticModel current, Compilation compilation, out SemanticModel model)
        {
            if (tree == current.SyntaxTree)
            {
                model = current;
                return true;
            }

            return compilation.TryGetOwnedSemanticModel(tree, out model);
        }

        private static bool IsWriteTarget(IdentifierNameSyntax identifier)
        {
            ExpressionSyntax expression = identifier;
            while (true)
            {
                switch (expression.Parent)
                {
                    case MemberAccessExpressionSyntax memberAccess when memberAccess.Name == expression:
                    case ParenthesizedExpressionSyntax:
                    case PostfixUnaryExpressionSyntax { RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression }:
                        expression = (ExpressionSyntax)expression.Parent;
                        continue;
                }

                break;
            }

            switch (expression.Parent)
            {
                case AssignmentExpressionSyntax assignment:
                    return assignment.Left == expression;
                case RefExpressionSyntax:
                    return true;
                case PrefixUnaryExpressionSyntax { RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddressOfExpression }:
                    return true;
                case ArgumentSyntax argument:
                    if (!argument.RefKindKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None) &&
                        !argument.RefKindKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InKeyword))
                    {
                        return true;
                    }

                    // (m, _) = ...; possibly nested in other tuples.
                    SyntaxNode node = argument;
                    while (node.Parent is TupleExpressionSyntax tuple)
                    {
                        if (tuple.Parent is AssignmentExpressionSyntax tupleAssignment && tupleAssignment.Left == tuple)
                            return true;
                        if (tuple.Parent is not ArgumentSyntax outer)
                            break;
                        node = outer;
                    }

                    return false;
                default:
                    return false;
            }
        }
    }
}
