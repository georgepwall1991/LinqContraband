using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC052_NonDeterministicModelData;

/// <summary>
/// Reports <c>DateTime.Now</c>, <c>DateTime.UtcNow</c>, <c>DateTimeOffset.Now</c>, <c>Guid.NewGuid()</c> and similar
/// values passed to <c>HasData</c> or <c>HasDefaultValue</c>. They are evaluated once when the model is built, so every
/// build produces a different model: migrations churn, and since EF Core 9 <c>Migrate()</c> throws
/// <c>PendingModelChangesWarning</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonDeterministicModelDataAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC052";
    private const string Category = "Reliability";

    private const string HasDataAdvice = "Seed a fixed value instead";
    private const string HasDefaultValueAdvice = "Use HasDefaultValueSql with the database's current-time or new-id function instead";

    private static readonly LocalizableString Title = "Model data uses a value that changes on every run";

    private static readonly LocalizableString MessageFormat =
        "'{0}' in '{1}' is evaluated when the model is built, so every run produces a different model and EF Core 9+ Migrate() throws PendingModelChangesWarning. {2}.";

    private static readonly LocalizableString Description =
        "HasData seed values and HasDefaultValue values become part of the EF Core model and its migration snapshot. A value such as DateTime.Now or Guid.NewGuid() differs on every run, so the model never matches the last migration.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC052_NonDeterministicModelData.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name is not ("HasData" or "HasDefaultValue") || !IsEfCoreBuilderMethod(method))
            return;

        var advice = method.Name == "HasData" ? HasDataAdvice : HasDefaultValueAdvice;
        var skip = invocation.Instance == null && method.IsExtensionMethod ? 1 : 0;
        var reported = new HashSet<IOperation>();

        foreach (var argument in invocation.Arguments.Skip(skip))
        {
            foreach (var operation in argument.Value.DescendantsAndSelf())
            {
                if (!TryDescribeNonDeterministicValue(operation, out var description) || !reported.Add(operation))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    operation.Syntax.GetLocation(),
                    description,
                    method.Name,
                    advice));
            }
        }
    }

    private static bool IsEfCoreBuilderMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        return ns is "Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    /// <summary>
    /// Recognizes a non-deterministic value used directly, or read through locals or static readonly fields whose
    /// only value contains one (for example <c>var now = DateTime.UtcNow; var seed = new[] { new Blog { At = now } }</c>).
    /// </summary>
    private static bool TryDescribeNonDeterministicValue(IOperation operation, out string description)
    {
        if (TryDescribeDirectValue(operation, out description))
            return true;

        if (!TryFindThroughSymbols(operation, depth: 0, out var source))
            return false;

        description = operation.Syntax.ToString() + " (" + source + ")";
        return true;
    }

    private const int MaxSymbolHops = 3;

    private static bool TryFindThroughSymbols(IOperation operation, int depth, out string source)
    {
        source = string.Empty;
        if (depth >= MaxSymbolHops)
            return false;

        IOperation? initializer = operation switch
        {
            ILocalReferenceOperation localReference => GetSingleLocalValue(localReference),
            IFieldReferenceOperation fieldReference => GetStaticReadonlyFieldValue(fieldReference),
            _ => null
        };

        if (initializer == null)
            return false;

        foreach (var candidate in initializer.DescendantsAndSelf())
        {
            if (TryDescribeDirectValue(candidate, out source) ||
                TryFindThroughSymbols(candidate, depth + 1, out source))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDescribeDirectValue(IOperation operation, out string description)
    {
        description = string.Empty;
        switch (operation)
        {
            case IPropertyReferenceOperation { Instance: null, Property: var property }
                when property.Name is "Now" or "UtcNow" or "Today" &&
                     IsSystemType(property.ContainingType, "DateTime", "DateTimeOffset"):
                description = property.ContainingType.Name + "." + property.Name;
                return true;

            case IInvocationOperation { Instance: null, TargetMethod: var method }
                when method.Name is "NewGuid" or "CreateVersion7" && IsSystemType(method.ContainingType, "Guid"):
                description = "Guid." + method.Name + "()";
                return true;

            default:
                return false;
        }
    }

    private static bool IsSystemType(INamedTypeSymbol? type, params string[] names)
    {
        return type != null &&
               type.ContainingNamespace?.ToString() == "System" &&
               Array.IndexOf(names, type.Name) >= 0;
    }

    private static IOperation? GetSingleLocalValue(ILocalReferenceOperation localReference)
    {
        var local = localReference.Local;
        if (local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax { Initializer: { } initializer } ||
            localReference.SemanticModel == null)
        {
            return null;
        }

        var root = localReference as IOperation;
        while (root.Parent != null)
            root = root.Parent;

        // A local written again after its declaration may no longer hold the non-deterministic value.
        foreach (var operation in root.Descendants())
        {
            var target = operation switch
            {
                IAssignmentOperation assignment => assignment.Target,
                IIncrementOrDecrementOperation increment => increment.Target,
                IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
                _ => null
            };

            if (target is ILocalReferenceOperation written && SymbolEqualityComparer.Default.Equals(written.Local, local))
                return null;
        }

        return localReference.SemanticModel.GetOperation(initializer.Value);
    }

    private static IOperation? GetStaticReadonlyFieldValue(IFieldReferenceOperation fieldReference)
    {
        var field = fieldReference.Field;
        if (!field.IsStatic || !field.IsReadOnly || field.DeclaringSyntaxReferences.Length != 1 || fieldReference.SemanticModel == null)
            return null;

        if (field.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax { Initializer: { } initializer })
            return null;

        if (!fieldReference.SemanticModel.Compilation.TryGetOwnedSemanticModel(initializer.SyntaxTree, out var semanticModel))
            return null;

        return semanticModel.GetOperation(initializer.Value);
    }
}
