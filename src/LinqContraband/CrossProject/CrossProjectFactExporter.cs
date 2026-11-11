using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Analyzers.LC007_NPlusOneLooper;
using LinqContraband.Analyzers.LC010_SaveChangesInLoop;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.CrossProject;

/// <summary>
/// Writes what LC007 and LC010 know about each project into the compiler log of a <c>LinqContraband.Scan</c>
/// build, so the scanner can follow a loop's call from one project into another. A build sees a referenced project
/// as an assembly without method bodies, so the rules themselves stop at the project boundary: a controller that
/// loops over <c>ICustomerRepository.GetById</c> is quiet when the repository lives in the infrastructure project.
/// </summary>
/// <remarks>
/// This is not a rule. It reports nothing unless the global analyzer config sets
/// <see cref="CrossProjectFacts.OptionKey"/>, which only the scanner does, and its diagnostics are facts for the
/// scanner to join, never findings. The descriptor is not configurable so that a repository which raises every
/// analyzer diagnostic to an error cannot turn the facts into build errors.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossProjectFactExporter : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Fact = new(
        CrossProjectFacts.DiagnosticId,
        "LinqContraband.Scan cross-project fact",
        "{0}",
        "LinqContraband.Scan",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Written only during a LinqContraband.Scan build. The scanner joins these facts across projects to find N+1 queries and saves that cross a project boundary.",
        customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Fact);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var options = compilationContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (!options.TryGetValue(CrossProjectFacts.OptionKey, out var enabled) ||
                !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var compilation = compilationContext.Compilation;
            var queries = NPlusOneLooperHelperCache.For(compilation);
            var saves = SaveChangesInLoopAnalyzer.CreateSaveHelperCache(compilation);
            var isTestProject = IsTestProject(compilation);

            compilationContext.RegisterSymbolAction(
                symbolContext => ExportType(symbolContext, queries, saves, isTestProject),
                SymbolKind.NamedType);
            compilationContext.RegisterOperationAction(
                operationContext => ExportCall(operationContext, queries),
                OperationKind.Invocation);
            compilationContext.RegisterOperationAction(
                operationContext => ExportMethodGroup(operationContext, queries),
                OperationKind.MethodReference);
        });
    }

    private static void ExportType(
        SymbolAnalysisContext context,
        NPlusOneLooperHelperCache queries,
        NPlusOneLooperHelperCache saves,
        bool isTestProject)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsImplicitlyDeclared)
            return;

        var location = FirstSourceLocation(type);
        if (location == null)
            return;

        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol method)
                ExportMethod(context, method, queries, saves, isTestProject);
        }

        if (type.IsAbstract)
            return;

        // Which body each interface method lands on for this concrete type, wherever that body is declared.
        foreach (var @interface in type.AllInterfaces)
        {
            var definition = @interface.OriginalDefinition;
            if (!IsFollowedType(definition, queries.Compilation))
                continue;

            foreach (var interfaceMember in @interface.GetMembers())
            {
                if (interfaceMember is not IMethodSymbol { MethodKind: MethodKind.Ordinary } interfaceMethod)
                    continue;

                var rootId = NPlusOneLooperAnalysis.GetCrossProjectId(interfaceMethod);
                if (rootId == null)
                    continue;

                var implementationId = type.FindImplementationForInterfaceMember(interfaceMethod) is IMethodSymbol implementation
                    ? NPlusOneLooperAnalysis.GetCrossProjectId(implementation)
                    : null;
                Report(
                    context.ReportDiagnostic,
                    location,
                    CrossProjectFacts.Implements,
                    rootId,
                    implementationId ?? string.Empty,
                    isTestProject ? "1" : "0");
            }
        }
    }

    private static void ExportMethod(
        SymbolAnalysisContext context,
        IMethodSymbol method,
        NPlusOneLooperHelperCache queries,
        NPlusOneLooperHelperCache saves,
        bool isTestProject)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation) ||
            method.IsAbstract ||
            method.IsExtern ||
            method.IsImplicitlyDeclared)
        {
            return;
        }

        var location = FirstSourceLocation(method);
        var id = NPlusOneLooperAnalysis.GetCrossProjectId(method);
        if (location == null || id == null)
            return;

        if (method.IsOverride)
        {
            var root = NPlusOneLooperAnalysis.GetCrossProjectRoot(method);
            if (root.ContainingType is { } rootType &&
                IsFollowedType(rootType, queries.Compilation) &&
                root.GetDocumentationCommentId() is { Length: > 0 } rootId)
            {
                Report(context.ReportDiagnostic, location, CrossProjectFacts.Implements, rootId, id, isTestProject ? "1" : "0");
            }
        }

        // Only a method another project can reach: anything but a private one, or an interface implementation.
        if (method.DeclaredAccessibility == Accessibility.Private && method.ExplicitInterfaceImplementations.Length == 0)
            return;

        var query = NPlusOneLooperAnalysis.GetMethodExecution(method, queries, context.CancellationToken);
        if (query.Length > 0)
            Report(context.ReportDiagnostic, location, CrossProjectFacts.RunsQuery, id, query);

        var save = NPlusOneLooperAnalysis.GetMethodExecution(method, saves, context.CancellationToken);
        if (save.Length > 0)
            Report(context.ReportDiagnostic, location, CrossProjectFacts.Saves, id, save);

        if (query.Length > 0 && save.Length > 0)
            return;

        var calls = new List<CrossProjectCallee>();
        var memoized = NPlusOneLooperAnalysis.CollectCrossProjectCalls(method, queries, context.CancellationToken, calls);
        foreach (var call in calls)
        {
            Report(
                context.ReportDiagnostic,
                location,
                CrossProjectFacts.Calls,
                id,
                call.Kind.ToString(),
                call.Id,
                call.RootId,
                memoized ? "1" : "0");
        }
    }

    private static void ExportCall(OperationAnalysisContext context, NPlusOneLooperHelperCache queries)
    {
        var invocation = (IInvocationOperation)context.Operation;

        // Cheap gate first: this runs for every invocation in the compilation.
        if (invocation.FindEnclosingLoop() == null &&
            NPlusOneLooperAnalysis.FindPerElementOperator(invocation, out _) == null)
        {
            return;
        }

        if (!NPlusOneLooperAnalysis.TryGetCrossProjectCallee(invocation, queries, context.CancellationToken, out var callee))
            return;

        if (NPlusOneLooperAnalysis.TryGetPerIterationCallSite(
                invocation,
                queries,
                context.CancellationToken,
                out var queryLoopKind,
                out var queryOperator))
        {
            ReportCallSite(context, invocation.Syntax, "LC007", "7", callee, queryLoopKind, queryOperator);
        }

        if (SaveChangesInLoopAnalyzer.TryGetPerItemCallSite(
                invocation,
                context.CancellationToken,
                out var saveLoopKind,
                out var saveOperator))
        {
            ReportCallSite(context, invocation.Syntax, "LC010", "10", callee, saveLoopKind, saveOperator);
        }
    }

    private static void ExportMethodGroup(OperationAnalysisContext context, NPlusOneLooperHelperCache queries)
    {
        var methodReference = (IMethodReferenceOperation)context.Operation;
        var perElementOperator = NPlusOneLooperAnalysis.FindPerElementOperatorTakingDelegate(methodReference);
        if (perElementOperator == null)
            return;

        if (!NPlusOneLooperAnalysis.TryGetCrossProjectCallee(
                methodReference.Method,
                methodReference.IsVirtual,
                methodReference.Instance?.Type,
                queries,
                context.CancellationToken,
                out var callee))
        {
            return;
        }

        ReportCallSite(
            context,
            methodReference.Syntax,
            "LC007",
            "7",
            callee,
            NPlusOneLooperDiagnosticProperties.LambdaLoopKind,
            perElementOperator.TargetMethod.Name);
    }

    private static void ReportCallSite(
        OperationAnalysisContext context,
        SyntaxNode syntax,
        string ruleId,
        string rule,
        CrossProjectCallee callee,
        string loopKind,
        string? operatorName)
    {
        if (!CrossProjectSuppression.TryGetSeverity(
                context.Compilation,
                syntax,
                context.ContainingSymbol,
                ruleId,
                context.CancellationToken,
                out var severity))
        {
            return;
        }

        Report(
            context.ReportDiagnostic,
            syntax.GetLocation(),
            CrossProjectFacts.PerItemCall,
            rule,
            callee.Kind.ToString(),
            callee.Id,
            callee.RootId,
            loopKind,
            operatorName ?? string.Empty,
            callee.Name,
            severity.ToString());
    }

    private static void Report(Action<Diagnostic> report, Location location, string kind, params string[] fields)
    {
        var separator = CrossProjectFacts.Separator.ToString();
        var message = CrossProjectFacts.Prefix + separator + kind + separator + string.Join(separator, fields);
        report(Diagnostic.Create(Fact, location, message));
    }

    private static Location? FirstSourceLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
                return location;
        }

        return null;
    }

    /// <summary>An interface or base type declared in this project or in one that looks like another project.</summary>
    private static bool IsFollowedType(INamedTypeSymbol type, Compilation compilation)
    {
        return SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly) ||
               NPlusOneLooperAnalysis.IsProjectLikeAssembly(type.ContainingAssembly, compilation);
    }

    /// <summary>
    /// A project that references a test framework. Its fakes and stubs implement the same interfaces as the real
    /// repositories, and counting them would make every interface call look like it has an in-memory implementation.
    /// </summary>
    private static bool IsTestProject(Compilation compilation)
    {
        foreach (var reference in compilation.ReferencedAssemblyNames)
        {
            var name = reference.Name;
            if (name.StartsWith("xunit.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("nunit.framework", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.VisualStudio.TestPlatform.TestFramework", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("MSTest.TestFramework", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("TUnit.Core", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
