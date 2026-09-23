using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    /// trusts every overload of that name on <c>T</c>, and <c>() =&gt; T.M(...)</c>.
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
                            AddMappedMethods(argument.Value, result);
                    }
                }
            }

            return result.ToImmutable();
        }

        private static void AddMappedMethods(IOperation value, ImmutableHashSet<IMethodSymbol>.Builder result)
        {
            value = value.UnwrapConversions();

            // typeof(T).GetMethod(nameof(T.M), ...) or typeof(T).GetMethod("M", ...)
            if (value is IInvocationOperation { TargetMethod.Name: "GetMethod" or "GetRuntimeMethod" } getMethod &&
                getMethod.TargetMethod.ContainingType?.ToDisplayString() is "System.Type" or "System.Reflection.RuntimeReflectionExtensions")
            {
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

                return;
            }

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
    }
}
