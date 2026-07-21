using System.IO;
using Xunit;

namespace LinqContraband.Tests.Architecture;

public sealed class AnalyzerModularizationTests
{
    private readonly string _repoRoot = RepositoryLayout.GetRepositoryRoot();

    [Fact]
    public void LC046_FlowAndClassification_LiveInDedicatedPartials()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC046_ConcurrentDbContextOperations");
        var analyzerPath = Path.Combine(analyzerDir, "ConcurrentDbContextOperationsAnalyzer.cs");
        var flowPath = Path.Combine(analyzerDir, "ConcurrentDbContextOperationsFlowAnalysis.cs");
        var classificationPath = Path.Combine(analyzerDir, "ConcurrentDbContextOperationsClassification.cs");

        Assert.True(File.Exists(flowPath), "LC046 overlap flow should live in a focused partial file.");
        Assert.True(File.Exists(classificationPath), "LC046 EF operation classification should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class ConcurrentDbContextOperationsAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static void AnalyzeDirectOverlaps", analyzerSource);
        Assert.DoesNotContain("private static bool TryClassifyEfAsyncOperation", analyzerSource);

        Assert.Contains("private static void AnalyzeDirectOverlaps", File.ReadAllText(flowPath));
        Assert.Contains("private static bool TryClassifyEfAsyncOperation", File.ReadAllText(classificationPath));
    }

    [Fact]
    public void LC044_RootScanEntries_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var rootScanPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyRootScan.cs");
        var entriesPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyRootScanEntries.cs");

        Assert.True(File.Exists(entriesPath), "LC044 root-scan entry DTOs should live in a focused partial file.");

        var rootScanSource = File.ReadAllText(rootScanPath);
        Assert.DoesNotContain("internal readonly struct MutationEntry", rootScanSource);
        Assert.DoesNotContain("internal readonly struct ReattachEntry", rootScanSource);
        Assert.DoesNotContain("internal readonly struct DetachEntry", rootScanSource);
        Assert.DoesNotContain("internal readonly struct TrackerClearEntry", rootScanSource);
        Assert.DoesNotContain("internal readonly struct SaveChangesEntry", rootScanSource);

        var entriesSource = File.ReadAllText(entriesPath);
        Assert.Contains("internal readonly struct MutationEntry", entriesSource);
        Assert.Contains("internal readonly struct ReattachEntry", entriesSource);
        Assert.Contains("internal readonly struct DetachEntry", entriesSource);
        Assert.Contains("internal readonly struct TrackerClearEntry", entriesSource);
        Assert.Contains("internal readonly struct SaveChangesEntry", entriesSource);
    }

    [Fact]
    public void LC044_RootScanOperationRecording_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var rootScanPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyRootScan.cs");
        var operationRecordingPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyRootScanOperationRecording.cs");

        Assert.True(File.Exists(operationRecordingPath), "LC044 root-scan operation recording should live in a focused partial file.");

        var rootScanSource = File.ReadAllText(rootScanPath);
        Assert.DoesNotContain("private static void HandleAssignment", rootScanSource);
        Assert.DoesNotContain("private static void HandlePropertyMutation", rootScanSource);
        Assert.DoesNotContain("private static void HandleInvocation", rootScanSource);

        var operationRecordingSource = File.ReadAllText(operationRecordingPath);
        Assert.Contains("private static void HandleAssignment", operationRecordingSource);
        Assert.Contains("private static void HandlePropertyMutation", operationRecordingSource);
        Assert.Contains("private static void HandleInvocation", operationRecordingSource);
        Assert.Contains("TryParseEntryStateAssignment", operationRecordingSource);
        Assert.Contains("TryParseReattachInvocation", operationRecordingSource);
        Assert.Contains("TryParseTrackerClear", operationRecordingSource);
    }

    [Fact]
    public void LC044_OptionalControlFlow_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var reachabilityPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyReachability.cs");
        var optionalFlowPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyOptionalFlow.cs");

        Assert.True(File.Exists(optionalFlowPath), "LC044 optional branch/loop reachability rules should live in a focused partial file.");

        var reachabilitySource = File.ReadAllText(reachabilityPath);
        Assert.DoesNotContain("private static bool IsNestedUnderOptionalControlFlow", reachabilitySource);
        Assert.DoesNotContain("private static bool IfStatementMakesBranchMandatory", reachabilitySource);
        Assert.DoesNotContain("private static bool StatementSkipsLater", reachabilitySource);
        Assert.DoesNotContain("private static bool BranchSkipsLater", reachabilitySource);

        var optionalFlowSource = File.ReadAllText(optionalFlowPath);
        Assert.Contains("private static bool IsNestedUnderOptionalControlFlow", optionalFlowSource);
        Assert.Contains("private static bool IfStatementMakesBranchMandatory", optionalFlowSource);
        Assert.Contains("private static bool StatementSkipsLater", optionalFlowSource);
        Assert.Contains("private static bool BranchSkipsLater", optionalFlowSource);
    }

    [Fact]
    public void LC044_QuerySourceResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var analyzerPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyAnalyzer.cs");
        var querySourcePath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyQuerySource.cs");

        Assert.True(File.Exists(querySourcePath), "LC044 AsNoTracking query/materialization source resolution should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AsNoTrackingThenModifyAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsAsNoTrackingMaterialization", analyzerSource);
        Assert.DoesNotContain("private static bool ChainContainsAsNoTracking", analyzerSource);
        Assert.DoesNotContain("private static bool TryGetQueryContextSymbol", analyzerSource);

        var querySource = File.ReadAllText(querySourcePath);
        Assert.Contains("private static bool IsAsNoTrackingMaterialization", querySource);
        Assert.Contains("private static bool ChainContainsAsNoTracking", querySource);
        Assert.Contains("private static bool TryGetQueryContextSymbol", querySource);
    }

    [Fact]
    public void LC008_AsyncContextResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC008_SyncBlocker");
        var analyzerPath = Path.Combine(analyzerDir, "SyncBlockerAnalyzer.cs");
        var asyncContextPath = Path.Combine(analyzerDir, "SyncBlockerAsyncContext.cs");

        Assert.True(File.Exists(asyncContextPath), "LC008 async-context boundary checks should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class SyncBlockerAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsInsideAsyncMethod", analyzerSource);

        var asyncContextSource = File.ReadAllText(asyncContextPath);
        Assert.Contains("private static bool IsInsideAsyncMethod", asyncContextSource);
        Assert.Contains("ILocalFunctionOperation", asyncContextSource);
        Assert.Contains("IAnonymousFunctionOperation", asyncContextSource);
    }

    [Fact]
    public void LC004_MethodSummaryConsumption_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC004_IQueryableLeak");
        var summaryPath = Path.Combine(analyzerDir, "IQueryableLeakMethodSummaryAnalysis.cs");
        var consumptionPath = Path.Combine(analyzerDir, "IQueryableLeakMethodSummaryConsumption.cs");

        Assert.True(File.Exists(consumptionPath), "LC004 method-summary local consumption checks should live in a focused partial file.");

        var summarySource = File.ReadAllText(summaryPath);
        Assert.DoesNotContain("private void MarkMaterializingConstructorHazards", summarySource);
        Assert.DoesNotContain("private bool IsMaterializingCollectionConstructor", summarySource);
        Assert.DoesNotContain("private bool IsDirectConsumption", summarySource);
        Assert.DoesNotContain("private bool IsGetEnumeratorInvocation", summarySource);
        Assert.DoesNotContain("private void MarkHazardIfParameterSource", summarySource);

        var consumptionSource = File.ReadAllText(consumptionPath);
        Assert.Contains("private void MarkMaterializingConstructorHazards", consumptionSource);
        Assert.Contains("private bool IsMaterializingCollectionConstructor", consumptionSource);
        Assert.Contains("private bool IsDirectConsumption", consumptionSource);
        Assert.Contains("private bool IsGetEnumeratorInvocation", consumptionSource);
        Assert.Contains("private void MarkHazardIfParameterSource", consumptionSource);
    }

    [Fact]
    public void LC004_InvocationInputMapping_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC004_IQueryableLeak");
        var sourceResolutionPath = Path.Combine(analyzerDir, "IQueryableLeakSourceResolution.cs");
        var invocationInputsPath = Path.Combine(analyzerDir, "IQueryableLeakInvocationInputs.cs");

        Assert.True(File.Exists(invocationInputsPath), "LC004 invocation input mapping should live in a focused partial file.");

        var sourceResolutionSource = File.ReadAllText(sourceResolutionPath);
        Assert.DoesNotContain("private IEnumerable<InvocationInput> EnumerateInvocationInputs", sourceResolutionSource);

        var invocationInputsSource = File.ReadAllText(invocationInputsPath);
        Assert.Contains("private IEnumerable<InvocationInput> EnumerateInvocationInputs", invocationInputsSource);
    }

    [Fact]
    public void LC004_ExecutableRootTraversal_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC004_IQueryableLeak");
        var symbolHelpersPath = Path.Combine(analyzerDir, "IQueryableLeakSymbolHelpers.cs");
        var executableRootsPath = Path.Combine(analyzerDir, "IQueryableLeakExecutableRoots.cs");

        Assert.True(File.Exists(executableRootsPath), "LC004 executable-root traversal should live in a focused partial file.");

        var symbolHelpersSource = File.ReadAllText(symbolHelpersPath);
        Assert.DoesNotContain("private bool TryGetExecutableRoot", symbolHelpersSource);
        Assert.DoesNotContain("private IEnumerable<IOperation> EnumerateOperations", symbolHelpersSource);
        Assert.DoesNotContain("private static bool IsInsideNestedExecutable", symbolHelpersSource);

        var executableRootsSource = File.ReadAllText(executableRootsPath);
        Assert.Contains("private bool TryGetExecutableRoot", executableRootsSource);
        Assert.Contains("private IEnumerable<IOperation> EnumerateOperations", executableRootsSource);
        Assert.Contains("private static bool IsInsideNestedExecutable", executableRootsSource);
    }

    [Fact]
    public void LC004_HazardousLookupTables_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC004_IQueryableLeak");
        var compilationStatePath = Path.Combine(analyzerDir, "IQueryableLeakCompilationState.cs");
        var lookupTablesPath = Path.Combine(analyzerDir, "IQueryableLeakHazardousLookupTables.cs");

        Assert.True(File.Exists(lookupTablesPath), "LC004 hazardous method/type lookup tables should live in a focused partial file.");

        var compilationStateSource = File.ReadAllText(compilationStatePath);
        Assert.DoesNotContain("private static readonly ImmutableHashSet<string> HazardousEnumerableMethods", compilationStateSource);
        Assert.DoesNotContain("private static readonly ImmutableHashSet<string> MaterializingCollectionTypes", compilationStateSource);

        var lookupTablesSource = File.ReadAllText(lookupTablesPath);
        Assert.Contains("private static readonly ImmutableHashSet<string> HazardousEnumerableMethods", lookupTablesSource);
        Assert.Contains("private static readonly ImmutableHashSet<string> MaterializingCollectionTypes", lookupTablesSource);
    }

    [Fact]
    public void LC001_StaticQueryableOrderingRewrite_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC001_LocalMethod");
        var staticRewritePath = Path.Combine(analyzerDir, "LocalMethodFixerStaticQueryableRewrite.cs");
        var orderingRewritePath = Path.Combine(analyzerDir, "LocalMethodFixerStaticQueryableOrdering.cs");

        Assert.True(File.Exists(orderingRewritePath), "LC001 static Queryable ordering-source rewrite should live in a focused partial file.");

        var staticRewriteSource = File.ReadAllText(staticRewritePath);
        Assert.DoesNotContain("private static bool RewriteQueryableExtensionOrderingSourceChain", staticRewriteSource);
        Assert.DoesNotContain("private static bool IsRewritableOrderedSource", staticRewriteSource);
        Assert.DoesNotContain("private static bool IsQueryableOrderingInvocation", staticRewriteSource);

        var orderingRewriteSource = File.ReadAllText(orderingRewritePath);
        Assert.Contains("private static bool RewriteQueryableExtensionOrderingSourceChain", orderingRewriteSource);
        Assert.Contains("private static bool IsRewritableOrderedSource", orderingRewriteSource);
        Assert.Contains("private static bool IsQueryableOrderingInvocation", orderingRewriteSource);
    }

    [Fact]
    public void LC044_DiagnosticReporting_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var analyzerPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyAnalyzer.cs");
        var reportingPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyReporting.cs");

        Assert.True(File.Exists(reportingPath), "LC044 diagnostic reporting should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static void TryReportForLocal", analyzerSource);
        Assert.DoesNotContain("private static void TryReportForForeach", analyzerSource);
        Assert.DoesNotContain("private readonly struct MutationHit", analyzerSource);
        Assert.DoesNotContain("private static MutationHit? FindFirstPropertyMutation", analyzerSource);

        var reportingSource = File.ReadAllText(reportingPath);
        Assert.Contains("private static void TryReportForLocal", reportingSource);
        Assert.Contains("private static void TryReportForForeach", reportingSource);
        Assert.Contains("private readonly struct MutationHit", reportingSource);
        Assert.Contains("private static MutationHit? FindFirstPropertyMutation", reportingSource);
    }

    [Fact]
    public void LC017_FixerPropertyAccessSafety_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC017_WholeEntityProjection");
        var contextPath = Path.Combine(analyzerDir, "WholeEntityProjectionFixerContextAnalysis.cs");
        var propertyAccessPath = Path.Combine(analyzerDir, "WholeEntityProjectionFixerPropertyAccess.cs");
        var trackedExpressionsPath = Path.Combine(analyzerDir, "WholeEntityProjectionFixerTrackedEntityExpressions.cs");

        Assert.True(File.Exists(propertyAccessPath), "LC017 fixer property-access safety checks should live in a focused partial file.");
        Assert.True(File.Exists(trackedExpressionsPath), "LC017 fixer tracked-entity expression recognition should live in a focused partial file.");

        var contextSource = File.ReadAllText(contextPath);
        Assert.DoesNotContain("private static bool HasUnsupportedEntityPropertyAccess", contextSource);
        Assert.DoesNotContain("private static bool IsTrackedEntityConversionExpression", contextSource);
        Assert.DoesNotContain("private static bool IsTrackedEntityExpression", contextSource);
        Assert.DoesNotContain("private static bool HasUnsafeIndexedEntityAccess", contextSource);
        Assert.DoesNotContain("private static bool IsPropertyOfType", contextSource);

        var propertyAccessSource = File.ReadAllText(propertyAccessPath);
        Assert.Contains("private static bool HasUnsupportedEntityPropertyAccess", propertyAccessSource);
        Assert.DoesNotContain("private static bool IsTrackedEntityConversionExpression", propertyAccessSource);
        Assert.DoesNotContain("private static bool IsTrackedEntityExpression", propertyAccessSource);
        Assert.Contains("private static bool HasUnsafeIndexedEntityAccess", propertyAccessSource);
        Assert.Contains("private static bool IsPropertyOfType", propertyAccessSource);

        var trackedExpressionsSource = File.ReadAllText(trackedExpressionsPath);
        Assert.Contains("private static bool IsTrackedEntityConversionExpression", trackedExpressionsSource);
        Assert.Contains("private static bool IsTrackedEntityExpression", trackedExpressionsSource);
    }

    [Fact]
    public void LC017_UsageReferenceClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC017_WholeEntityProjection");
        var usageAnalysisPath = Path.Combine(analyzerDir, "WholeEntityProjectionUsageAnalysis.cs");
        var usageReferencesPath = Path.Combine(analyzerDir, "WholeEntityProjectionUsageReferences.cs");

        Assert.True(File.Exists(usageReferencesPath), "LC017 usage reference classification should live in a focused partial file.");

        var usageAnalysisSource = File.ReadAllText(usageAnalysisPath);
        Assert.DoesNotContain("private static bool IsTrackedEntityReference", usageAnalysisSource);
        Assert.DoesNotContain("private static bool IsDirectVariableEscape", usageAnalysisSource);
        Assert.DoesNotContain("private static bool LambdaDirectlyReferences", usageAnalysisSource);

        var usageReferencesSource = File.ReadAllText(usageReferencesPath);
        Assert.Contains("private static bool IsTrackedEntityReference", usageReferencesSource);
        Assert.Contains("private static bool IsDirectVariableEscape", usageReferencesSource);
        Assert.Contains("private static bool LambdaDirectlyReferences", usageReferencesSource);
    }

    [Fact]
    public void LC017_SyntaxPropertyCollection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC017_WholeEntityProjection");
        var syntaxAnalysisPath = Path.Combine(analyzerDir, "WholeEntityProjectionSyntaxAnalysis.cs");
        var propertyCollectionPath = Path.Combine(analyzerDir, "WholeEntityProjectionSyntaxPropertyCollection.cs");

        Assert.True(File.Exists(propertyCollectionPath), "LC017 syntax-based property collection should live in a focused partial file.");

        var syntaxAnalysisSource = File.ReadAllText(syntaxAnalysisPath);
        Assert.DoesNotContain("private static void CollectSyntaxBasedPropertyAccesses", syntaxAnalysisSource);

        var propertyCollectionSource = File.ReadAllText(propertyCollectionPath);
        Assert.Contains("private static void CollectSyntaxBasedPropertyAccesses", propertyCollectionSource);
        Assert.Contains("ConditionalAccessExpressionSyntax", propertyCollectionSource);
        Assert.Contains("MemberAccessExpressionSyntax", propertyCollectionSource);
    }

    [Fact]
    public void LC025_FixerSourceResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC025_AsNoTrackingWithUpdate");
        var fixerPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateFixer.cs");
        var sourceResolutionPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateFixerSourceResolution.cs");
        var noTrackingSourcePath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateFixerNoTrackingSource.cs");

        Assert.True(File.Exists(sourceResolutionPath), "LC025 fixer AsNoTracking source resolution should live in a focused partial file.");
        Assert.True(File.Exists(noTrackingSourcePath), "LC025 fixer recursive no-tracking source tracing should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class AsNoTrackingWithUpdateFixer", fixerSource);
        Assert.DoesNotContain("private static InvocationExpressionSyntax? FindAsNoTrackingOrigin", fixerSource);
        Assert.DoesNotContain("private readonly struct AsNoTrackingOrigin", fixerSource);
        Assert.DoesNotContain("private static bool IsNoTrackingSource", fixerSource);
        Assert.DoesNotContain("private static bool IsLocalFromNoTracking", fixerSource);
        Assert.DoesNotContain("private static bool HasAsNoTrackingInChain", fixerSource);

        var sourceResolutionSource = File.ReadAllText(sourceResolutionPath);
        Assert.Contains("private static InvocationExpressionSyntax? FindAsNoTrackingOrigin", sourceResolutionSource);
        Assert.Contains("private readonly struct AsNoTrackingOrigin", sourceResolutionSource);
        Assert.DoesNotContain("private static bool IsNoTrackingSource", sourceResolutionSource);
        Assert.DoesNotContain("private static bool IsLocalFromNoTracking", sourceResolutionSource);

        var noTrackingSource = File.ReadAllText(noTrackingSourcePath);
        Assert.Contains("private static bool IsNoTrackingSource", noTrackingSource);
        Assert.Contains("private static bool IsLocalFromNoTracking", noTrackingSource);
    }

    [Fact]
    public void LC025_FixerTrackingDirectiveAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC025_AsNoTrackingWithUpdate");
        var sourceResolutionPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateFixerSourceResolution.cs");
        var trackingDirectivePath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateFixerTrackingDirectiveAnalysis.cs");

        Assert.True(File.Exists(trackingDirectivePath), "LC025 fixer tracking-directive chain analysis should live in a focused partial file.");

        var sourceResolutionSource = File.ReadAllText(sourceResolutionPath);
        Assert.DoesNotContain("private static bool HasAsNoTrackingInChain", sourceResolutionSource);
        Assert.DoesNotContain("private static bool IsEfCoreNoTrackingDirective", sourceResolutionSource);
        Assert.DoesNotContain("private static bool IsEfCoreAsTracking", sourceResolutionSource);
        Assert.DoesNotContain("private static InvocationExpressionSyntax? FindAsNoTrackingInvocation", sourceResolutionSource);

        var trackingDirectiveSource = File.ReadAllText(trackingDirectivePath);
        Assert.Contains("private static bool HasAsNoTrackingInChain", trackingDirectiveSource);
        Assert.Contains("private static bool IsEfCoreNoTrackingDirective", trackingDirectiveSource);
        Assert.Contains("private static bool IsEfCoreAsTracking", trackingDirectiveSource);
        Assert.Contains("private static InvocationExpressionSyntax? FindAsNoTrackingInvocation", trackingDirectiveSource);
    }

    [Fact]
    public void IncludePathParser_HelperResponsibilities_LiveInDedicatedPartials()
    {
        var extensionsDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Extensions");
        var parserPath = Path.Combine(extensionsDir, "IncludePathParser.cs");
        var lambdaPath = Path.Combine(extensionsDir, "IncludePathParserLambdaPaths.cs");
        var typeAnalysisPath = Path.Combine(extensionsDir, "IncludePathParserTypeAnalysis.cs");

        Assert.True(File.Exists(lambdaPath), "IncludePathParser lambda navigation parsing should live in a focused partial file.");
        Assert.True(File.Exists(typeAnalysisPath), "IncludePathParser type/collection analysis should live in a focused partial file.");

        var parserSource = File.ReadAllText(parserPath);
        Assert.Contains("internal static partial class IncludePathParser", parserSource);
        Assert.DoesNotContain("private static bool TryAddNavigationSegments", parserSource);
        Assert.DoesNotContain("public static bool TryGetCollectionElementType", parserSource);

        var lambdaSource = File.ReadAllText(lambdaPath);
        Assert.Contains("private static bool TryAddNavigationSegments", lambdaSource);
        Assert.Contains("private static CSharpSyntaxNode UnwrapExpression", lambdaSource);

        var typeAnalysisSource = File.ReadAllText(typeAnalysisPath);
        Assert.Contains("public static bool TryGetCollectionElementType", typeAnalysisSource);
        Assert.Contains("private static bool TryGetNamedGenericElementType", typeAnalysisSource);
    }

    [Fact]
    public void LC045_UsageScan_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var usageAnalysisPath = Path.Combine(analyzerDir, "MissingIncludeUsageAnalysis.cs");
        var usageScanPath = Path.Combine(analyzerDir, "MissingIncludeUsageScan.cs");

        Assert.True(File.Exists(usageScanPath), "LC045 executable-root usage scanning should live in a focused partial file.");

        var usageAnalysisSource = File.ReadAllText(usageAnalysisPath);
        Assert.DoesNotContain("private static List<NavigationAccess>? CollectNavigationAccessesFromExecutableRoot", usageAnalysisSource);

        var usageScanSource = File.ReadAllText(usageScanPath);
        Assert.Contains("private static List<NavigationAccess>? CollectNavigationAccessesFromExecutableRoot", usageScanSource);
        Assert.Contains("LambdaReferencesTrackedLocal", usageScanSource);
        Assert.Contains("satisfiedPaths", usageScanSource);
    }

    [Fact]
    public void LC045_OriginAwareFlowResponsibilities_LiveInDedicatedPartials()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var analyzerPath = Path.Combine(analyzerDir, "MissingIncludeAnalyzer.cs");
        var usageScanPath = Path.Combine(analyzerDir, "MissingIncludeUsageScan.cs");
        var flowAnalysisPath = Path.Combine(analyzerDir, "MissingIncludeOriginFlowAnalysis.cs");
        var flowBindingsPath = Path.Combine(analyzerDir, "MissingIncludeOriginFlowBindings.cs");
        var flowContextPath = Path.Combine(analyzerDir, "MissingIncludeOriginFlowContext.cs");
        var flowEventsPath = Path.Combine(analyzerDir, "MissingIncludeOriginFlowEvents.cs");
        var flowStatePath = Path.Combine(analyzerDir, "MissingIncludeOriginFlowState.cs");
        var autoIncludePath = Path.Combine(analyzerDir, "MissingIncludeAutoIncludeConfiguration.cs");

        Assert.True(File.Exists(flowAnalysisPath), "LC045 origin-aware control-flow analysis should live in a focused partial file.");
        Assert.True(File.Exists(flowBindingsPath), "LC045 origin and alias binding discovery should live in a focused partial file.");
        Assert.True(File.Exists(flowContextPath), "LC045 origin-flow context construction should live in a focused partial file.");
        Assert.True(File.Exists(flowEventsPath), "LC045 origin-flow event collection should live in a focused partial file.");
        Assert.True(File.Exists(flowStatePath), "LC045 origin-flow state and event models should live in a focused partial file.");
        Assert.True(File.Exists(autoIncludePath), "LC045 model-level AutoInclude proof should live in a focused partial file.");

        var usageScanSource = File.ReadAllText(usageScanPath);
        Assert.DoesNotContain("private static bool TryGetFlowGraph", usageScanSource);
        Assert.DoesNotContain("private sealed partial class OriginFlowContext", usageScanSource);
        Assert.DoesNotContain("private readonly struct FlowProbeState", usageScanSource);
        Assert.DoesNotContain("private enum FlowEventKind", usageScanSource);
        Assert.DoesNotContain("private void DiscoverStableAliases", usageScanSource);

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("RegisterCompilationStartAction", analyzerSource);
        Assert.Contains("var flowGraphCache = new System.Runtime.CompilerServices.ConditionalWeakTable<", analyzerSource);
        Assert.Contains("var autoIncludeCache = new System.Collections.Concurrent.ConcurrentDictionary<", analyzerSource);
        Assert.Contains("AnalyzeInvocation(", analyzerSource);
        Assert.Contains("autoIncludeCache,", analyzerSource);

        var autoIncludeSource = File.ReadAllText(autoIncludePath);
        Assert.Contains("private static void AddModelAutoIncludePrefixes", autoIncludeSource);
        Assert.Contains("private static bool TryGetDirectAutoInclude", autoIncludeSource);

        var flowAnalysisSource = File.ReadAllText(flowAnalysisPath);
        Assert.Contains("private static bool TryCollectOriginAwareNavigationAccesses", flowAnalysisSource);
        Assert.Contains("private static bool TryGetFlowGraph", flowAnalysisSource);
        Assert.Contains("ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache", flowAnalysisSource);
        Assert.DoesNotContain("static readonly ConditionalWeakTable<IOperation", flowAnalysisSource);

        var flowBindingsSource = File.ReadAllText(flowBindingsPath);
        Assert.Contains("private void DiscoverStableAliases", flowBindingsSource);
        Assert.Contains("private sealed class IterationBinding", flowBindingsSource);

        var flowContextSource = File.ReadAllText(flowContextPath);
        Assert.Contains("private sealed partial class OriginFlowContext", flowContextSource);
        Assert.Contains("public bool TryMapEventsToBlocks", flowContextSource);
        Assert.Contains("private bool TryResolveEntityOrigin", flowContextSource);

        var flowEventsSource = File.ReadAllText(flowEventsPath);
        Assert.Contains("private void CollectBindingAndEscapeEvents", flowEventsSource);
        Assert.Contains("private void CollectNavigationEvent", flowEventsSource);

        var flowStateSource = File.ReadAllText(flowStatePath);
        Assert.Contains("private sealed class EntityOrigin", flowStateSource);
        Assert.Contains("private enum FlowEventKind", flowStateSource);
        Assert.Contains("private readonly struct FlowProbeState", flowStateSource);
    }

    [Fact]
    public void LC045_DiagnosticReporting_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var analyzerPath = Path.Combine(analyzerDir, "MissingIncludeAnalyzer.cs");
        var reportingPath = Path.Combine(analyzerDir, "MissingIncludeReporting.cs");

        Assert.True(File.Exists(reportingPath), "LC045 missing-path diagnostic reporting should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static void ReportMissingIncludeDiagnostics", analyzerSource);
        Assert.DoesNotContain("private static bool HasLongerMissingPath", analyzerSource);

        var reportingSource = File.ReadAllText(reportingPath);
        Assert.Contains("private static void ReportMissingIncludeDiagnostics", reportingSource);
        Assert.Contains("private static bool HasLongerMissingPath", reportingSource);
        Assert.Contains("ReportDiagnostic", reportingSource);
    }

    [Fact]
    public void LC011_AssemblyLocalScope_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var localResolutionPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyLocalResolution.cs");
        var localScopePath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyLocalScope.cs");

        Assert.True(File.Exists(localScopePath), "LC011 local Assembly scope/shadowing checks should live in a focused partial file.");

        var localResolutionSource = File.ReadAllText(localResolutionPath);
        Assert.DoesNotContain("private static bool HasLocalAssemblyValueInScope", localResolutionSource);
        Assert.DoesNotContain("private static bool DeclaresAssemblyValue", localResolutionSource);

        var localScopeSource = File.ReadAllText(localScopePath);
        Assert.Contains("private static bool HasLocalAssemblyValueInScope", localScopeSource);
        Assert.Contains("private static bool DeclaresAssemblyValue", localScopeSource);
        Assert.Contains("ParameterListSyntax", localScopeSource);
    }

    [Fact]
    public void LC011_KeyPropertyRules_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var keyRulesPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyKeyRules.cs");
        var keyPropertyRulesPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyKeyPropertyRules.cs");

        Assert.True(File.Exists(keyPropertyRulesPath), "LC011 convention/key-property validation should live in a focused partial file.");

        var keyRulesSource = File.ReadAllText(keyRulesPath);
        Assert.DoesNotContain("private bool HasValidKeyProperty", keyRulesSource);
        Assert.DoesNotContain("private static bool TryFindProperty", keyRulesSource);
        Assert.DoesNotContain("private static bool IsUsableKeyProperty", keyRulesSource);
        Assert.DoesNotContain("private static bool IsValidKeyType", keyRulesSource);

        var keyPropertyRulesSource = File.ReadAllText(keyPropertyRulesPath);
        Assert.Contains("private bool HasValidKeyProperty", keyPropertyRulesSource);
        Assert.Contains("private static bool TryFindProperty", keyPropertyRulesSource);
        Assert.Contains("private static bool IsUsableKeyProperty", keyPropertyRulesSource);
        Assert.Contains("private static bool IsValidKeyType", keyPropertyRulesSource);
    }

    [Fact]
    public void LC015_LocalValueCache_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC015_MissingOrderBy");
        var analyzerPath = Path.Combine(analyzerDir, "MissingOrderByAnalyzer.cs");
        var localValueCachePath = Path.Combine(analyzerDir, "MissingOrderByLocalValueCache.cs");
        var localReferenceKeyPath = Path.Combine(analyzerDir, "MissingOrderByLocalReferenceKey.cs");

        Assert.True(File.Exists(localValueCachePath), "LC015 local assignment resolution cache should live in a focused partial file.");
        Assert.True(File.Exists(localReferenceKeyPath), "LC015 local-reference cycle keys should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private sealed class LocalValueCache", analyzerSource);
        Assert.DoesNotContain("private readonly struct LocalWrite", analyzerSource);
        Assert.DoesNotContain("private readonly struct LocalReferenceKey", analyzerSource);

        var localValueCacheSource = File.ReadAllText(localValueCachePath);
        Assert.Contains("private sealed class LocalValueCache", localValueCacheSource);
        Assert.Contains("private readonly struct LocalWrite", localValueCacheSource);
        Assert.DoesNotContain("private readonly struct LocalReferenceKey", localValueCacheSource);

        var localReferenceKeySource = File.ReadAllText(localReferenceKeyPath);
        Assert.Contains("private readonly struct LocalReferenceKey", localReferenceKeySource);
        Assert.Contains("private sealed class LocalReferenceKeyComparer", localReferenceKeySource);
    }

    [Fact]
    public void LC015_QuerySourceResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC015_MissingOrderBy");
        var analyzerPath = Path.Combine(analyzerDir, "MissingOrderByAnalyzer.cs");
        var querySourcePath = Path.Combine(analyzerDir, "MissingOrderByQuerySource.cs");

        Assert.True(File.Exists(querySourcePath), "LC015 EF query source resolution should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool HasEntityFrameworkQuerySource", analyzerSource);
        Assert.DoesNotContain("private static bool TryResolveLocalValue", analyzerSource);

        var querySource = File.ReadAllText(querySourcePath);
        Assert.Contains("private static bool HasEntityFrameworkQuerySource", querySource);
        Assert.Contains("private static bool TryResolveLocalValue", querySource);
    }

    [Fact]
    public void LC015_DownstreamLocalAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC015_MissingOrderBy");
        var downstreamPath = Path.Combine(analyzerDir, "MissingOrderByDownstreamAnalysis.cs");
        var localAnalysisPath = Path.Combine(analyzerDir, "MissingOrderByDownstreamLocalAnalysis.cs");

        Assert.True(File.Exists(localAnalysisPath), "LC015 downstream sorting through assigned locals should live in a focused partial file.");

        var downstreamSource = File.ReadAllText(downstreamPath);
        Assert.DoesNotContain("private bool HasSortingDownstreamThroughLocal", downstreamSource);
        Assert.DoesNotContain("private static bool TryGetAssignedLocal", downstreamSource);

        var localAnalysisSource = File.ReadAllText(localAnalysisPath);
        Assert.Contains("private bool HasSortingDownstreamThroughLocal", localAnalysisSource);
        Assert.Contains("private static bool TryGetAssignedLocal", localAnalysisSource);
    }

    [Fact]
    public void LC015_DownstreamValueFlow_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC015_MissingOrderBy");
        var downstreamPath = Path.Combine(analyzerDir, "MissingOrderByDownstreamAnalysis.cs");
        var valueFlowPath = Path.Combine(analyzerDir, "MissingOrderByDownstreamValueFlow.cs");

        Assert.True(File.Exists(valueFlowPath), "LC015 downstream local/value-flow tracing should live in a focused partial file.");

        var downstreamSource = File.ReadAllText(downstreamPath);
        Assert.Contains("private bool HasPaginationAfterDownstreamSort", downstreamSource);
        Assert.DoesNotContain("private bool ReceivesFromOperation", downstreamSource);

        var valueFlowSource = File.ReadAllText(valueFlowPath);
        Assert.Contains("private bool ReceivesFromOperation", valueFlowSource);
        Assert.Contains("TryResolveLocalValue", valueFlowSource);
    }

    [Fact]
    public void LC015_FixerKeyShapeChecks_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC015_MissingOrderBy");
        var fixerPath = Path.Combine(analyzerDir, "MissingOrderByFixer.cs");
        var keyShapePath = Path.Combine(analyzerDir, "MissingOrderByFixerKeyShape.cs");

        Assert.True(File.Exists(keyShapePath), "LC015 fixer composite/keyless key-shape checks should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class MissingOrderByFixer", fixerSource);
        Assert.DoesNotContain("private static bool HasCompositeKeyAttribute", fixerSource);
        Assert.DoesNotContain("private static bool HasKeylessAttribute", fixerSource);
        Assert.DoesNotContain("private static int CountPrimaryKeyParts", fixerSource);

        var keyShapeSource = File.ReadAllText(keyShapePath);
        Assert.Contains("private static bool HasCompositeKeyAttribute", keyShapeSource);
        Assert.Contains("private static bool HasKeylessAttribute", keyShapeSource);
        Assert.Contains("private static int CountPrimaryKeyParts", keyShapeSource);
    }

    [Fact]
    public void LC023_PrimaryKeyCache_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC023_FindInsteadOfFirstOrDefault");
        var keyAnalysisPath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultKeyAnalysis.cs");
        var primaryKeyCachePath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultPrimaryKeyCache.cs");

        Assert.True(File.Exists(primaryKeyCachePath), "LC023 primary-key cache state and scanning should live in a focused partial file.");

        var keyAnalysisSource = File.ReadAllText(keyAnalysisPath);
        Assert.Contains("public static PrimaryKeyCache CreateCache", keyAnalysisSource);
        Assert.DoesNotContain("internal sealed partial class PrimaryKeyCache", keyAnalysisSource);

        var primaryKeyCacheSource = File.ReadAllText(primaryKeyCachePath);
        Assert.Contains("internal sealed partial class PrimaryKeyCache", primaryKeyCacheSource);
        Assert.Contains("private void EnsureFullyScanned", primaryKeyCacheSource);
    }

    [Fact]
    public void LC027_ConfigurationSyntaxProcessing_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC027_MissingExplicitForeignKey");
        var configurationAnalysisPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyConfigurationAnalysis.cs");
        var syntaxProcessingPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyConfigurationSyntaxProcessing.cs");

        Assert.True(File.Exists(syntaxProcessingPath), "LC027 per-syntax relationship configuration processing should live in a focused partial file.");

        var configurationAnalysisSource = File.ReadAllText(configurationAnalysisPath);
        Assert.Contains("private static ConfigurationScan BuildConfigurationScan", configurationAnalysisSource);
        Assert.DoesNotContain("private static void ProcessConfigurationSyntax", configurationAnalysisSource);

        var syntaxProcessingSource = File.ReadAllText(syntaxProcessingPath);
        Assert.Contains("private static void ProcessConfigurationSyntax", syntaxProcessingSource);
        Assert.Contains("private static string GetNavigationConfigurationKey", syntaxProcessingSource);
    }

    [Fact]
    public void LC045_QueryRootResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var queryAnalysisPath = Path.Combine(analyzerDir, "MissingIncludeQueryAnalysis.cs");
        var queryRootPath = Path.Combine(analyzerDir, "MissingIncludeQueryRoot.cs");

        Assert.True(File.Exists(queryRootPath), "LC045 DbSet query-root proof should live in a focused partial file.");

        var queryAnalysisSource = File.ReadAllText(queryAnalysisPath);
        Assert.Contains("private static bool TryAnalyzeQueryChain", queryAnalysisSource);
        Assert.DoesNotContain("private static bool TryGetDbSetRoot", queryAnalysisSource);

        var queryRootSource = File.ReadAllText(queryRootPath);
        Assert.Contains("private static bool TryGetDbSetRoot", queryRootSource);
        Assert.Contains("IsDbContext", queryRootSource);
    }

    [Fact]
    public void LC033_FixerCollectionInitializerRewrite_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC033_UseFrozenSetForStaticMembershipCaches");
        var rewritePath = Path.Combine(analyzerDir, "UseFrozenSetForStaticMembershipCachesFixerRewrite.cs");
        var collectionRewritePath = Path.Combine(analyzerDir, "UseFrozenSetForStaticMembershipCachesFixerCollectionRewrite.cs");

        Assert.True(File.Exists(collectionRewritePath), "LC033 collection-initializer rewrite rules should live in a focused partial file.");

        var rewriteSource = File.ReadAllText(rewritePath);
        Assert.DoesNotContain("private static bool TryRewriteCollectionInitializer", rewriteSource);

        var collectionRewriteSource = File.ReadAllText(collectionRewritePath);
        Assert.Contains("private static bool TryRewriteCollectionInitializer", collectionRewriteSource);
        Assert.Contains("SyntaxKind.ArrayInitializerExpression", collectionRewriteSource);
    }

    [Fact]
    public void LC001_StaticQueryableArgumentSelection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC001_LocalMethod");
        var rewritePath = Path.Combine(analyzerDir, "LocalMethodFixerStaticQueryableRewrite.cs");
        var argumentsPath = Path.Combine(analyzerDir, "LocalMethodFixerStaticQueryableArguments.cs");

        Assert.True(File.Exists(argumentsPath), "LC001 static Queryable source-argument selection should live in a focused partial file.");

        var rewriteSource = File.ReadAllText(rewritePath);
        Assert.DoesNotContain("private static bool TryGetInputSequenceArgument", rewriteSource);
        Assert.DoesNotContain("private static bool TryGetNamedSequenceArgument", rewriteSource);

        var argumentsSource = File.ReadAllText(argumentsPath);
        Assert.Contains("private static bool TryGetInputSequenceArgument", argumentsSource);
        Assert.Contains("private static bool TryGetNamedSequenceArgument", argumentsSource);
        Assert.Contains("IArgumentOperation", argumentsSource);
    }

    [Fact]
    public void LC041_PropertyConsumptionAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC041_SingleEntityScalarProjection");
        var usagePath = Path.Combine(analyzerDir, "SingleEntityScalarProjectionUsageAnalysis.cs");
        var propertyUsagePath = Path.Combine(analyzerDir, "SingleEntityScalarProjectionPropertyUsage.cs");

        Assert.True(File.Exists(propertyUsagePath), "LC041 local property-consumption rules should live in a focused partial file.");

        var usageSource = File.ReadAllText(usagePath);
        Assert.DoesNotContain("private static bool TryGetConsumedProperty", usageSource);
        Assert.DoesNotContain("private static bool TryGetConditionalAccessProperty", usageSource);
        Assert.DoesNotContain("private static bool IsReadOnlyPropertyReference", usageSource);

        var propertyUsageSource = File.ReadAllText(propertyUsagePath);
        Assert.Contains("private static bool TryGetConsumedProperty", propertyUsageSource);
        Assert.Contains("private static bool TryGetConditionalAccessProperty", propertyUsageSource);
        Assert.Contains("private static bool IsReadOnlyPropertyReference", propertyUsageSource);
    }

    [Fact]
    public void LC027_RelationshipLocalShadowing_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC027_MissingExplicitForeignKey");
        var localScopePath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyRelationshipLocalScope.cs");
        var shadowingPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyRelationshipLocalShadowing.cs");

        Assert.True(File.Exists(shadowingPath), "LC027 nested local shadowing checks should live in a focused partial file.");

        var localScopeSource = File.ReadAllText(localScopePath);
        Assert.DoesNotContain("private static bool IsShadowedByNestedLocal", localScopeSource);
        Assert.DoesNotContain("private static SyntaxNode? FindDesignationScope", localScopeSource);
        Assert.DoesNotContain("private static SyntaxNode? FindParameterScope", localScopeSource);

        var shadowingSource = File.ReadAllText(shadowingPath);
        Assert.Contains("private static bool IsShadowedByNestedLocal", shadowingSource);
        Assert.Contains("private static SyntaxNode? FindDesignationScope", shadowingSource);
        Assert.Contains("private static SyntaxNode? FindParameterScope", shadowingSource);
    }

    [Fact]
    public void LC002_ProviderSafeExpressionRules_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC002_PrematureMaterialization");
        var continuationSafetyPath = Path.Combine(analyzerDir, "PrematureMaterializationContinuationSafety.cs");
        var providerSafeExpressionsPath = Path.Combine(analyzerDir, "PrematureMaterializationProviderSafeExpressions.cs");

        Assert.True(File.Exists(providerSafeExpressionsPath), "LC002 provider-safe expression recursion should live in a focused partial file.");

        var continuationSafetySource = File.ReadAllText(continuationSafetyPath);
        Assert.DoesNotContain("private static bool IsProviderSafeExpression", continuationSafetySource);
        Assert.DoesNotContain("private static bool IsProviderSafeObjectCreation", continuationSafetySource);
        Assert.DoesNotContain("private static bool IsProviderSafeInvocation", continuationSafetySource);

        var providerSafeExpressionsSource = File.ReadAllText(providerSafeExpressionsPath);
        Assert.Contains("private static bool IsProviderSafeExpression", providerSafeExpressionsSource);
        Assert.Contains("private static bool IsProviderSafeObjectCreation", providerSafeExpressionsSource);
        Assert.Contains("private static bool IsProviderSafeInvocation", providerSafeExpressionsSource);
    }

    [Fact]
    public void LC002_RedundantDiagnosticPolicy_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC002_PrematureMaterialization");
        var originAnalysisPath = Path.Combine(analyzerDir, "PrematureMaterializationOriginAnalysis.cs");
        var redundantDiagnosticPath = Path.Combine(analyzerDir, "PrematureMaterializationRedundantDiagnostic.cs");

        Assert.True(File.Exists(redundantDiagnosticPath), "LC002 redundant-materialization diagnostic policy should live in a focused partial file.");

        var originAnalysisSource = File.ReadAllText(originAnalysisPath);
        Assert.DoesNotContain("private static bool TryCreateRedundantDiagnostic", originAnalysisSource);

        var redundantDiagnosticSource = File.ReadAllText(redundantDiagnosticPath);
        Assert.Contains("private static bool TryCreateRedundantDiagnostic", redundantDiagnosticSource);
        Assert.Contains("RemoveRedundantMaterializationFixKind", redundantDiagnosticSource);
    }

    [Fact]
    public void LC002_DiagnosticDescriptors_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC002_PrematureMaterialization");
        var analyzerPath = Path.Combine(analyzerDir, "PrematureMaterializationAnalyzer.cs");
        var diagnosticsPath = Path.Combine(analyzerDir, "PrematureMaterializationDiagnostics.cs");

        Assert.True(File.Exists(diagnosticsPath), "LC002 diagnostic descriptors and diagnostic-property metadata should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("private static void AnalyzeInvocation", analyzerSource);
        Assert.DoesNotContain("private static readonly LocalizableString Title", analyzerSource);
        Assert.DoesNotContain("public static readonly DiagnosticDescriptor Rule", analyzerSource);
        Assert.DoesNotContain("public static readonly DiagnosticDescriptor RedundantRule", analyzerSource);
        Assert.DoesNotContain("private static ImmutableDictionary<string, string?> CreateProperties", analyzerSource);

        var diagnosticsSource = File.ReadAllText(diagnosticsPath);
        Assert.Contains("private static readonly LocalizableString Title", diagnosticsSource);
        Assert.Contains("public static readonly DiagnosticDescriptor Rule", diagnosticsSource);
        Assert.Contains("public static readonly DiagnosticDescriptor RedundantRule", diagnosticsSource);
        Assert.Contains("private static ImmutableDictionary<string, string?> CreateProperties", diagnosticsSource);
        Assert.DoesNotContain("private static void AnalyzeInvocation", diagnosticsSource);
    }

    [Fact]
    public void LC005_LocalInitializerFlow_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC005_MultipleOrderBy");
        var analyzerPath = Path.Combine(analyzerDir, "MultipleOrderByAnalyzer.cs");
        var localFlowPath = Path.Combine(analyzerDir, "MultipleOrderByLocalInitializerFlow.cs");

        Assert.True(File.Exists(localFlowPath), "LC005 local initializer and write tracking should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class MultipleOrderByAnalyzer", analyzerSource);
        Assert.DoesNotContain("private bool TryGetSingleAssignmentLocalInitializer", analyzerSource);
        Assert.DoesNotContain("private static bool HasWriteBeforeUse", analyzerSource);
        Assert.DoesNotContain("private static bool IsWriteToLocal", analyzerSource);

        var localFlowSource = File.ReadAllText(localFlowPath);
        Assert.Contains("private bool TryGetSingleAssignmentLocalInitializer", localFlowSource);
        Assert.Contains("private static bool HasWriteBeforeUse", localFlowSource);
        Assert.Contains("private static bool IsWriteToLocal", localFlowSource);
    }

    [Fact]
    public void LC006_IncludeChainResultModel_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC006_CartesianExplosion");
        var analyzerPath = Path.Combine(analyzerDir, "CartesianExplosionAnalyzer.cs");
        var chainResultPath = Path.Combine(analyzerDir, "CartesianExplosionIncludeChainResult.cs");

        Assert.True(File.Exists(chainResultPath), "LC006 include-chain result model should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private enum QuerySplittingMode", analyzerSource);
        Assert.DoesNotContain("private sealed class IncludeChainAnalysis", analyzerSource);

        var chainResultSource = File.ReadAllText(chainResultPath);
        Assert.Contains("private enum QuerySplittingMode", chainResultSource);
        Assert.Contains("private sealed class IncludeChainAnalysis", chainResultSource);
        Assert.Contains("public bool TryGetRiskySiblingCollections", chainResultSource);
    }

    [Fact]
    public void LC024_GroupAccessTranslation_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC024_GroupByNonTranslatable");
        var analyzerPath = Path.Combine(analyzerDir, "GroupByNonTranslatableAnalyzer.cs");
        var groupAccessPath = Path.Combine(analyzerDir, "GroupByNonTranslatableGroupAccess.cs");

        Assert.True(File.Exists(groupAccessPath), "LC024 group-chain translatability rules should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class GroupByNonTranslatableAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsTranslatableGroupAccess", analyzerSource);
        Assert.DoesNotContain("private static IInvocationOperation FindOutermostGroupChainInvocation", analyzerSource);

        var groupAccessSource = File.ReadAllText(groupAccessPath);
        Assert.Contains("private static bool IsTranslatableGroupAccess", groupAccessSource);
        Assert.Contains("private static IInvocationOperation FindOutermostGroupChainInvocation", groupAccessSource);
    }

    [Fact]
    public void LC024_GroupingTypeAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC024_GroupByNonTranslatable");
        var analyzerPath = Path.Combine(analyzerDir, "GroupByNonTranslatableAnalyzer.cs");
        var groupingTypesPath = Path.Combine(analyzerDir, "GroupByNonTranslatableGroupingTypes.cs");

        Assert.True(File.Exists(groupingTypesPath), "LC024 grouping IQueryable/IGrouping type checks should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool IsGroupingQueryable", analyzerSource);
        Assert.DoesNotContain("private static ITypeSymbol? GetQueryableElementType", analyzerSource);
        Assert.DoesNotContain("private static bool IsGrouping(ITypeSymbol type)", analyzerSource);

        var groupingTypesSource = File.ReadAllText(groupingTypesPath);
        Assert.Contains("private static bool IsGroupingQueryable", groupingTypesSource);
        Assert.Contains("private static ITypeSymbol? GetQueryableElementType", groupingTypesSource);
        Assert.Contains("private static bool IsGrouping(ITypeSymbol type)", groupingTypesSource);
    }

    [Fact]
    public void LC037_StringBuilderAliasIdentity_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var detectionPath = Path.Combine(analyzerDir, "RawSqlStringConstructionDetection.cs");
        var aliasPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderAliases.cs");
        var identityPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderIdentity.cs");

        Assert.True(File.Exists(aliasPath), "LC037 StringBuilder alias matching should live in a focused partial file.");
        Assert.True(File.Exists(identityPath), "LC037 StringBuilder alias identity resolution should live in a focused partial file.");

        var detectionSource = File.ReadAllText(detectionPath);
        Assert.DoesNotContain("private readonly struct LocalIdentity", detectionSource);

        var aliasSource = File.ReadAllText(aliasPath);
        Assert.DoesNotContain("private readonly struct LocalIdentity", aliasSource);
        Assert.DoesNotContain("private static LocalIdentity ResolveLocalIdentity", aliasSource);

        var identitySource = File.ReadAllText(identityPath);
        Assert.Contains("private readonly struct LocalIdentity", identitySource);
        Assert.Contains("private static LocalIdentity ResolveLocalIdentity", identitySource);
    }

    [Fact]
    public void LC037_StringBuilderAliasGuaranteedWrites_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var aliasPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderAliases.cs");
        var guaranteedWritesPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderAliasWrites.cs");

        Assert.True(File.Exists(guaranteedWritesPath), "LC037 StringBuilder alias guaranteed-write resolution should live in a focused partial file.");

        var aliasSource = File.ReadAllText(aliasPath);
        Assert.DoesNotContain("private static bool HasNonGuaranteedWriteAfterLatestGuaranteed", aliasSource);
        Assert.DoesNotContain("private static bool TryResolveGuaranteedLocalValue", aliasSource);

        var guaranteedWritesSource = File.ReadAllText(guaranteedWritesPath);
        Assert.Contains("private static bool HasNonGuaranteedWriteAfterLatestGuaranteed", guaranteedWritesSource);
        Assert.Contains("private static bool TryResolveGuaranteedLocalValue", guaranteedWritesSource);
    }

    [Fact]
    public void LC037_StringBuilderAppendFlow_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var detectionPath = Path.Combine(analyzerDir, "RawSqlStringConstructionDetection.cs");
        var flowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderFlow.cs");

        Assert.True(File.Exists(flowPath), "LC037 StringBuilder append/reset flow should live in a focused partial file.");

        var detectionSource = File.ReadAllText(detectionPath);
        Assert.DoesNotContain("private static bool IsStringBuilderAppendArgumentNonConstant", detectionSource);

        var flowSource = File.ReadAllText(flowPath);
        Assert.Contains("private static bool IsStringBuilderAppendArgumentNonConstant", flowSource);
    }

    [Fact]
    public void LC037_StringBuilderLocalWriteFlow_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var flowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderFlow.cs");
        var localWritesPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderLocalWrites.cs");
        var latestWritesPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderLatestLocalWrite.cs");

        Assert.True(File.Exists(localWritesPath), "LC037 StringBuilder local-write flow should live in a focused partial file.");
        Assert.True(File.Exists(latestWritesPath), "LC037 StringBuilder latest local-write flow should live in a focused partial file.");

        var flowSource = File.ReadAllText(flowPath);
        Assert.DoesNotContain("private static bool HasOnlyConstantLocalWritesBeforeReference", flowSource);
        Assert.DoesNotContain("private static bool HasLatestNonConstantLocalWriteBeforeReference", flowSource);
        Assert.DoesNotContain("private static IOperation GetCompoundAssignmentRightValue", flowSource);

        var localWritesSource = File.ReadAllText(localWritesPath);
        Assert.Contains("private static bool HasOnlyConstantLocalWritesBeforeReference", localWritesSource);
        Assert.DoesNotContain("private static bool HasLatestNonConstantLocalWriteBeforeReference", localWritesSource);

        var latestWritesSource = File.ReadAllText(latestWritesPath);
        Assert.Contains("private static bool HasLatestNonConstantLocalWriteBeforeReference", latestWritesSource);
        Assert.Contains("private static IOperation GetCompoundAssignmentRightValue", latestWritesSource);
    }

    [Fact]
    public void LC037_StringBuilderLoopFlow_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var flowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderFlow.cs");
        var loopFlowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderLoopFlow.cs");

        Assert.True(File.Exists(loopFlowPath), "LC037 StringBuilder loop-carried local write flow should live in a focused partial file.");

        var flowSource = File.ReadAllText(flowPath);
        Assert.DoesNotContain("private static bool HasNonConstantLoopCarriedLocalWrite", flowSource);
        Assert.DoesNotContain("private static bool HasGuaranteedConstantLocalWriteInSameIterationBeforeReference", flowSource);

        var loopFlowSource = File.ReadAllText(loopFlowPath);
        Assert.Contains("private static bool HasNonConstantLoopCarriedLocalWrite", loopFlowSource);
        Assert.Contains("private static bool HasGuaranteedConstantLocalWriteInSameIterationBeforeReference", loopFlowSource);
    }

    [Fact]
    public void LC037_LoopCarriedReachability_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var reachabilityPath = Path.Combine(analyzerDir, "RawSqlStringConstructionReachability.cs");
        var loopReachabilityPath = Path.Combine(analyzerDir, "RawSqlStringConstructionLoopReachability.cs");

        Assert.True(File.Exists(loopReachabilityPath), "LC037 loop-carried write reachability should live in a focused partial file.");

        var reachabilitySource = File.ReadAllText(reachabilityPath);
        Assert.DoesNotContain("private static bool IsLoopCarriedWriteForReference", reachabilitySource);
        Assert.DoesNotContain("private static bool IsLoopSyntax", reachabilitySource);
        Assert.DoesNotContain("private static bool CanWriteReachLaterLoopIteration", reachabilitySource);

        var loopReachabilitySource = File.ReadAllText(loopReachabilityPath);
        Assert.Contains("private static bool IsLoopCarriedWriteForReference", loopReachabilitySource);
        Assert.Contains("private static bool IsLoopSyntax", loopReachabilitySource);
        Assert.Contains("private static bool CanWriteReachLaterLoopIteration", loopReachabilitySource);
    }

    [Fact]
    public void LC037_StringBuilderAppendScan_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var flowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderFlow.cs");
        var appendScanPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderAppendScan.cs");

        Assert.True(File.Exists(appendScanPath), "LC037 StringBuilder append scan should live in a focused partial file.");

        var flowSource = File.ReadAllText(flowPath);
        Assert.DoesNotContain("private static bool ContainsSuspiciousStringBuilderAppend", flowSource);
        Assert.DoesNotContain("private static int GetLatestGuaranteedStringBuilderReset", flowSource);

        var appendScanSource = File.ReadAllText(appendScanPath);
        Assert.Contains("private static bool ContainsSuspiciousStringBuilderAppend", appendScanSource);
        Assert.DoesNotContain("private static int GetLatestGuaranteedStringBuilderReset", appendScanSource);
    }

    [Fact]
    public void LC037_StringBuilderResetDetection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var appendScanPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderAppendScan.cs");
        var resetDetectionPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderResetDetection.cs");

        Assert.True(File.Exists(resetDetectionPath), "LC037 StringBuilder reset detection should live in a focused partial file.");

        var appendScanSource = File.ReadAllText(appendScanPath);
        Assert.DoesNotContain("private static int GetLatestGuaranteedStringBuilderReset", appendScanSource);
        Assert.DoesNotContain("void TrackReset", appendScanSource);

        var resetDetectionSource = File.ReadAllText(resetDetectionPath);
        Assert.Contains("private static int GetLatestGuaranteedStringBuilderReset", resetDetectionSource);
        Assert.Contains("void TrackReset", resetDetectionSource);
    }

    [Fact]
    public void LC011_AssemblyConfigurationResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var configurationScanPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyConfigurationScan.cs");
        var assemblyResolutionPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyResolution.cs");
        var assemblyLocalsPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyLocalResolution.cs");
        var assemblyMembersPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyMemberResolution.cs");

        Assert.True(File.Exists(assemblyResolutionPath), "LC011 ApplyConfigurationsFromAssembly resolution should live in a focused partial file.");
        Assert.True(File.Exists(assemblyLocalsPath), "LC011 ApplyConfigurationsFromAssembly local resolution should live in a focused partial file.");
        Assert.True(File.Exists(assemblyMembersPath), "LC011 ApplyConfigurationsFromAssembly member resolution should live in a focused partial file.");

        var configurationScanSource = File.ReadAllText(configurationScanPath);
        Assert.DoesNotContain("private static bool IsCurrentAssemblyExpression", configurationScanSource);
        Assert.DoesNotContain("private static bool TryResolveLocalCurrentAssembly", configurationScanSource);

        var assemblyResolutionSource = File.ReadAllText(assemblyResolutionPath);
        Assert.Contains("private static bool IsCurrentAssemblyExpression", assemblyResolutionSource);
        Assert.DoesNotContain("private static bool TryResolveLocalCurrentAssembly", assemblyResolutionSource);

        var assemblyLocalsSource = File.ReadAllText(assemblyLocalsPath);
        Assert.Contains("private static bool TryResolveLocalCurrentAssembly", assemblyLocalsSource);
        Assert.DoesNotContain("private static bool TryResolveMemberCurrentAssembly", assemblyLocalsSource);

        var assemblyMembersSource = File.ReadAllText(assemblyMembersPath);
        Assert.Contains("private static bool TryResolveMemberCurrentAssembly", assemblyMembersSource);
    }

    [Fact]
    public void LC011_AssemblyLocalAssignmentScan_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var assemblyLocalsPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyLocalResolution.cs");
        var assignmentScanPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyLocalAssignments.cs");

        Assert.True(File.Exists(assignmentScanPath), "LC011 ApplyConfigurationsFromAssembly local assignment scanning should live in a focused partial file.");

        var assemblyLocalsSource = File.ReadAllText(assemblyLocalsPath);
        Assert.DoesNotContain("private static bool TryGetLocalAssignment", assemblyLocalsSource);
        Assert.DoesNotContain("private static bool ContainsLocalAssignment", assemblyLocalsSource);

        var assignmentScanSource = File.ReadAllText(assignmentScanPath);
        Assert.Contains("private static bool TryGetLocalAssignment", assignmentScanSource);
        Assert.Contains("private static bool ContainsLocalAssignment", assignmentScanSource);
    }

    [Fact]
    public void LC011_AppliedConfigurationResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var configurationScanPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyConfigurationScan.cs");
        var appliedConfigurationPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAppliedConfigurationResolution.cs");

        Assert.True(File.Exists(appliedConfigurationPath), "LC011 ApplyConfiguration argument/member/local resolution should live in a focused partial file.");

        var configurationScanSource = File.ReadAllText(configurationScanPath);
        Assert.DoesNotContain("private static INamedTypeSymbol? ResolveConfigurationType", configurationScanSource);
        Assert.DoesNotContain("private static bool TryResolveLocalConfiguration", configurationScanSource);

        var appliedConfigurationSource = File.ReadAllText(appliedConfigurationPath);
        Assert.Contains("private static INamedTypeSymbol? ResolveConfigurationType", appliedConfigurationSource);
        Assert.Contains("private static bool TryResolveLocalConfiguration", appliedConfigurationSource);
    }

    [Fact]
    public void LC011_EntityTypeConfigurationScan_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var configurationScanPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyConfigurationScan.cs");
        var entityTypeConfigurationPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyEntityTypeConfigurationScan.cs");

        Assert.True(File.Exists(entityTypeConfigurationPath), "LC011 IEntityTypeConfiguration<T> scanning should live in a focused partial file.");

        var configurationScanSource = File.ReadAllText(configurationScanPath);
        Assert.DoesNotContain("private static EntityTypeConfigurationScan BuildEntityTypeConfigurationScan", configurationScanSource);
        Assert.DoesNotContain("private static (bool hasKey, bool hasNoKey) CheckConfigureMethod", configurationScanSource);
        Assert.DoesNotContain("private static bool TryGetConfiguredEntityType", configurationScanSource);

        var entityTypeConfigurationSource = File.ReadAllText(entityTypeConfigurationPath);
        Assert.Contains("private static EntityTypeConfigurationScan BuildEntityTypeConfigurationScan", entityTypeConfigurationSource);
        Assert.Contains("private static (bool hasKey, bool hasNoKey) CheckConfigureMethod", entityTypeConfigurationSource);
        Assert.Contains("private static bool TryGetConfiguredEntityType", entityTypeConfigurationSource);
    }

    [Fact]
    public void LC011_CompilationModel_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var typeLookupPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyTypeLookup.cs");
        var compilationModelPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyCompilationModel.cs");

        Assert.True(File.Exists(compilationModelPath), "LC011 compilation model and type index should live in a focused partial file.");

        var typeLookupSource = File.ReadAllText(typeLookupPath);
        Assert.DoesNotContain("private sealed class CompilationModel", typeLookupSource);
        Assert.DoesNotContain("private sealed class TypeIndex", typeLookupSource);
        Assert.DoesNotContain("private sealed class EntityTypeConfigurationScan", typeLookupSource);

        var compilationModelSource = File.ReadAllText(compilationModelPath);
        Assert.Contains("private sealed class CompilationModel", compilationModelSource);
        Assert.Contains("private sealed class TypeIndex", compilationModelSource);
        Assert.Contains("private sealed class EntityTypeConfigurationScan", compilationModelSource);
    }

    [Fact]
    public void LC011_PrimaryKeyAttributeRules_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var keyRulesPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyKeyRules.cs");
        var primaryKeyAttributePath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyPrimaryKeyAttributeRules.cs");

        Assert.True(File.Exists(primaryKeyAttributePath), "LC011 [PrimaryKey] attribute parsing should live in a focused partial file.");

        var keyRulesSource = File.ReadAllText(keyRulesPath);
        Assert.DoesNotContain("private bool HasValidPrimaryKeyAttribute", keyRulesSource);
        Assert.DoesNotContain("private static List<string> GetPrimaryKeyPropertyNames", keyRulesSource);

        var primaryKeyAttributeSource = File.ReadAllText(primaryKeyAttributePath);
        Assert.Contains("private bool HasValidPrimaryKeyAttribute", primaryKeyAttributeSource);
        Assert.Contains("private static List<string> GetPrimaryKeyPropertyNames", primaryKeyAttributeSource);
    }

    [Fact]
    public void LC011_AssemblyUsingVisibility_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var assemblyResolutionPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyResolution.cs");
        var usingVisibilityPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyUsingVisibility.cs");

        Assert.True(File.Exists(usingVisibilityPath), "LC011 System.Reflection using and Assembly alias visibility should live in a focused partial file.");

        var assemblyResolutionSource = File.ReadAllText(assemblyResolutionPath);
        Assert.DoesNotContain("private static bool HasSystemReflectionAssemblyAliasInScope", assemblyResolutionSource);
        Assert.DoesNotContain("private static bool HasSystemReflectionUsing", assemblyResolutionSource);

        var usingVisibilitySource = File.ReadAllText(usingVisibilityPath);
        Assert.Contains("private static bool HasSystemReflectionAssemblyAliasInScope", usingVisibilitySource);
        Assert.Contains("private static bool HasSystemReflectionUsing", usingVisibilitySource);
    }

    [Fact]
    public void LC017_FixerAccessedProperties_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC017_WholeEntityProjection");
        var contextAnalysisPath = Path.Combine(analyzerDir, "WholeEntityProjectionFixerContextAnalysis.cs");
        var accessedPropertiesPath = Path.Combine(analyzerDir, "WholeEntityProjectionFixerAccessedProperties.cs");

        Assert.True(File.Exists(accessedPropertiesPath), "LC017 fixer accessed-property scanning should live in a focused partial file.");

        var contextAnalysisSource = File.ReadAllText(contextAnalysisPath);
        Assert.Contains("private static bool TryCreateProjectionFixContext", contextAnalysisSource);
        Assert.DoesNotContain("private static HashSet<string> FindAccessedProperties", contextAnalysisSource);

        var accessedPropertiesSource = File.ReadAllText(accessedPropertiesPath);
        Assert.Contains("private static HashSet<string> FindAccessedProperties", accessedPropertiesSource);
        Assert.Contains("ForEachStatementSyntax", accessedPropertiesSource);
    }

    [Fact]
    public void LC011_AssemblyConfigurationTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC011_EntityMissingPrimaryKey");
        var edgeCasesPath = Path.Combine(testDir, "EntityMissingPrimaryKeyEdgeCasesTests.cs");
        var assemblyTestsPath = Path.Combine(testDir, "EntityMissingPrimaryKeyAssemblyConfigurationTests.cs");

        Assert.True(File.Exists(assemblyTestsPath), "LC011 ApplyConfigurationsFromAssembly edge cases should live in a focused partial test file.");

        var edgeCasesSource = File.ReadAllText(edgeCasesPath);
        Assert.Contains("public partial class EntityMissingPrimaryKeyEdgeCasesTests", edgeCasesSource);
        Assert.DoesNotContain("TestCrime_ExternalApplyConfigurationsFromAssembly_ShouldNotApplyLocalConfig", edgeCasesSource);
        Assert.DoesNotContain("TestInnocent_MemberExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger", edgeCasesSource);

        var assemblyTestsSource = File.ReadAllText(assemblyTestsPath);
        Assert.Contains("public partial class EntityMissingPrimaryKeyEdgeCasesTests", assemblyTestsSource);
        Assert.Contains("TestCrime_ExternalApplyConfigurationsFromAssembly_ShouldNotApplyLocalConfig", assemblyTestsSource);
        Assert.Contains("TestInnocent_MemberExecutingAssemblyApplyConfigurationsFromAssembly_ShouldNotTrigger", assemblyTestsSource);
    }

    [Fact]
    public void LC011_BuilderResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var configurationScanPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyConfigurationScan.cs");
        var builderResolutionPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyBuilderResolution.cs");
        var localBuilderResolutionPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyLocalBuilderResolution.cs");

        Assert.True(File.Exists(builderResolutionPath), "LC011 builder-expression and owned-type resolution should live in a focused partial file.");
        Assert.True(File.Exists(localBuilderResolutionPath), "LC011 local builder backtracking should live in a focused partial file.");

        var configurationScanSource = File.ReadAllText(configurationScanPath);
        Assert.DoesNotContain("private static bool TryResolveLocalBuilder", configurationScanSource);

        var builderResolutionSource = File.ReadAllText(builderResolutionPath);
        Assert.Contains("private static bool TryResolveEntityTypeFromBuilderExpression", builderResolutionSource);
        Assert.DoesNotContain("private static bool TryResolveLocalBuilder", builderResolutionSource);

        var localBuilderResolutionSource = File.ReadAllText(localBuilderResolutionPath);
        Assert.Contains("private static bool TryResolveLocalBuilder", localBuilderResolutionSource);
        Assert.Contains("LocalDeclarationStatementSyntax", localBuilderResolutionSource);
    }

    [Fact]
    public void LC011_OwnedBuilderNavigationResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var builderResolutionPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyBuilderResolution.cs");
        var ownedNavigationPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyOwnedBuilderNavigationResolution.cs");

        Assert.True(File.Exists(ownedNavigationPath), "LC011 owned-entity builder navigation resolution should live in a focused partial file.");

        var builderResolutionSource = File.ReadAllText(builderResolutionPath);
        Assert.DoesNotContain("private static bool TryGetOwnedEntityType", builderResolutionSource);
        Assert.DoesNotContain("private static INamedTypeSymbol? TryGetCollectionElementType", builderResolutionSource);

        var ownedNavigationSource = File.ReadAllText(ownedNavigationPath);
        Assert.Contains("private static bool TryGetOwnedEntityType", ownedNavigationSource);
        Assert.Contains("private static INamedTypeSymbol? TryGetCollectionElementType", ownedNavigationSource);
    }

    [Fact]
    public void LC007_QueryProvenanceAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC007_NPlusOneLooper");
        var analysisPath = Path.Combine(analyzerDir, "NPlusOneLooperAnalysis.cs");
        var queryProvenancePath = Path.Combine(analyzerDir, "NPlusOneLooperQueryProvenance.cs");

        Assert.True(File.Exists(queryProvenancePath), "LC007 query provenance analysis should live in a focused partial file.");

        var analysisSource = File.ReadAllText(analysisPath);
        Assert.Contains("internal static partial class NPlusOneLooperAnalysis", analysisSource);
        Assert.DoesNotContain("private static QueryProvenance AnalyzeQueryProvenance", analysisSource);
        Assert.DoesNotContain("private enum QueryProvenanceKind", analysisSource);

        var queryProvenanceSource = File.ReadAllText(queryProvenancePath);
        Assert.Contains("private static QueryProvenance AnalyzeQueryProvenance", queryProvenanceSource);
        Assert.Contains("private enum QueryProvenanceKind", queryProvenanceSource);
    }

    [Fact]
    public void LC007_QueryProvenanceClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC007_NPlusOneLooper");
        var queryProvenancePath = Path.Combine(analyzerDir, "NPlusOneLooperQueryProvenance.cs");
        var classificationPath = Path.Combine(analyzerDir, "NPlusOneLooperQueryProvenanceClassification.cs");

        Assert.True(File.Exists(classificationPath), "LC007 query provenance source classification should live in a focused partial file.");

        var queryProvenanceSource = File.ReadAllText(queryProvenancePath);
        Assert.Contains("private static QueryProvenance AnalyzeQueryProvenance", queryProvenanceSource);
        Assert.Contains("private enum QueryProvenanceKind", queryProvenanceSource);
        Assert.DoesNotContain("private static bool IsDbContextSetInvocation", queryProvenanceSource);
        Assert.DoesNotContain("private static bool IsClientBoundaryInvocation", queryProvenanceSource);
        Assert.DoesNotContain("private static bool IsNavigationQueryInvocation", queryProvenanceSource);
        Assert.DoesNotContain("private static bool IsAsQueryableInvocation", queryProvenanceSource);
        Assert.DoesNotContain("private static bool IsChangeTrackingNamespace", queryProvenanceSource);

        var classificationSource = File.ReadAllText(classificationPath);
        Assert.Contains("private static bool IsDbContextSetInvocation", classificationSource);
        Assert.Contains("private static bool IsClientBoundaryInvocation", classificationSource);
        Assert.Contains("private static bool IsNavigationQueryInvocation", classificationSource);
        Assert.Contains("private static bool IsAsQueryableInvocation", classificationSource);
        Assert.Contains("private static bool IsChangeTrackingNamespace", classificationSource);
        Assert.DoesNotContain("private static QueryProvenance AnalyzeQueryProvenance", classificationSource);
        Assert.DoesNotContain("private enum QueryProvenanceKind", classificationSource);
    }

    [Fact]
    public void LC007_ExecutionMethodClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC007_NPlusOneLooper");
        var analysisPath = Path.Combine(analyzerDir, "NPlusOneLooperAnalysis.cs");
        var executionMethodsPath = Path.Combine(analyzerDir, "NPlusOneLooperExecutionMethods.cs");

        Assert.True(File.Exists(executionMethodsPath), "LC007 immediate execution and set-based method classification should live in a focused partial file.");

        var analysisSource = File.ReadAllText(analysisPath);
        Assert.DoesNotContain("private static readonly HashSet<string> ImmediateQueryExecutionMethods", analysisSource);
        Assert.DoesNotContain("private static readonly HashSet<string> SetBasedExecutorMethods", analysisSource);

        var executionMethodsSource = File.ReadAllText(executionMethodsPath);
        Assert.Contains("private static readonly HashSet<string> ImmediateQueryExecutionMethods", executionMethodsSource);
        Assert.Contains("private static readonly HashSet<string> SetBasedExecutorMethods", executionMethodsSource);
    }

    [Fact]
    public void LC007_DatabaseExecutionMatching_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC007_NPlusOneLooper");
        var analysisPath = Path.Combine(analyzerDir, "NPlusOneLooperAnalysis.cs");
        var databaseExecutionPath = Path.Combine(analyzerDir, "NPlusOneLooperDatabaseExecution.cs");

        Assert.True(File.Exists(databaseExecutionPath), "LC007 database execution matching should live in a focused partial file.");

        var analysisSource = File.ReadAllText(analysisPath);
        Assert.Contains("internal static partial class NPlusOneLooperAnalysis", analysisSource);
        Assert.DoesNotContain("private static bool TryMatchDatabaseExecution", analysisSource);
        Assert.DoesNotContain("public static bool HasStronglyTypedNavigationAccessor", analysisSource);

        var databaseExecutionSource = File.ReadAllText(databaseExecutionPath);
        Assert.Contains("internal static partial class NPlusOneLooperAnalysis", databaseExecutionSource);
        Assert.Contains("private static bool TryMatchDatabaseExecution", databaseExecutionSource);
        Assert.Contains("public static bool HasStronglyTypedNavigationAccessor", databaseExecutionSource);
    }

    [Fact]
    public void AnalyzerPerformance_LC023Sources_LiveInDedicatedPartial()
    {
        var architectureDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Architecture");
        var performancePath = Path.Combine(architectureDir, "AnalyzerPerformanceTests.cs");
        var lc023SourcesPath = Path.Combine(architectureDir, "AnalyzerPerformanceLc023Sources.cs");

        Assert.True(File.Exists(lc023SourcesPath), "LC023 performance source builders should live in a focused partial file.");

        var performanceSource = File.ReadAllText(performancePath);
        Assert.Contains("public partial class AnalyzerPerformanceTests", performanceSource);
        Assert.DoesNotContain("private static string GenerateLc023StressSource", performanceSource);
        Assert.DoesNotContain("private static string[] GenerateLc023MultiTreeStressSources", performanceSource);

        var lc023Sources = File.ReadAllText(lc023SourcesPath);
        Assert.Contains("public partial class AnalyzerPerformanceTests", lc023Sources);
        Assert.Contains("private static string GenerateLc023StressSource", lc023Sources);
        Assert.Contains("private static string[] GenerateLc023MultiTreeStressSources", lc023Sources);
    }

    [Fact]
    public void LC002_FixerInlineMaterializationHelpers_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC002_PrematureMaterialization");
        var fixerPath = Path.Combine(analyzerDir, "PrematureMaterializationFixer.cs");
        var inlineMaterializationPath = Path.Combine(analyzerDir, "PrematureMaterializationFixerInlineMaterialization.cs");

        Assert.True(File.Exists(inlineMaterializationPath), "LC002 fixer inline materializer shape/safety helpers should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class PrematureMaterializationFixer", fixerSource);
        Assert.DoesNotContain("private static bool IsInlineMaterializerReceiver", fixerSource);
        Assert.DoesNotContain("private static bool TryGetInlineMaterializerParts", fixerSource);
        Assert.DoesNotContain("private static bool IsInsideOuterMaterialization", fixerSource);
        Assert.DoesNotContain("private static bool IsMaterializingConstructor", fixerSource);

        var inlineMaterializationSource = File.ReadAllText(inlineMaterializationPath);
        Assert.Contains("private static bool IsInlineMaterializerReceiver", inlineMaterializationSource);
        Assert.Contains("private static bool TryGetInlineMaterializerParts", inlineMaterializationSource);
        Assert.Contains("private static bool IsInsideOuterMaterialization", inlineMaterializationSource);
        Assert.Contains("private static bool IsMaterializingConstructor", inlineMaterializationSource);
    }

    [Fact]
    public void LC010_RetryGuardAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC010_SaveChangesInLoop");
        var analyzerPath = Path.Combine(analyzerDir, "SaveChangesInLoopAnalyzer.cs");
        var retryGuardPath = Path.Combine(analyzerDir, "SaveChangesInLoopRetryGuard.cs");

        Assert.True(File.Exists(retryGuardPath), "LC010 catch-guarded retry suppression should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class SaveChangesInLoopAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsSaveInsideCatchGuardedRetryAttempt", analyzerSource);
        Assert.DoesNotContain("private static TryStatementSyntax? FindTryStatementBetweenInvocationAndLoop", analyzerSource);
        Assert.DoesNotContain("private static bool HasLoopExitAfterSave", analyzerSource);

        var retryGuardSource = File.ReadAllText(retryGuardPath);
        Assert.Contains("private static bool IsSaveInsideCatchGuardedRetryAttempt", retryGuardSource);
        Assert.Contains("private static TryStatementSyntax? FindTryStatementBetweenInvocationAndLoop", retryGuardSource);
        Assert.Contains("private static bool HasLoopExitAfterSave", retryGuardSource);
    }

    [Fact]
    public void LC010_DelegateExecutionAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC010_SaveChangesInLoop");
        var analyzerPath = Path.Combine(analyzerDir, "SaveChangesInLoopAnalyzer.cs");
        var delegateExecutionPath = Path.Combine(analyzerDir, "SaveChangesInLoopDelegateExecution.cs");

        Assert.True(File.Exists(delegateExecutionPath), "LC010 delegate-loop execution proof should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class SaveChangesInLoopAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsInsideDelegateCalledFromLoop", analyzerSource);
        Assert.DoesNotContain("private static bool IsDelegateLocalCalledFromLoop", analyzerSource);

        var delegateExecutionSource = File.ReadAllText(delegateExecutionPath);
        Assert.Contains("private static bool IsInsideDelegateCalledFromLoop", delegateExecutionSource);
        Assert.Contains("private static bool IsDelegateLocalCalledFromLoop", delegateExecutionSource);
        Assert.Contains("private static bool IsLocalAssignedInStraightLinePathBetween", delegateExecutionSource);
    }

    [Fact]
    public void LC010_FixerMoveSafety_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC010_SaveChangesInLoop");
        var fixerPath = Path.Combine(analyzerDir, "SaveChangesInLoopFixer.cs");
        var moveSafetyPath = Path.Combine(analyzerDir, "SaveChangesInLoopFixerMoveSafety.cs");

        Assert.True(File.Exists(moveSafetyPath), "LC010 fixer move-safety predicates should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class SaveChangesInLoopFixer", fixerSource);
        Assert.DoesNotContain("private static bool TryGetMovableSaveStatement", fixerSource);
        Assert.DoesNotContain("private static bool ContainsUnsafeControlFlow", fixerSource);
        Assert.DoesNotContain("private static bool IsSaveReceiverDeclaredInsideLoop", fixerSource);

        var moveSafetySource = File.ReadAllText(moveSafetyPath);
        Assert.Contains("private static bool TryGetMovableSaveStatement", moveSafetySource);
        Assert.Contains("private static bool ContainsUnsafeControlFlow", moveSafetySource);
        Assert.Contains("private static bool IsSaveReceiverDeclaredInsideLoop", moveSafetySource);
    }

    [Fact]
    public void LC032_FixerExtensionNamespaceResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var fixerPath = Path.Combine(analyzerDir, "ExecuteUpdateForBulkUpdatesFixer.cs");
        var namespaceResolutionPath = Path.Combine(analyzerDir, "ExecuteUpdateForBulkUpdatesFixerNamespaceResolution.cs");

        Assert.True(File.Exists(namespaceResolutionPath), "LC032 fixer ExecuteUpdate extension namespace resolution should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.DoesNotContain("private static string? ResolveExecuteUpdateNamespace", fixerSource);
        Assert.DoesNotContain("private static bool IsExecuteUpdateLikeMethod", fixerSource);

        var namespaceResolutionSource = File.ReadAllText(namespaceResolutionPath);
        Assert.Contains("private static string? ResolveExecuteUpdateNamespace", namespaceResolutionSource);
        Assert.Contains("private static bool IsExecuteUpdateLikeMethod", namespaceResolutionSource);
    }

    [Fact]
    public void LC016_FixerVariableNameGeneration_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC016_AvoidDateTimeNow");
        var fixerPath = Path.Combine(analyzerDir, "AvoidDateTimeNowFixer.cs");
        var variableNamesPath = Path.Combine(analyzerDir, "AvoidDateTimeNowFixerVariableNames.cs");

        Assert.True(File.Exists(variableNamesPath), "LC016 fixer variable-name collision handling should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.DoesNotContain("private static string GetUniqueVariableName", fixerSource);
        Assert.DoesNotContain("private static HashSet<string> CollectExistingNames", fixerSource);
        Assert.DoesNotContain("private static void AddEnclosingParameterNames", fixerSource);
        Assert.DoesNotContain("private static void AddParameterNames", fixerSource);

        var variableNamesSource = File.ReadAllText(variableNamesPath);
        Assert.Contains("private static string GetUniqueVariableName", variableNamesSource);
        Assert.Contains("private static HashSet<string> CollectExistingNames", variableNamesSource);
        Assert.Contains("private static void AddEnclosingParameterNames", variableNamesSource);
        Assert.Contains("private static void AddParameterNames", variableNamesSource);
    }

    [Fact]
    public void LC023_KeyConfigurationAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC023_FindInsteadOfFirstOrDefault");
        var keyAnalysisPath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultKeyAnalysis.cs");
        var keyConfigurationPath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultKeyConfiguration.cs");

        Assert.True(File.Exists(keyConfigurationPath), "LC023 Fluent API key-configuration parsing should live in a focused partial file.");

        var keyAnalysisSource = File.ReadAllText(keyAnalysisPath);
        Assert.DoesNotContain("private static bool TryGetEntityTypeBuilderEntity", keyAnalysisSource);
        Assert.DoesNotContain("private static ConfiguredPrimaryKey AnalyzeKeyArgument", keyAnalysisSource);
        Assert.DoesNotContain("private readonly struct ConfiguredPrimaryKey", keyAnalysisSource);

        var keyConfigurationSource = File.ReadAllText(keyConfigurationPath);
        Assert.Contains("private static bool TryGetEntityTypeBuilderEntity", keyConfigurationSource);
        Assert.Contains("private static ConfiguredPrimaryKey AnalyzeKeyArgument", keyConfigurationSource);
        Assert.Contains("private readonly struct ConfiguredPrimaryKey", keyConfigurationSource);
    }

    [Fact]
    public void LC023_PredicateAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC023_FindInsteadOfFirstOrDefault");
        var analyzerPath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultAnalyzer.cs");
        var predicateAnalysisPath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultPredicateAnalysis.cs");

        Assert.True(File.Exists(predicateAnalysisPath), "LC023 primary-key predicate analysis should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class FindInsteadOfFirstOrDefaultAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool TryGetPrimaryKeyEqualityProperty", analyzerSource);
        Assert.DoesNotContain("private static bool TryGetLambdaParameterProperty", analyzerSource);

        var predicateAnalysisSource = File.ReadAllText(predicateAnalysisPath);
        Assert.Contains("private static bool TryGetPrimaryKeyEqualityProperty", predicateAnalysisSource);
        Assert.Contains("private static bool TryGetLambdaParameterProperty", predicateAnalysisSource);
    }

    [Fact]
    public void LC023_FixerKeyValueAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC023_FindInsteadOfFirstOrDefault");
        var contextPath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultFixerContext.cs");
        var keyValueAnalysisPath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultFixerKeyValueAnalysis.cs");

        Assert.True(File.Exists(keyValueAnalysisPath), "LC023 fixer key-value analysis should live in a focused partial file.");

        var contextSource = File.ReadAllText(contextPath);
        Assert.DoesNotContain("private static bool TryGetKeyValueExpression", contextSource);
        Assert.DoesNotContain("private static bool ReferencesLambdaParameter", contextSource);
        Assert.DoesNotContain("private static bool IsPrimaryKeyAccess", contextSource);

        var keyValueAnalysisSource = File.ReadAllText(keyValueAnalysisPath);
        Assert.Contains("private static bool TryGetKeyValueExpression", keyValueAnalysisSource);
        Assert.Contains("private static bool ReferencesLambdaParameter", keyValueAnalysisSource);
        Assert.Contains("private static bool IsPrimaryKeyAccess", keyValueAnalysisSource);
    }

    [Fact]
    public void LC045_UsageTargetHelpers_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var usageAnalysisPath = Path.Combine(analyzerDir, "MissingIncludeUsageAnalysis.cs");
        var usageTargetsPath = Path.Combine(analyzerDir, "MissingIncludeUsageTargets.cs");
        var indexedAccessPath = Path.Combine(analyzerDir, "MissingIncludeIndexedAccess.cs");

        Assert.True(File.Exists(usageTargetsPath), "LC045 usage target and result-local helpers should live in a focused partial file.");
        Assert.True(File.Exists(indexedAccessPath), "LC045 indexed collection access classification should live in a focused partial file.");

        var usageAnalysisSource = File.ReadAllText(usageAnalysisPath);
        Assert.DoesNotContain("private static bool IsWriteTarget", usageAnalysisSource);
        Assert.DoesNotContain("private static bool IsIndexedAccessOf", usageAnalysisSource);
        Assert.DoesNotContain("private static ILocalSymbol? FindVariableAssignment", usageAnalysisSource);

        var usageTargetsSource = File.ReadAllText(usageTargetsPath);
        Assert.Contains("private static bool IsWriteTarget", usageTargetsSource);
        Assert.DoesNotContain("private static bool IsIndexedAccessOf", usageTargetsSource);
        Assert.Contains("private static ILocalSymbol? FindVariableAssignment", usageTargetsSource);

        var indexedAccessSource = File.ReadAllText(indexedAccessPath);
        Assert.Contains("private static bool IsIndexedAccessOf", indexedAccessSource);
    }

    [Fact]
    public void LC045_NavigationAccessCollection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var usageAnalysisPath = Path.Combine(analyzerDir, "MissingIncludeUsageAnalysis.cs");
        var accessCollectionPath = Path.Combine(analyzerDir, "MissingIncludeNavigationAccessCollection.cs");

        Assert.True(File.Exists(accessCollectionPath), "LC045 navigation access collection and satisfaction helpers should live in a focused partial file.");

        var usageAnalysisSource = File.ReadAllText(usageAnalysisPath);
        Assert.DoesNotContain("private readonly struct NavigationAccess", usageAnalysisSource);
        Assert.DoesNotContain("private static bool IsSatisfied", usageAnalysisSource);
        Assert.DoesNotContain("private static List<NavigationAccess> CollectInlineAccesses", usageAnalysisSource);

        var accessCollectionSource = File.ReadAllText(accessCollectionPath);
        Assert.Contains("private readonly struct NavigationAccess", accessCollectionSource);
        Assert.Contains("private static bool IsSatisfied", accessCollectionSource);
        Assert.Contains("private static List<NavigationAccess> CollectInlineAccesses", accessCollectionSource);
    }

    [Fact]
    public void LC045_QueryOperatorClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var queryAnalysisPath = Path.Combine(analyzerDir, "MissingIncludeQueryAnalysis.cs");
        var queryOperatorsPath = Path.Combine(analyzerDir, "MissingIncludeQueryOperators.cs");
        var querySemanticsPath = Path.Combine(analyzerDir, "MissingIncludeQuerySemantics.cs");

        Assert.True(File.Exists(queryOperatorsPath), "LC045 query operator/materializer classification should live in a focused partial file.");
        Assert.True(File.Exists(querySemanticsPath), "LC045 exact query symbol/source classification should live in a focused partial file.");

        var queryAnalysisSource = File.ReadAllText(queryAnalysisPath);
        Assert.DoesNotContain("private static readonly ImmutableHashSet<string> ShapePreservingQueryableOperators", queryAnalysisSource);
        Assert.DoesNotContain("private static bool IsEntityMaterializer", queryAnalysisSource);

        var queryOperatorsSource = File.ReadAllText(queryOperatorsPath);
        Assert.Contains("private static readonly ImmutableHashSet<string> ShapePreservingQueryableOperators", queryOperatorsSource);
        Assert.Contains("private static readonly ImmutableHashSet<string> ShapePreservingEntityFrameworkOperators", queryOperatorsSource);
        Assert.Contains("private static bool IsEntityMaterializer", queryOperatorsSource);

        var querySemanticsSource = File.ReadAllText(querySemanticsPath);
        Assert.Contains("private static IOperation? GetQuerySource", querySemanticsSource);
        Assert.Contains("private static bool IsExactShapePreservingQueryStep", querySemanticsSource);
    }

    [Fact]
    public void LC025_NoTrackingSourceResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC025_AsNoTrackingWithUpdate");
        var analyzerPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateAnalyzer.cs");
        var sourceResolutionPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateSourceResolution.cs");

        Assert.True(File.Exists(sourceResolutionPath), "LC025 local source resolution should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AsNoTrackingWithUpdateAnalyzer", analyzerSource);
        Assert.DoesNotContain("private bool IsFromNoTrackingQuery", analyzerSource);
        Assert.DoesNotContain("private readonly struct LocalOrigin", analyzerSource);

        var sourceResolutionSource = File.ReadAllText(sourceResolutionPath);
        Assert.Contains("private bool IsFromNoTrackingQuery", sourceResolutionSource);
        Assert.DoesNotContain("private readonly struct LocalOrigin", sourceResolutionSource);
    }

    [Fact]
    public void LC025_EntryStateParsing_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC025_AsNoTrackingWithUpdate");
        var analyzerPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateAnalyzer.cs");
        var entryStatePath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateEntryStateParsing.cs");

        Assert.True(File.Exists(entryStatePath), "LC025 Entry(entity).State parsing should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool TryParseEntryStateWrite", analyzerSource);
        Assert.DoesNotContain("private static bool TryGetEntityStateName", analyzerSource);

        var entryStateSource = File.ReadAllText(entryStatePath);
        Assert.Contains("private static bool TryParseEntryStateWrite", entryStateSource);
        Assert.Contains("private static bool TryGetEntityStateName", entryStateSource);
    }

    [Fact]
    public void LC014_QuerySourceResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC014_AvoidStringCaseConversion");
        var analyzerPath = Path.Combine(analyzerDir, "AvoidStringCaseConversionAnalyzer.cs");
        var querySourcePath = Path.Combine(analyzerDir, "AvoidStringCaseConversionQuerySource.cs");

        Assert.True(File.Exists(querySourcePath), "LC014 EF query-source resolution should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AvoidStringCaseConversionAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool HasEntityFrameworkQuerySource", analyzerSource);
        Assert.DoesNotContain("private static bool TryResolveLocalValue", analyzerSource);

        var querySource = File.ReadAllText(querySourcePath);
        Assert.Contains("private static bool HasEntityFrameworkQuerySource", querySource);
        Assert.Contains("private static bool TryResolveLocalValue", querySource);
    }

    [Fact]
    public void LC014_ReceiverDependency_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC014_AvoidStringCaseConversion");
        var analyzerPath = Path.Combine(analyzerDir, "AvoidStringCaseConversionAnalyzer.cs");
        var receiverDependencyPath = Path.Combine(analyzerDir, "AvoidStringCaseConversionReceiverDependency.cs");

        Assert.True(File.Exists(receiverDependencyPath), "LC014 receiver dependency analysis should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AvoidStringCaseConversionAnalyzer", analyzerSource);
        Assert.DoesNotContain("private bool ReceiverDependsOnParameter", analyzerSource);

        var receiverDependencySource = File.ReadAllText(receiverDependencyPath);
        Assert.Contains("private bool ReceiverDependsOnParameter", receiverDependencySource);
    }

    [Fact]
    public void LC030_DependencyInjectionRegistration_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC030_DbContextInSingleton");
        var analyzerPath = Path.Combine(analyzerDir, "DbContextInSingletonAnalyzer.cs");
        var registrationPath = Path.Combine(analyzerDir, "DbContextInSingletonRegistrationAnalysis.cs");

        Assert.True(File.Exists(registrationPath), "LC030 dependency-injection registration analysis should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class DbContextInSingletonAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static void AnalyzeAddSingleton", analyzerSource);

        var registrationSource = File.ReadAllText(registrationPath);
        Assert.Contains("private static void AnalyzeAddSingleton", registrationSource);
        Assert.Contains("private static void AnalyzeAddDbContext", registrationSource);
    }

    [Fact]
    public void LC030_RegistrationLifetimeArgumentRules_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC030_DbContextInSingleton");
        var registrationPath = Path.Combine(analyzerDir, "DbContextInSingletonRegistrationAnalysis.cs");
        var lifetimePath = Path.Combine(analyzerDir, "DbContextInSingletonRegistrationLifetime.cs");

        Assert.True(File.Exists(lifetimePath), "LC030 AddDbContext singleton-lifetime argument parsing should live in a focused partial file.");

        var registrationSource = File.ReadAllText(registrationPath);
        Assert.Contains("private static void AnalyzeAddDbContext", registrationSource);
        Assert.DoesNotContain("private static bool HasSingletonContextLifetimeArgument", registrationSource);
        Assert.DoesNotContain("private static bool IsSingletonLifetime", registrationSource);
        Assert.DoesNotContain("private static IOperation UnwrapConversion", registrationSource);

        var lifetimeSource = File.ReadAllText(lifetimePath);
        Assert.Contains("private static bool HasSingletonContextLifetimeArgument", lifetimeSource);
        Assert.Contains("private static bool IsSingletonLifetime", lifetimeSource);
        Assert.Contains("private static IOperation UnwrapConversion", lifetimeSource);
        Assert.DoesNotContain("private static void AnalyzeAddDbContext", lifetimeSource);
    }

    [Fact]
    public void LC030_LongLivedEvidence_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC030_DbContextInSingleton");
        var analyzerPath = Path.Combine(analyzerDir, "DbContextInSingletonAnalyzer.cs");
        var evidencePath = Path.Combine(analyzerDir, "DbContextInSingletonLongLivedEvidence.cs");

        Assert.True(File.Exists(evidencePath), "LC030 long-lived type evidence should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static void AddIntrinsicLongLivedEvidence", analyzerSource);
        Assert.DoesNotContain("private static bool HasConventionalMiddlewareSignature", analyzerSource);

        var evidenceSource = File.ReadAllText(evidencePath);
        Assert.Contains("private static void AddIntrinsicLongLivedEvidence", evidenceSource);
        Assert.DoesNotContain("private static bool HasConventionalMiddlewareSignature", evidenceSource);
    }

    [Fact]
    public void LC030_LongLivedOptions_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC030_DbContextInSingleton");
        var evidencePath = Path.Combine(analyzerDir, "DbContextInSingletonLongLivedEvidence.cs");
        var optionsPath = Path.Combine(analyzerDir, "DbContextInSingletonLongLivedOptions.cs");

        Assert.True(File.Exists(optionsPath), "LC030 long-lived options and configured type matching should live in a focused partial file.");

        var evidenceSource = File.ReadAllText(evidencePath);
        Assert.DoesNotContain("private static Lc030Options GetOptions", evidenceSource);
        Assert.DoesNotContain("private static bool TryGetConfiguredLongLivedReason", evidenceSource);
        Assert.DoesNotContain("private sealed class Lc030Options", evidenceSource);

        var optionsSource = File.ReadAllText(optionsPath);
        Assert.Contains("private static Lc030Options GetOptions", optionsSource);
        Assert.Contains("private static bool TryGetConfiguredLongLivedReason", optionsSource);
        Assert.Contains("private sealed class Lc030Options", optionsSource);
    }

    [Fact]
    public void LC030_FreshComputedPropertyAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC030_DbContextInSingleton");
        var analyzerPath = Path.Combine(analyzerDir, "DbContextInSingletonAnalyzer.cs");
        var freshPropertyPath = Path.Combine(analyzerDir, "DbContextInSingletonFreshComputedProperty.cs");

        Assert.True(File.Exists(freshPropertyPath), "LC030 fresh computed DbContext property analysis should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool IsFreshComputedProperty", analyzerSource);
        Assert.DoesNotContain("private static bool IsDbContextFactoryCreate", analyzerSource);

        var freshPropertySource = File.ReadAllText(freshPropertyPath);
        Assert.Contains("private static bool IsFreshComputedProperty", freshPropertySource);
        Assert.Contains("private static bool IsDbContextFactoryCreate", freshPropertySource);
    }

    [Fact]
    public void LC030_CandidateCollectionAndReporting_LiveInDedicatedPartials()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC030_DbContextInSingleton");
        var analyzerPath = Path.Combine(analyzerDir, "DbContextInSingletonAnalyzer.cs");
        var candidateCollectionPath = Path.Combine(analyzerDir, "DbContextInSingletonCandidateCollection.cs");
        var candidateReportingPath = Path.Combine(analyzerDir, "DbContextInSingletonCandidateReporting.cs");

        Assert.True(File.Exists(candidateCollectionPath), "LC030 candidate field/property/constructor collection should live in a focused partial file.");
        Assert.True(File.Exists(candidateReportingPath), "LC030 candidate collection and final diagnostic reporting should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static void AnalyzeField", analyzerSource);
        Assert.DoesNotContain("private static void ReportCandidateDiagnostics", analyzerSource);
        Assert.DoesNotContain("private sealed class DbContextCandidate", analyzerSource);

        var candidateCollectionSource = File.ReadAllText(candidateCollectionPath);
        Assert.Contains("private static void AnalyzeField", candidateCollectionSource);
        Assert.Contains("private static void AnalyzeProperty", candidateCollectionSource);
        Assert.Contains("private static void AnalyzeConstructor", candidateCollectionSource);
        Assert.Contains("private static void AddCandidate", candidateCollectionSource);

        var candidateReportingSource = File.ReadAllText(candidateReportingPath);
        Assert.DoesNotContain("private static void AnalyzeField", candidateReportingSource);
        Assert.DoesNotContain("private static void AnalyzeProperty", candidateReportingSource);
        Assert.DoesNotContain("private static void AnalyzeConstructor", candidateReportingSource);
        Assert.DoesNotContain("private static void AddCandidate", candidateReportingSource);
        Assert.Contains("private static void ReportCandidateDiagnostics", candidateReportingSource);
        Assert.Contains("private sealed class DbContextCandidate", candidateReportingSource);
    }

    [Fact]
    public void LC033_FixerSyntaxHelpers_LiveInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC033_UseFrozenSetForStaticMembershipCaches");
        var rewritePath = Path.Combine(fixerDir, "UseFrozenSetForStaticMembershipCachesFixerRewrite.cs");
        var syntaxPath = Path.Combine(fixerDir, "UseFrozenSetForStaticMembershipCachesFixerSyntax.cs");

        Assert.True(File.Exists(syntaxPath), "LC033 FrozenSet fixer syntax construction helpers should live in a focused partial file.");

        var rewriteSource = File.ReadAllText(rewritePath);
        Assert.DoesNotContain("private static ExpressionSyntax CreateToFrozenSetInvocation", rewriteSource);
        Assert.DoesNotContain("private static TypeSyntax CreateTypeSyntax", rewriteSource);
        Assert.DoesNotContain("private static ExpressionSyntax ParenthesizeIfNeeded", rewriteSource);

        var syntaxSource = File.ReadAllText(syntaxPath);
        Assert.Contains("private static ExpressionSyntax CreateToFrozenSetInvocation", syntaxSource);
        Assert.Contains("private static TypeSyntax CreateTypeSyntax", syntaxSource);
        Assert.Contains("private static ExpressionSyntax ParenthesizeIfNeeded", syntaxSource);
    }

    [Fact]
    public void LC035_LocalConditionalFlow_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC035_MissingWhereBeforeExecuteDeleteUpdate");
        var analyzerPath = Path.Combine(analyzerDir, "MissingWhereBeforeExecuteDeleteUpdateAnalyzer.cs");
        var localFlowPath = Path.Combine(analyzerDir, "MissingWhereBeforeExecuteDeleteUpdateLocalFlow.cs");

        Assert.True(File.Exists(localFlowPath), "LC035 local initializer and conditional assignment flow should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class MissingWhereBeforeExecuteDeleteUpdateAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool HasWhereInLocalInitializer", analyzerSource);
        Assert.DoesNotContain("private static bool HasWhereInExhaustiveIfElseAssignments", analyzerSource);
        Assert.DoesNotContain("private static bool IsControlFlowConditionalAssignment", analyzerSource);

        var localFlowSource = File.ReadAllText(localFlowPath);
        Assert.Contains("private static bool HasWhereInLocalInitializer", localFlowSource);
        Assert.Contains("private static bool HasWhereInExhaustiveIfElseAssignments", localFlowSource);
        Assert.Contains("private static bool IsControlFlowConditionalAssignment", localFlowSource);
    }

    [Fact]
    public void LC027_RelationshipBuilderLocalResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC027_MissingExplicitForeignKey");
        var configurationPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyConfigurationAnalysis.cs");
        var relationshipLocalsPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyRelationshipLocals.cs");

        Assert.True(File.Exists(relationshipLocalsPath), "LC027 relationship-builder local resolution should live in a focused partial file.");

        var configurationSource = File.ReadAllText(configurationPath);
        Assert.DoesNotContain("private static List<RelationshipConfiguration> BuildRelationshipBuilderLocalMap", configurationSource);
        Assert.DoesNotContain("private static bool IsShadowedByNestedLocal", configurationSource);

        var relationshipLocalsSource = File.ReadAllText(relationshipLocalsPath);
        Assert.Contains("private static List<RelationshipConfiguration> BuildRelationshipBuilderLocalMap", relationshipLocalsSource);
        Assert.Contains("private static bool TryResolveRelationshipBuilderLocal", relationshipLocalsSource);
    }

    [Fact]
    public void LC027_RelationshipBuilderLocalScope_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC027_MissingExplicitForeignKey");
        var relationshipLocalsPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyRelationshipLocals.cs");
        var relationshipLocalScopePath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyRelationshipLocalScope.cs");
        var relationshipLocalShadowingPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyRelationshipLocalShadowing.cs");

        Assert.True(File.Exists(relationshipLocalScopePath), "LC027 relationship-builder local assignment/write-target logic should live in a focused partial file.");
        Assert.True(File.Exists(relationshipLocalShadowingPath), "LC027 relationship-builder local shadowing logic should live in a focused partial file.");

        var relationshipLocalsSource = File.ReadAllText(relationshipLocalsPath);
        Assert.DoesNotContain("private static bool HasSingleLocalAssignment", relationshipLocalsSource);
        Assert.DoesNotContain("private static bool IsShadowedByNestedLocal", relationshipLocalsSource);
        Assert.DoesNotContain("private static SyntaxNode? FindDesignationScope", relationshipLocalsSource);
        Assert.DoesNotContain("private static bool IsVisibleAt", relationshipLocalsSource);

        var relationshipLocalScopeSource = File.ReadAllText(relationshipLocalScopePath);
        Assert.Contains("private static bool HasSingleLocalAssignment", relationshipLocalScopeSource);
        Assert.DoesNotContain("private static bool IsShadowedByNestedLocal", relationshipLocalScopeSource);
        Assert.DoesNotContain("private static SyntaxNode? FindDesignationScope", relationshipLocalScopeSource);
        Assert.Contains("private static bool IsVisibleAt", relationshipLocalScopeSource);

        var relationshipLocalShadowingSource = File.ReadAllText(relationshipLocalShadowingPath);
        Assert.Contains("private static bool IsShadowedByNestedLocal", relationshipLocalShadowingSource);
        Assert.Contains("private static SyntaxNode? FindDesignationScope", relationshipLocalShadowingSource);
    }

    [Fact]
    public void LC027_ConfigurationNameExtraction_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC027_MissingExplicitForeignKey");
        var configurationPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyConfigurationAnalysis.cs");
        var nameExtractionPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyConfigurationNameExtraction.cs");

        Assert.True(File.Exists(nameExtractionPath), "LC027 configuration navigation/entity/owned-type extraction should live in a focused partial file.");

        var configurationSource = File.ReadAllText(configurationPath);
        Assert.Contains("public sealed partial class MissingExplicitForeignKeyAnalyzer", configurationSource);
        Assert.DoesNotContain("private static string? ExtractNavigationNameFromChain", configurationSource);
        Assert.DoesNotContain("private static string? ExtractEntityTypeNameFromChain", configurationSource);
        Assert.DoesNotContain("private static INamedTypeSymbol? ResolveOwnedTypeFromConfiguration", configurationSource);

        var nameExtractionSource = File.ReadAllText(nameExtractionPath);
        Assert.Contains("private static string? ExtractNavigationNameFromChain", nameExtractionSource);
        Assert.Contains("private static string? ExtractEntityTypeNameFromChain", nameExtractionSource);
        Assert.Contains("private static INamedTypeSymbol? ResolveOwnedTypeFromConfiguration", nameExtractionSource);
    }

    [Fact]
    public void LC032_SaveChangesRewriteMode_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var fixerPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixer.cs");
        var saveChangesModePath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixerSaveChangesMode.cs");
        var asyncSupportPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixerAsyncSupport.cs");

        Assert.True(File.Exists(saveChangesModePath), "LC032 SaveChanges/async rewrite-mode logic should live in a focused partial file.");
        Assert.True(File.Exists(asyncSupportPath), "LC032 ExecuteUpdateAsync capability detection should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class ExecuteUpdateForBulkUpdatesFixer", fixerSource);
        Assert.DoesNotContain("private static bool TryClassifyTrailingSaveChanges", fixerSource);
        Assert.DoesNotContain("private static bool HasExecuteUpdateAsyncTokenOverload", fixerSource);

        var saveChangesModeSource = File.ReadAllText(saveChangesModePath);
        Assert.Contains("private static bool TryClassifyTrailingSaveChanges", saveChangesModeSource);
        Assert.DoesNotContain("private static bool HasExecuteUpdateAsyncTokenOverload", saveChangesModeSource);

        var asyncSupportSource = File.ReadAllText(asyncSupportPath);
        Assert.Contains("private static bool HasExecuteUpdateAsyncTokenOverload", asyncSupportSource);
    }

    [Fact]
    public void LC032_FixerRewriteApplication_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var fixerPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixer.cs");
        var rewriteApplicationPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixerRewriteApplication.cs");

        Assert.True(File.Exists(rewriteApplicationPath), "LC032 ExecuteUpdate statement generation and document rewrite should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class ExecuteUpdateForBulkUpdatesFixer", fixerSource);
        Assert.DoesNotContain("private const string WarningComment", fixerSource);
        Assert.DoesNotContain("private static async Task<Document> ApplyFixAsync", fixerSource);

        var rewriteApplicationSource = File.ReadAllText(rewriteApplicationPath);
        Assert.Contains("private const string WarningComment", rewriteApplicationSource);
        Assert.Contains("private static async Task<Document> ApplyFixAsync", rewriteApplicationSource);
        Assert.Contains("DocumentEditor.CreateAsync", rewriteApplicationSource);
        Assert.Contains("SyntaxFactory.ParseStatement", rewriteApplicationSource);
    }

    [Fact]
    public void LC032_SetterExtraction_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var fixerPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixer.cs");
        var setterExtractionPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixerSetters.cs");

        Assert.True(File.Exists(setterExtractionPath), "LC032 setter extraction and duplicate-target safety should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class ExecuteUpdateForBulkUpdatesFixer", fixerSource);
        Assert.DoesNotContain("private static bool TryGetSetters", fixerSource);
        Assert.DoesNotContain("private static bool ReadsAnyProperty", fixerSource);

        var setterExtractionSource = File.ReadAllText(setterExtractionPath);
        Assert.Contains("private static bool TryGetSetters", setterExtractionSource);
        Assert.Contains("private static bool ReadsAnyProperty", setterExtractionSource);
    }

    [Fact]
    public void LC032_ReceiverNormalization_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var fixerPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixer.cs");
        var receiverPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixerReceiver.cs");

        Assert.True(File.Exists(receiverPath), "LC032 receiver materializer stripping and unsupported-step detection should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class ExecuteUpdateForBulkUpdatesFixer", fixerSource);
        Assert.DoesNotContain("UnsupportedExecuteUpdateReceiverSteps", fixerSource);
        Assert.DoesNotContain("private static ExpressionSyntax StripCollectionMaterializer", fixerSource);
        Assert.DoesNotContain("private static bool HasUnsupportedExecuteUpdateReceiverStep", fixerSource);
        Assert.DoesNotContain("private static bool IsStaticLinqTypeExpression", fixerSource);

        var receiverSource = File.ReadAllText(receiverPath);
        Assert.Contains("UnsupportedExecuteUpdateReceiverSteps", receiverSource);
        Assert.Contains("private static ExpressionSyntax StripCollectionMaterializer", receiverSource);
        Assert.Contains("private static bool HasUnsupportedExecuteUpdateReceiverStep", receiverSource);
        Assert.Contains("private static bool IsStaticLinqTypeExpression", receiverSource);
    }

    [Fact]
    public void LC012_RewriteSafetyAnalysis_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC012_OptimizeRemoveRange");
        var fixerPath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixer.cs");
        var rewriteSafetyPath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixerRewriteSafety.cs");
        var querySourcePath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixerQuerySourceContext.cs");

        Assert.True(File.Exists(rewriteSafetyPath), "LC012 rewrite-safety analysis should live in a focused partial file.");
        Assert.True(File.Exists(querySourcePath), "LC012 query-source context resolution should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class OptimizeRemoveRangeFixer", fixerSource);
        Assert.DoesNotContain("private static async Task<bool> CanSafelyRewriteAsync", fixerSource);
        Assert.DoesNotContain("private static bool TryResolveQuerySourceFreshContextLocal", fixerSource);

        var rewriteSafetySource = File.ReadAllText(rewriteSafetyPath);
        Assert.Contains("private static async Task<bool> CanSafelyRewriteAsync", rewriteSafetySource);
        Assert.DoesNotContain("private static bool TryResolveQuerySourceFreshContextLocal", rewriteSafetySource);

        var querySource = File.ReadAllText(querySourcePath);
        Assert.Contains("private static bool TryResolveQuerySourceFreshContextLocal", querySource);
    }

    [Fact]
    public void LC012_RewriteModeSelection_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC012_OptimizeRemoveRange");
        var rewriteSafetyPath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixerRewriteSafety.cs");
        var rewriteModePath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixerRewriteMode.cs");

        Assert.True(File.Exists(rewriteModePath), "LC012 async/sync ExecuteDelete rewrite mode selection should live in a focused partial file.");

        var rewriteSafetySource = File.ReadAllText(rewriteSafetyPath);
        Assert.DoesNotContain("private static RewriteMode DetermineRewriteMode", rewriteSafetySource);
        Assert.DoesNotContain("private static bool IsAsyncContext", rewriteSafetySource);
        Assert.DoesNotContain("private static bool HasExecuteDeleteAsyncSupport", rewriteSafetySource);

        var rewriteModeSource = File.ReadAllText(rewriteModePath);
        Assert.Contains("private static RewriteMode DetermineRewriteMode", rewriteModeSource);
        Assert.Contains("private static bool IsAsyncContext", rewriteModeSource);
        Assert.Contains("private static bool HasExecuteDeleteAsyncSupport", rewriteModeSource);
    }

    [Fact]
    public void LC012_SaveChangesSafetyAnalysis_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC012_OptimizeRemoveRange");
        var rewriteSafetyPath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixerRewriteSafety.cs");
        var saveChangesSafetyPath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixerSaveChangesSafety.cs");

        Assert.True(File.Exists(saveChangesSafetyPath), "LC012 SaveChanges detection safety should live in a focused partial file.");

        var rewriteSafetySource = File.ReadAllText(rewriteSafetyPath);
        Assert.DoesNotContain("private static bool HasSubsequentSaveChangesInvocation", rewriteSafetySource);
        Assert.DoesNotContain("private static bool AreMutuallyExclusiveBranches", rewriteSafetySource);

        var saveChangesSafetySource = File.ReadAllText(saveChangesSafetyPath);
        Assert.Contains("private static bool HasSubsequentSaveChangesInvocation", saveChangesSafetySource);
        Assert.DoesNotContain("private static bool AreMutuallyExclusiveBranches", saveChangesSafetySource);
    }

    [Fact]
    public void LC012_AnalyzerSaveChangesSafety_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC012_OptimizeRemoveRange");
        var analyzerPath = Path.Combine(analyzerDir, "OptimizeRemoveRangeAnalyzer.cs");
        var saveChangesSafetyPath = Path.Combine(analyzerDir, "OptimizeRemoveRangeAnalyzerSaveChangesSafety.cs");

        Assert.True(File.Exists(saveChangesSafetyPath), "LC012 analyzer SaveChanges detection and context-aliasing safety should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class OptimizeRemoveRangeAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool HasSubsequentSaveChangesInvocation", analyzerSource);
        Assert.DoesNotContain("private static bool TryResolveFreshContextLocal", analyzerSource);

        var saveChangesSafetySource = File.ReadAllText(saveChangesSafetyPath);
        Assert.Contains("private static bool HasSubsequentSaveChangesInvocation", saveChangesSafetySource);
        Assert.Contains("private static bool TryResolveFreshContextLocal", saveChangesSafetySource);
    }

    [Fact]
    public void LC012_AnalyzerBranchExclusion_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC012_OptimizeRemoveRange");
        var saveChangesSafetyPath = Path.Combine(analyzerDir, "OptimizeRemoveRangeAnalyzerSaveChangesSafety.cs");
        var branchExclusionPath = Path.Combine(analyzerDir, "OptimizeRemoveRangeAnalyzerBranchExclusion.cs");

        Assert.True(File.Exists(branchExclusionPath), "LC012 analyzer if/switch mutual-exclusion checks should live in a focused partial file.");

        var saveChangesSafetySource = File.ReadAllText(saveChangesSafetyPath);
        Assert.Contains("private static bool HasSubsequentSaveChangesInvocation", saveChangesSafetySource);
        Assert.DoesNotContain("private static bool AreMutuallyExclusiveBranches", saveChangesSafetySource);
        Assert.DoesNotContain("private static SyntaxNode? GetContainingIfBranch", saveChangesSafetySource);
        Assert.DoesNotContain("private static SwitchSectionSyntax? GetContainingSwitchSection", saveChangesSafetySource);

        var branchExclusionSource = File.ReadAllText(branchExclusionPath);
        Assert.Contains("private static bool AreMutuallyExclusiveBranches", branchExclusionSource);
        Assert.Contains("private static SyntaxNode? GetContainingIfBranch", branchExclusionSource);
        Assert.Contains("private static SwitchSectionSyntax? GetContainingSwitchSection", branchExclusionSource);
        Assert.DoesNotContain("private static bool HasSubsequentSaveChangesInvocation", branchExclusionSource);
    }

    [Fact]
    public void LC013_AssignedOriginResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC013_DisposedContextQuery");
        var originPath = Path.Combine(analyzerDir, "DisposedContextQueryOriginResolution.cs");
        var assignedOriginPath = Path.Combine(analyzerDir, "DisposedContextQueryAssignedOriginResolution.cs");

        Assert.True(File.Exists(assignedOriginPath), "LC013 assigned local origin resolution should live in a focused partial file.");

        var originSource = File.ReadAllText(originPath);
        Assert.Contains("public sealed partial class DisposedContextQueryAnalyzer", originSource);
        Assert.DoesNotContain("private static bool TryResolveAssignedDisposedContextOrigin", originSource);
        Assert.DoesNotContain("private static bool TryGetSharedAssignedDisposedContextOrigin", originSource);
        Assert.DoesNotContain("private static List<IOperation> GetAssignedValues", originSource);
        Assert.DoesNotContain("private static IEnumerable<IOperation> EnumerateOperations", originSource);

        var assignedOriginSource = File.ReadAllText(assignedOriginPath);
        Assert.Contains("private static bool TryResolveAssignedDisposedContextOrigin", assignedOriginSource);
        Assert.Contains("private static bool TryGetSharedAssignedDisposedContextOrigin", assignedOriginSource);
        Assert.DoesNotContain("private static List<IOperation> GetAssignedValues", assignedOriginSource);
        Assert.DoesNotContain("private static IEnumerable<IOperation> EnumerateOperations", assignedOriginSource);
    }

    [Fact]
    public void LC016_ExpressionBodyFixer_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC016_AvoidDateTimeNow");
        var fixerPath = Path.Combine(fixerDir, "AvoidDateTimeNowFixer.cs");
        var expressionBodyPath = Path.Combine(fixerDir, "AvoidDateTimeNowFixerExpressionBody.cs");

        Assert.True(File.Exists(expressionBodyPath), "LC016 expression-bodied member rewrite logic should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class AvoidDateTimeNowFixer", fixerSource);
        Assert.DoesNotContain("private static Document ConvertExpressionBodiedMember", fixerSource);
        Assert.DoesNotContain("private static IReadOnlyList<ClockReplacement> BuildExpressionBodyReplacements", fixerSource);

        var expressionBodySource = File.ReadAllText(expressionBodyPath);
        Assert.Contains("private static Document ConvertExpressionBodiedMember", expressionBodySource);
        Assert.DoesNotContain("private static IReadOnlyList<ClockReplacement> BuildExpressionBodyReplacements", expressionBodySource);
    }

    [Fact]
    public void LC016_ExpressionBodyStatements_LiveInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC016_AvoidDateTimeNow");
        var expressionBodyPath = Path.Combine(fixerDir, "AvoidDateTimeNowFixerExpressionBody.cs");
        var statementPath = Path.Combine(fixerDir, "AvoidDateTimeNowFixerExpressionBodyStatements.cs");

        Assert.True(File.Exists(statementPath), "LC016 expression-bodied return/expression-statement selection should live in a focused partial file.");

        var expressionBodySource = File.ReadAllText(expressionBodyPath);
        Assert.DoesNotContain("private static StatementSyntax CreateExpressionBodyStatement", expressionBodySource);
        Assert.DoesNotContain("private static bool RequiresExpressionStatement", expressionBodySource);
        Assert.DoesNotContain("private static bool IsNonGenericTaskLike", expressionBodySource);

        var statementSource = File.ReadAllText(statementPath);
        Assert.Contains("private static StatementSyntax CreateExpressionBodyStatement", statementSource);
        Assert.Contains("private static bool RequiresExpressionStatement", statementSource);
        Assert.Contains("private static bool IsNonGenericTaskLike", statementSource);
    }

    [Fact]
    public void RuleCatalog_LC016ToLC030Entries_LiveInDedicatedPartial()
    {
        var catalogDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Catalog");
        var catalogPath = Path.Combine(catalogDir, "RuleCatalog.cs");
        var lc016ToLc030Path = Path.Combine(catalogDir, "RuleCatalog.LC016ToLC030.cs");

        Assert.True(File.Exists(lc016ToLc030Path), "LC016-LC030 catalog entries should live in a focused partial file.");

        var catalogSource = File.ReadAllText(catalogPath);
        Assert.Contains("public static partial class RuleCatalog", catalogSource);
        Assert.DoesNotContain("id: \"LC016\"", catalogSource);
        Assert.DoesNotContain("id: \"LC030\"", catalogSource);

        var lc016ToLc030Source = File.ReadAllText(lc016ToLc030Path);
        Assert.Contains("private static ImmutableArray<RuleCatalogEntry> CreateLC016ToLC030Entries", lc016ToLc030Source);
        Assert.Contains("id: \"LC016\"", lc016ToLc030Source);
        Assert.Contains("id: \"LC030\"", lc016ToLc030Source);
    }

    [Fact]
    public void RuleCatalog_LC031ToLC045Entries_LiveInDedicatedPartial()
    {
        var catalogDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Catalog");
        var catalogPath = Path.Combine(catalogDir, "RuleCatalog.cs");
        var lc031ToLc045Path = Path.Combine(catalogDir, "RuleCatalog.LC031ToLC045.cs");

        Assert.True(File.Exists(lc031ToLc045Path), "LC031-LC045 catalog entries should live in a focused partial file.");

        var catalogSource = File.ReadAllText(catalogPath);
        Assert.Contains("public static partial class RuleCatalog", catalogSource);
        Assert.DoesNotContain("id: \"LC031\"", catalogSource);
        Assert.DoesNotContain("id: \"LC045\"", catalogSource);

        var lc031ToLc045Source = File.ReadAllText(lc031ToLc045Path);
        Assert.Contains("private static ImmutableArray<RuleCatalogEntry> CreateLC031ToLC045Entries", lc031ToLc045Source);
        Assert.Contains("id: \"LC031\"", lc031ToLc045Source);
        Assert.Contains("id: \"LC045\"", lc031ToLc045Source);
    }

    [Fact]
    public void LC044_ReachabilityAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var analyzerPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyAnalyzer.cs");
        var reachabilityPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyReachability.cs");
        var terminatorsPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyReachabilityTerminators.cs");

        Assert.True(File.Exists(reachabilityPath), "LC044 reachability/control-flow analysis should live in a focused partial file.");
        Assert.True(File.Exists(terminatorsPath), "LC044 reachability terminator and branch-blocking rules should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AsNoTrackingThenModifyAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool BlockReaches", analyzerSource);
        Assert.DoesNotContain("private static bool HasTerminatorBetween", analyzerSource);

        var reachabilitySource = File.ReadAllText(reachabilityPath);
        Assert.Contains("private static bool BlockReaches", reachabilitySource);
        Assert.DoesNotContain("private static bool HasTerminatorBetween", reachabilitySource);

        var terminatorsSource = File.ReadAllText(terminatorsPath);
        Assert.Contains("private static bool HasTerminatorBetween", terminatorsSource);
        Assert.Contains("private static bool IsBreakBlocking", terminatorsSource);
        Assert.Contains("private static bool IsContinueBlocking", terminatorsSource);
    }

    [Fact]
    public void LC044_TrackingStateAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var analyzerPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyAnalyzer.cs");
        var trackingStatePath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyTrackingState.cs");

        Assert.True(File.Exists(trackingStatePath), "LC044 reattach/save-state analysis should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AsNoTrackingThenModifyAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool HasDominatingPriorReattach", analyzerSource);
        Assert.DoesNotContain("private static bool HasInterveningDetach", analyzerSource);

        var trackingStateSource = File.ReadAllText(trackingStatePath);
        Assert.Contains("private static bool HasDominatingPriorReattach", trackingStateSource);
        Assert.DoesNotContain("private static bool HasInterveningDetach", trackingStateSource);
    }

    [Fact]
    public void LC044_RootScanTrackingStateParsing_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var rootScanPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyRootScan.cs");
        var parsingPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyRootScanTrackingState.cs");

        Assert.True(File.Exists(parsingPath), "LC044 root-scan reattach/detach parsing should live in a focused partial file.");

        var rootScanSource = File.ReadAllText(rootScanPath);
        Assert.Contains("internal sealed partial class AsNoTrackingThenModifyRootScan", rootScanSource);
        Assert.DoesNotContain("private static bool TryParseReattachInvocation", rootScanSource);
        Assert.DoesNotContain("private static bool TryParseEntryStateAssignment", rootScanSource);

        var parsingSource = File.ReadAllText(parsingPath);
        Assert.Contains("private static bool TryParseReattachInvocation", parsingSource);
        Assert.Contains("private static bool TryParseEntryStateAssignment", parsingSource);
    }

    [Fact]
    public void LC044_RootScanBuckets_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var rootScanPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyRootScan.cs");
        var bucketsPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyRootScanBuckets.cs");

        Assert.True(File.Exists(bucketsPath), "LC044 root-scan bucket/index helpers should live in a focused partial file.");

        var rootScanSource = File.ReadAllText(rootScanPath);
        Assert.DoesNotContain("internal static bool TryGetSymbol", rootScanSource);
        Assert.DoesNotContain("private static void AddMutation", rootScanSource);
        Assert.DoesNotContain("private static void AddReattach", rootScanSource);
        Assert.DoesNotContain("private static void AddDetach", rootScanSource);

        var bucketsSource = File.ReadAllText(bucketsPath);
        Assert.Contains("internal static bool TryGetSymbol", bucketsSource);
        Assert.Contains("private static void AddMutation", bucketsSource);
        Assert.Contains("private static void AddReattach", bucketsSource);
        Assert.Contains("private static void AddDetach", bucketsSource);
        Assert.Contains("private static void AddToBucket", bucketsSource);
    }

    [Fact]
    public void LC045_ConditionalAccessAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var usagePath = Path.Combine(analyzerDir, "MissingIncludeUsageAnalysis.cs");
        var conditionalAccessPath = Path.Combine(analyzerDir, "MissingIncludeConditionalAccessAnalysis.cs");

        Assert.True(File.Exists(conditionalAccessPath), "LC045 conditional-access path analysis should live in a focused partial file.");

        var usageSource = File.ReadAllText(usagePath);
        Assert.DoesNotContain("private static IOperation? FindConditionalAccessEntryProperty", usageSource);
        Assert.DoesNotContain("private static IOperation? ResolveConditionalAccessReceiver", usageSource);

        var conditionalAccessSource = File.ReadAllText(conditionalAccessPath);
        Assert.Contains("private static IOperation? FindConditionalAccessEntryProperty", conditionalAccessSource);
        Assert.Contains("private static IOperation? ResolveConditionalAccessReceiver", conditionalAccessSource);
    }

    [Fact]
    public void LC045_NavigationPathResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var usagePath = Path.Combine(analyzerDir, "MissingIncludeUsageAnalysis.cs");
        var pathResolutionPath = Path.Combine(analyzerDir, "MissingIncludeNavigationPathAnalysis.cs");

        Assert.True(File.Exists(pathResolutionPath), "LC045 navigation-path resolution should live in a focused partial file.");

        var usageSource = File.ReadAllText(usagePath);
        Assert.DoesNotContain("private static bool TryGetAccessPath", usageSource);
        Assert.DoesNotContain("private static bool TryResolveNavigationTargetForPath", usageSource);

        var pathResolutionSource = File.ReadAllText(pathResolutionPath);
        Assert.Contains("private static bool TryGetAccessPath", pathResolutionSource);
        Assert.Contains("private static bool TryResolveNavigationTargetForPath", pathResolutionSource);
    }

    [Fact]
    public void LC037_LocalWriteReachability_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var detectionPath = Path.Combine(analyzerDir, "RawSqlStringConstructionDetection.cs");
        var reachabilityPath = Path.Combine(analyzerDir, "RawSqlStringConstructionReachability.cs");

        Assert.True(File.Exists(reachabilityPath), "LC037 local-write reachability analysis should live in a focused partial file.");

        var detectionSource = File.ReadAllText(detectionPath);
        Assert.DoesNotContain("private static bool CanWriteReachLaterLoopIteration", detectionSource);
        Assert.DoesNotContain("private static bool CanOperationReachReference", detectionSource);

        var reachabilitySource = File.ReadAllText(reachabilityPath);
        Assert.DoesNotContain("private static bool CanWriteReachLaterLoopIteration", reachabilitySource);
        Assert.Contains("private static bool CanOperationReachReference", reachabilitySource);
    }

    [Fact]
    public void LC037_ExceptionFlowReachability_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var reachabilityPath = Path.Combine(analyzerDir, "RawSqlStringConstructionReachability.cs");
        var exceptionFlowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionExceptionFlow.cs");

        Assert.True(File.Exists(exceptionFlowPath), "LC037 throw/catch reachability analysis should live in a focused partial file.");

        var reachabilitySource = File.ReadAllText(reachabilityPath);
        Assert.DoesNotContain("private static bool ThrowCanContinueThroughCatch", reachabilitySource);
        Assert.DoesNotContain("private static bool IsKnownExceptionBase", reachabilitySource);

        var exceptionFlowSource = File.ReadAllText(exceptionFlowPath);
        Assert.Contains("private static bool ThrowCanContinueThroughCatch", exceptionFlowSource);
    }

    [Fact]
    public void LC037_ExceptionTypeResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var exceptionFlowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionExceptionFlow.cs");
        var exceptionTypesPath = Path.Combine(analyzerDir, "RawSqlStringConstructionExceptionTypes.cs");

        Assert.True(File.Exists(exceptionTypesPath), "LC037 exception type-name and hierarchy matching should live in a focused partial file.");

        var exceptionFlowSource = File.ReadAllText(exceptionFlowPath);
        Assert.DoesNotContain("private static string? GetSimpleTypeName", exceptionFlowSource);
        Assert.DoesNotContain("private static string? GetResolvedSimpleTypeName", exceptionFlowSource);
        Assert.DoesNotContain("private static bool HasLocalExceptionBase", exceptionFlowSource);
        Assert.DoesNotContain("private static bool IsKnownExceptionBase", exceptionFlowSource);

        var exceptionTypesSource = File.ReadAllText(exceptionTypesPath);
        Assert.Contains("private static string? GetSimpleTypeName", exceptionTypesSource);
        Assert.Contains("private static string? GetResolvedSimpleTypeName", exceptionTypesSource);
        Assert.Contains("private static bool HasLocalExceptionBase", exceptionTypesSource);
        Assert.Contains("private static bool IsKnownExceptionBase", exceptionTypesSource);
    }

    [Fact]
    public void LC037_TerminationFlowReachability_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var reachabilityPath = Path.Combine(analyzerDir, "RawSqlStringConstructionReachability.cs");
        var terminationFlowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionTerminationFlow.cs");

        Assert.True(File.Exists(terminationFlowPath), "LC037 branch, jump, and terminator reachability should live in a focused partial file.");

        var reachabilitySource = File.ReadAllText(reachabilityPath);
        Assert.DoesNotContain("private static bool BlockTerminatesAfterNode", reachabilitySource);
        Assert.DoesNotContain("private static bool StatementTerminates", reachabilitySource);

        var terminationFlowSource = File.ReadAllText(terminationFlowPath);
        Assert.Contains("private static bool BlockTerminatesAfterNode", terminationFlowSource);
        Assert.Contains("private static bool StatementTerminates", terminationFlowSource);
    }

    [Fact]
    public void LC037_StringBuilderFlowTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC037_RawSqlStringConstruction");
        var generalTestsPath = Path.Combine(testDir, "RawSqlStringConstructionTests.cs");
        var stringBuilderTestsPath = Path.Combine(testDir, "RawSqlStringConstructionStringBuilderTests.cs");

        Assert.True(File.Exists(stringBuilderTestsPath), "LC037 StringBuilder flow tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class RawSqlStringConstructionTests", generalTestsSource);
        Assert.DoesNotContain("ExecuteSqlRaw_WithStringBuilderAliasAndLaterAppend_ShouldTrigger", generalTestsSource);
        Assert.DoesNotContain("ExecuteSqlRaw_WithStringBuilderInitializerAppendsClearedBeforeCall_ShouldNotTrigger", generalTestsSource);

        var stringBuilderTestsSource = File.ReadAllText(stringBuilderTestsPath);
        Assert.Contains("public partial class RawSqlStringConstructionTests", stringBuilderTestsSource);
        Assert.Contains("ExecuteSqlRaw_WithStringBuilderAliasAndLaterAppend_ShouldTrigger", stringBuilderTestsSource);
        Assert.Contains("ExecuteSqlRaw_WithStringBuilderInitializerAppendsClearedBeforeCall_ShouldNotTrigger", stringBuilderTestsSource);
    }

    [Fact]
    public void LC037_StringBuilderReachabilityTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC037_RawSqlStringConstruction");
        var generalTestsPath = Path.Combine(testDir, "RawSqlStringConstructionTests.cs");
        var reachabilityTestsPath = Path.Combine(testDir, "RawSqlStringConstructionStringBuilderReachabilityTests.cs");

        Assert.True(File.Exists(reachabilityTestsPath), "LC037 StringBuilder reachability tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.DoesNotContain("ExecuteSqlRaw_WithStringBuilderAppendBeforeCaughtThrow_ShouldTrigger", generalTestsSource);
        Assert.DoesNotContain("ExecuteSqlRaw_WithStringBuilderConstructorTaintClearedInFluentChain_ShouldNotTrigger", generalTestsSource);

        var reachabilityTestsSource = File.ReadAllText(reachabilityTestsPath);
        Assert.Contains("public partial class RawSqlStringConstructionTests", reachabilityTestsSource);
        Assert.Contains("ExecuteSqlRaw_WithStringBuilderAppendBeforeCaughtThrow_ShouldTrigger", reachabilityTestsSource);
        Assert.Contains("ExecuteSqlRaw_WithStringBuilderConstructorTaintClearedInFluentChain_ShouldNotTrigger", reachabilityTestsSource);
    }

    [Fact]
    public void LC037_LocalWriteFlowTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC037_RawSqlStringConstruction");
        var generalTestsPath = Path.Combine(testDir, "RawSqlStringConstructionTests.cs");
        var localWriteTestsPath = Path.Combine(testDir, "RawSqlStringConstructionLocalWriteTests.cs");

        Assert.True(File.Exists(localWriteTestsPath), "LC037 local-write flow tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.DoesNotContain("ExecuteSqlRaw_WithConstructedInitialValueOverwrittenByConstantBeforeCall_ShouldNotTrigger", generalTestsSource);
        Assert.DoesNotContain("ExecuteSqlRaw_WithConditionalConstructedSqlOverwrittenByConstantBeforeCall_ShouldNotTrigger", generalTestsSource);

        var localWriteTestsSource = File.ReadAllText(localWriteTestsPath);
        Assert.Contains("public partial class RawSqlStringConstructionTests", localWriteTestsSource);
        Assert.Contains("ExecuteSqlRaw_WithConstructedInitialValueOverwrittenByConstantBeforeCall_ShouldNotTrigger", localWriteTestsSource);
        Assert.Contains("ExecuteSqlRaw_WithConditionalConstructedSqlOverwrittenByConstantBeforeCall_ShouldNotTrigger", localWriteTestsSource);
    }

    [Fact]
    public void LC018_ProviderVariantTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC018_AvoidFromSqlRawWithInterpolation");
        var generalTestsPath = Path.Combine(testDir, "AvoidFromSqlRawWithInterpolationTests.cs");
        var providerTestsPath = Path.Combine(testDir, "AvoidFromSqlRawWithInterpolationProviderVariantTests.cs");

        Assert.True(File.Exists(providerTestsPath), "LC018 provider-variant and SqlQueryRaw coverage should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class AvoidFromSqlRawWithInterpolationTests", generalTestsSource);
        Assert.DoesNotContain("FromSqlRaw_OnDbSet_WithUnsafeInterpolation_ShouldTrigger", generalTestsSource);
        Assert.DoesNotContain("SqlQueryRaw_WithInterpolatedString_ShouldTriggerLC018", generalTestsSource);

        var providerTestsSource = File.ReadAllText(providerTestsPath);
        Assert.Contains("public partial class AvoidFromSqlRawWithInterpolationTests", providerTestsSource);
        Assert.Contains("FromSqlRaw_OnDbSet_WithUnsafeInterpolation_ShouldTrigger", providerTestsSource);
        Assert.Contains("SqlQueryRaw_WithInterpolatedString_ShouldTriggerLC018", providerTestsSource);
    }

    [Fact]
    public void LC032_FixerSafetyTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC032_ExecuteUpdateForBulkUpdates");
        var fixerTestsPath = Path.Combine(testDir, "ExecuteUpdateForBulkUpdatesFixerTests.cs");
        var safetyTestsPath = Path.Combine(testDir, "ExecuteUpdateForBulkUpdatesFixerSafetyTests.cs");

        Assert.True(File.Exists(safetyTestsPath), "LC032 fixer safety and decline cases should live in a focused partial test file.");

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class ExecuteUpdateForBulkUpdatesFixerTests", fixerTestsSource);
        Assert.DoesNotContain("Fixer_DuplicateProperty_LaterReadsEarlierWrite_DoesNotRegister", fixerTestsSource);
        Assert.DoesNotContain("Fixer_SyncLocalFunctionInsideAsyncMethod_UsesSyncExecuteUpdate", fixerTestsSource);

        var safetyTestsSource = File.ReadAllText(safetyTestsPath);
        Assert.Contains("public partial class ExecuteUpdateForBulkUpdatesFixerTests", safetyTestsSource);
        Assert.Contains("Fixer_DuplicateProperty_LaterReadsEarlierWrite_DoesNotRegister", safetyTestsSource);
        Assert.Contains("Fixer_SyncLocalFunctionInsideAsyncMethod_UsesSyncExecuteUpdate", safetyTestsSource);
    }

    [Fact]
    public void LC032_FixerQuerySourceTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC032_ExecuteUpdateForBulkUpdates");
        var fixerTestsPath = Path.Combine(testDir, "ExecuteUpdateForBulkUpdatesFixerTests.cs");
        var querySourceTestsPath = Path.Combine(testDir, "ExecuteUpdateForBulkUpdatesFixerQuerySourceTests.cs");

        Assert.True(File.Exists(querySourceTestsPath), "LC032 query-source and materializer fixer coverage should live in a focused partial test file.");

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class ExecuteUpdateForBulkUpdatesFixerTests", fixerTestsSource);
        Assert.DoesNotContain("Fixer_DbContextSetSource_Rewrites", fixerTestsSource);
        Assert.DoesNotContain("Fixer_AwaitedInlineToListAsync_StripsMaterializer", fixerTestsSource);

        var querySourceTestsSource = File.ReadAllText(querySourceTestsPath);
        Assert.Contains("public partial class ExecuteUpdateForBulkUpdatesFixerTests", querySourceTestsSource);
        Assert.Contains("Fixer_DbContextSetSource_Rewrites", querySourceTestsSource);
        Assert.Contains("Fixer_AwaitedInlineToListAsync_StripsMaterializer", querySourceTestsSource);
    }

    [Fact]
    public void LC035_ConditionalFlowTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC035_MissingWhereBeforeExecuteDeleteUpdate");
        var generalTestsPath = Path.Combine(testDir, "MissingWhereBeforeExecuteDeleteUpdateTests.cs");
        var conditionalFlowTestsPath = Path.Combine(testDir, "MissingWhereBeforeExecuteDeleteUpdateConditionalFlowTests.cs");

        Assert.True(File.Exists(conditionalFlowTestsPath), "LC035 conditional-flow and reassignment tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class MissingWhereBeforeExecuteDeleteUpdateTests", generalTestsSource);
        Assert.DoesNotContain("ExecuteDelete_UnconditionalFilterThenOptionalNarrowing_ShouldNotTrigger", generalTestsSource);
        Assert.DoesNotContain("ExecuteUpdate_CatchPathReassignsToUnfilteredQuery_ShouldTrigger", generalTestsSource);

        var conditionalFlowTestsSource = File.ReadAllText(conditionalFlowTestsPath);
        Assert.Contains("public partial class MissingWhereBeforeExecuteDeleteUpdateTests", conditionalFlowTestsSource);
        Assert.Contains("ExecuteDelete_UnconditionalFilterThenOptionalNarrowing_ShouldNotTrigger", conditionalFlowTestsSource);
        Assert.Contains("ExecuteUpdate_CatchPathReassignsToUnfilteredQuery_ShouldTrigger", conditionalFlowTestsSource);
    }

    [Fact]
    public void LC001_StaticQueryableFixerTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC001_LocalMethod");
        var fixerTestsPath = Path.Combine(testDir, "LocalMethodFixerTests.cs");
        var staticQueryableTestsPath = Path.Combine(testDir, "LocalMethodFixerStaticQueryableTests.cs");

        Assert.True(File.Exists(staticQueryableTestsPath), "LC001 static Queryable fixer coverage should live in a focused partial test file.");

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class LocalMethodFixerTests", fixerTestsSource);
        Assert.DoesNotContain("FixCrime_StaticQueryableWhere_SwitchesToEnumerable", fixerTestsSource);
        Assert.DoesNotContain("FixCrime_StaticQueryableThenBy_RewritesExtensionOrderedSourceChain", fixerTestsSource);

        var staticQueryableTestsSource = File.ReadAllText(staticQueryableTestsPath);
        Assert.Contains("public partial class LocalMethodFixerTests", staticQueryableTestsSource);
        Assert.Contains("FixCrime_StaticQueryableWhere_SwitchesToEnumerable", staticQueryableTestsSource);
        Assert.Contains("FixCrime_StaticQueryableThenBy_RewritesExtensionOrderedSourceChain", staticQueryableTestsSource);
    }

    [Fact]
    public void LC001_StaticQueryableRewrite_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC001_LocalMethod");
        var fixerPath = Path.Combine(fixerDir, "LocalMethodFixer.cs");
        var staticQueryableRewritePath = Path.Combine(fixerDir, "LocalMethodFixerStaticQueryableRewrite.cs");

        Assert.True(File.Exists(staticQueryableRewritePath), "LC001 static Queryable rewrite logic should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class LocalMethodFixer", fixerSource);
        Assert.DoesNotContain("private static bool RewriteStaticQueryableInvocation", fixerSource);
        Assert.DoesNotContain("private static bool IsSystemLinqQueryableType", fixerSource);

        var staticQueryableRewriteSource = File.ReadAllText(staticQueryableRewritePath);
        Assert.Contains("private static bool RewriteStaticQueryableInvocation", staticQueryableRewriteSource);
        Assert.DoesNotContain("private static bool IsSystemLinqQueryableType", staticQueryableRewriteSource);
    }

    [Fact]
    public void LC001_StaticQueryableQualifiers_LiveInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC001_LocalMethod");
        var staticQueryableRewritePath = Path.Combine(fixerDir, "LocalMethodFixerStaticQueryableRewrite.cs");
        var staticQueryableQualifiersPath = Path.Combine(fixerDir, "LocalMethodFixerStaticQueryableQualifiers.cs");

        Assert.True(File.Exists(staticQueryableQualifiersPath), "LC001 static Queryable qualifier and method classification should live in a focused partial file.");

        var staticQueryableRewriteSource = File.ReadAllText(staticQueryableRewritePath);
        Assert.DoesNotContain("private static ExpressionSyntax CreateEnumerableQualifier", staticQueryableRewriteSource);
        Assert.DoesNotContain("private static bool IsSystemLinqQueryableType", staticQueryableRewriteSource);
        Assert.DoesNotContain("private static bool CanSwitchStaticQueryableMethodToEnumerable", staticQueryableRewriteSource);

        var staticQueryableQualifiersSource = File.ReadAllText(staticQueryableQualifiersPath);
        Assert.Contains("private static ExpressionSyntax CreateEnumerableQualifier", staticQueryableQualifiersSource);
        Assert.Contains("private static bool IsSystemLinqQueryableType", staticQueryableQualifiersSource);
        Assert.Contains("private static bool CanSwitchStaticQueryableMethodToEnumerable", staticQueryableQualifiersSource);
    }

    [Fact]
    public void LC001_QueryDiscovery_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC001_LocalMethod");
        var fixerPath = Path.Combine(fixerDir, "LocalMethodFixer.cs");
        var queryDiscoveryPath = Path.Combine(fixerDir, "LocalMethodFixerQueryDiscovery.cs");

        Assert.True(File.Exists(queryDiscoveryPath), "LC001 query invocation discovery and rewrite eligibility should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.DoesNotContain("private static InvocationExpressionSyntax? FindQueryInvocation", fixerSource);
        Assert.DoesNotContain("private static bool IsNestedQueryInvocation", fixerSource);
        Assert.DoesNotContain("private static bool CanRewriteQueryInvocation", fixerSource);

        var queryDiscoverySource = File.ReadAllText(queryDiscoveryPath);
        Assert.Contains("private static InvocationExpressionSyntax? FindQueryInvocation", queryDiscoverySource);
        Assert.Contains("private static bool IsNestedQueryInvocation", queryDiscoverySource);
        Assert.Contains("private static bool CanRewriteQueryInvocation", queryDiscoverySource);
    }

    [Fact]
    public void LC001_AnalyzerTrustPolicy_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC001_LocalMethod");
        var analyzerPath = Path.Combine(analyzerDir, "LocalMethodAnalyzer.cs");
        var trustPolicyPath = Path.Combine(analyzerDir, "LocalMethodAnalyzerTrustPolicy.cs");

        Assert.True(File.Exists(trustPolicyPath), "LC001 trusted method and translation-marker policy should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class LocalMethodAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsTrustedTranslatableMethod", analyzerSource);
        Assert.DoesNotContain("private static bool HasExplicitTranslationMarker", analyzerSource);
        Assert.DoesNotContain("private static IEnumerable<IMethodSymbol> EnumerateMethodVariants", analyzerSource);

        var trustPolicySource = File.ReadAllText(trustPolicyPath);
        Assert.Contains("private static bool IsTrustedTranslatableMethod", trustPolicySource);
        Assert.Contains("private static bool HasExplicitTranslationMarker", trustPolicySource);
        Assert.Contains("private static IEnumerable<IMethodSymbol> EnumerateMethodVariants", trustPolicySource);
    }

    [Fact]
    public void LC036_CapturedContextAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC036_DbContextCapturedAcrossThreads");
        var analyzerPath = Path.Combine(analyzerDir, "DbContextCapturedAcrossThreadsAnalyzer.cs");
        var captureAnalysisPath = Path.Combine(analyzerDir, "DbContextCapturedAcrossThreadsCaptureAnalysis.cs");

        Assert.True(File.Exists(captureAnalysisPath), "LC036 captured DbContext syntax/symbol analysis should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class DbContextCapturedAcrossThreadsAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool TryFindCapturedDbContext", analyzerSource);
        Assert.DoesNotContain("private static bool TryFindCapturedDbContextInLambda", analyzerSource);
        Assert.DoesNotContain("private static bool IsCapturedDbContext", analyzerSource);

        var captureAnalysisSource = File.ReadAllText(captureAnalysisPath);
        Assert.Contains("private static bool TryFindCapturedDbContext", captureAnalysisSource);
        Assert.Contains("private static bool TryFindCapturedDbContextInLambda", captureAnalysisSource);
        Assert.Contains("private static bool IsCapturedDbContext", captureAnalysisSource);
    }

    [Fact]
    public void LC007_LocalWriteTracking_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC007_NPlusOneLooper");
        var analysisPath = Path.Combine(analyzerDir, "NPlusOneLooperAnalysis.cs");
        var localWritePath = Path.Combine(analyzerDir, "NPlusOneLooperLocalWriteAnalysis.cs");

        Assert.True(File.Exists(localWritePath), "LC007 local-write tracking should live in a focused partial file.");

        var analysisSource = File.ReadAllText(analysisPath);
        Assert.Contains("internal static partial class NPlusOneLooperAnalysis", analysisSource);
        Assert.DoesNotContain("private sealed class LocalWriteCache", analysisSource);
        Assert.DoesNotContain("private static bool TryGetSingleAssignedLocalValue", analysisSource);

        var localWriteSource = File.ReadAllText(localWritePath);
        Assert.Contains("private sealed class LocalWriteCache", localWriteSource);
        Assert.Contains("private static bool TryGetSingleAssignedLocalValue", localWriteSource);
    }

    [Fact]
    public void LC045_ConditionalAccessEdgeCaseTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC045_MissingInclude");
        var edgeCasesPath = Path.Combine(testDir, "MissingIncludeEdgeCasesTests.cs");
        var conditionalAccessTestsPath = Path.Combine(testDir, "MissingIncludeConditionalAccessEdgeCasesTests.cs");

        Assert.True(File.Exists(conditionalAccessTestsPath), "LC045 conditional-access edge cases should live in a focused partial test file.");

        var edgeCasesSource = File.ReadAllText(edgeCasesPath);
        Assert.Contains("public partial class MissingIncludeEdgeCasesTests", edgeCasesSource);
        Assert.DoesNotContain("TestInnocent_ConditionalCollectionMutatorCall_NoDiagnostic", edgeCasesSource);
        Assert.DoesNotContain("TestCrime_InheritedNavigationParenthesizedConditionalAccessReportsFullNestedPath", edgeCasesSource);

        var conditionalAccessTestsSource = File.ReadAllText(conditionalAccessTestsPath);
        Assert.Contains("public partial class MissingIncludeEdgeCasesTests", conditionalAccessTestsSource);
        Assert.Contains("TestInnocent_ConditionalCollectionMutatorCall_NoDiagnostic", conditionalAccessTestsSource);
        Assert.Contains("TestCrime_InheritedNavigationParenthesizedConditionalAccessReportsFullNestedPath", conditionalAccessTestsSource);
    }

    [Fact]
    public void LC045_ConditionalAccessCrimeTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC045_MissingInclude");
        var generalTestsPath = Path.Combine(testDir, "MissingIncludeTests.cs");
        var conditionalAccessTestsPath = Path.Combine(testDir, "MissingIncludeConditionalAccessTests.cs");

        Assert.True(File.Exists(conditionalAccessTestsPath), "LC045 conditional-access crime cases should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class MissingIncludeTests", generalTestsSource);
        Assert.DoesNotContain("TestCrime_ConditionalAccessNav_TriggersDiagnostic", generalTestsSource);
        Assert.DoesNotContain("TestCrime_LocalFromConditionalIndexer_TriggersDiagnostic", generalTestsSource);

        var conditionalAccessTestsSource = File.ReadAllText(conditionalAccessTestsPath);
        Assert.Contains("public partial class MissingIncludeTests", conditionalAccessTestsSource);
        Assert.Contains("TestCrime_ConditionalAccessNav_TriggersDiagnostic", conditionalAccessTestsSource);
        Assert.Contains("TestCrime_LocalFromConditionalIndexer_TriggersDiagnostic", conditionalAccessTestsSource);
    }

    [Fact]
    public void LC045_CoveredNavigationTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC045_MissingInclude");
        var edgeCasesPath = Path.Combine(testDir, "MissingIncludeEdgeCasesTests.cs");
        var coveredNavigationTestsPath = Path.Combine(testDir, "MissingIncludeCoveredNavigationTests.cs");

        Assert.True(File.Exists(coveredNavigationTestsPath), "LC045 include-covered navigation tests should live in a focused partial test file.");

        var edgeCasesSource = File.ReadAllText(edgeCasesPath);
        Assert.Contains("public partial class MissingIncludeEdgeCasesTests", edgeCasesSource);
        Assert.DoesNotContain("TestInnocent_LambdaIncludeCoversAccess_NoDiagnostic", edgeCasesSource);
        Assert.DoesNotContain("TestInnocent_CastMidPathInclude_NoDiagnostic", edgeCasesSource);

        var coveredNavigationTestsSource = File.ReadAllText(coveredNavigationTestsPath);
        Assert.Contains("public partial class MissingIncludeEdgeCasesTests", coveredNavigationTestsSource);
        Assert.Contains("TestInnocent_LambdaIncludeCoversAccess_NoDiagnostic", coveredNavigationTestsSource);
        Assert.Contains("TestInnocent_CastMidPathInclude_NoDiagnostic", coveredNavigationTestsSource);
    }

    [Fact]
    public void LC045_FixerSyntaxTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC045_MissingInclude");
        var fixerTestsPath = Path.Combine(testDir, "MissingIncludeFixerTests.cs");
        var syntaxTestsPath = Path.Combine(testDir, "MissingIncludeFixerSyntaxTests.cs");

        Assert.True(File.Exists(syntaxTestsPath), "LC045 fixer syntax and fix-all edge cases should live in a focused partial test file.");

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class MissingIncludeFixerTests", fixerTestsSource);
        Assert.DoesNotContain("FixCrime_StaticCallSyntax_WrapsTheArgumentNotTheTypeName", fixerTestsSource);
        Assert.DoesNotContain("FixAll_AddsIncludeToEveryFlaggedQuery", fixerTestsSource);

        var syntaxTestsSource = File.ReadAllText(syntaxTestsPath);
        Assert.Contains("public partial class MissingIncludeFixerTests", syntaxTestsSource);
        Assert.Contains("FixCrime_StaticCallSyntax_WrapsTheArgumentNotTheTypeName", syntaxTestsSource);
        Assert.Contains("FixAll_AddsIncludeToEveryFlaggedQuery", syntaxTestsSource);
    }

    [Fact]
    public void LC025_ReassignmentTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC025_AsNoTrackingWithUpdate");
        var generalTestsPath = Path.Combine(testDir, "AsNoTrackingWithUpdateTests.cs");
        var reassignmentTestsPath = Path.Combine(testDir, "AsNoTrackingWithUpdateReassignmentTests.cs");

        Assert.True(File.Exists(reassignmentTestsPath), "LC025 reassignment and ambiguous-origin tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class AsNoTrackingWithUpdateTests", generalTestsSource);
        Assert.DoesNotContain("ConditionallyReassignedToNoTracking_AmbiguousOrigin_ShouldNotTrigger", generalTestsSource);
        Assert.DoesNotContain("ReassignedInsideSameBranchAsUpdate_ShouldStillTrigger", generalTestsSource);

        var reassignmentTestsSource = File.ReadAllText(reassignmentTestsPath);
        Assert.Contains("public partial class AsNoTrackingWithUpdateTests", reassignmentTestsSource);
        Assert.Contains("ConditionallyReassignedToNoTracking_AmbiguousOrigin_ShouldNotTrigger", reassignmentTestsSource);
        Assert.Contains("ReassignedInsideSameBranchAsUpdate_ShouldStillTrigger", reassignmentTestsSource);
    }

    [Fact]
    public void LC025_ProjectionTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC025_AsNoTrackingWithUpdate");
        var generalTestsPath = Path.Combine(testDir, "AsNoTrackingWithUpdateTests.cs");
        var projectionTestsPath = Path.Combine(testDir, "AsNoTrackingWithUpdateProjectionTests.cs");

        Assert.True(File.Exists(projectionTestsPath), "LC025 projection and query-alias tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class AsNoTrackingWithUpdateTests", generalTestsSource);
        Assert.DoesNotContain("MaterializedFromNoTrackingQueryAlias_ShouldTriggerLC025", generalTestsSource);
        Assert.DoesNotContain("IdentitySelect_ThenUpdate_ShouldStillTrigger", generalTestsSource);

        var projectionTestsSource = File.ReadAllText(projectionTestsPath);
        Assert.Contains("public partial class AsNoTrackingWithUpdateTests", projectionTestsSource);
        Assert.Contains("MaterializedFromNoTrackingQueryAlias_ShouldTriggerLC025", projectionTestsSource);
        Assert.Contains("IdentitySelect_ThenUpdate_ShouldStillTrigger", projectionTestsSource);
    }

    [Fact]
    public void LC024_QuerySyntaxTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC024_GroupByNonTranslatable");
        var generalTestsPath = Path.Combine(testDir, "GroupByNonTranslatableTests.cs");
        var querySyntaxTestsPath = Path.Combine(testDir, "GroupByNonTranslatableQuerySyntaxTests.cs");

        Assert.True(File.Exists(querySyntaxTestsPath), "LC024 query-syntax GroupBy coverage should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class GroupByNonTranslatableTests", generalTestsSource);
        Assert.DoesNotContain("QuerySyntaxGroupBy_Select_ToList_ShouldTriggerLC024", generalTestsSource);
        Assert.DoesNotContain("EnumerableQuerySyntaxGroupBy_Select_ClientProjection_ShouldNotTrigger", generalTestsSource);

        var querySyntaxTestsSource = File.ReadAllText(querySyntaxTestsPath);
        Assert.Contains("public partial class GroupByNonTranslatableTests", querySyntaxTestsSource);
        Assert.Contains("QuerySyntaxGroupBy_Select_ToList_ShouldTriggerLC024", querySyntaxTestsSource);
        Assert.Contains("EnumerableQuerySyntaxGroupBy_Select_ClientProjection_ShouldNotTrigger", querySyntaxTestsSource);
    }

    [Fact]
    public void LC012_FixerContextSafetyTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC012_OptimizeRemoveRange");
        var fixerTestsPath = Path.Combine(testDir, "OptimizeRemoveRangeFixerTests.cs");
        var contextSafetyTestsPath = Path.Combine(testDir, "OptimizeRemoveRangeFixerContextSafetyTests.cs");

        Assert.True(File.Exists(contextSafetyTestsPath), "LC012 fixer SaveChanges/context safety tests should live in a focused partial test file.");

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class OptimizeRemoveRangeFixerTests", fixerTestsSource);
        Assert.DoesNotContain("Fixer_ShouldNotRegister_WhenRemoveRangeIsFollowedBySaveChanges", fixerTestsSource);
        Assert.DoesNotContain("Fixer_ShouldNotRegister_WhenQueryCombinesLaterSaveContextSource", fixerTestsSource);

        var contextSafetyTestsSource = File.ReadAllText(contextSafetyTestsPath);
        Assert.Contains("public partial class OptimizeRemoveRangeFixerTests", contextSafetyTestsSource);
        Assert.Contains("Fixer_ShouldNotRegister_WhenRemoveRangeIsFollowedBySaveChanges", contextSafetyTestsSource);
        Assert.Contains("Fixer_ShouldNotRegister_WhenQueryCombinesLaterSaveContextSource", contextSafetyTestsSource);
    }

    [Fact]
    public void LC012_AnalyzerSaveChangesTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC012_OptimizeRemoveRange");
        var analyzerTestsPath = Path.Combine(testDir, "OptimizeRemoveRangeAnalyzerTests.cs");
        var saveChangesTestsPath = Path.Combine(testDir, "OptimizeRemoveRangeAnalyzerSaveChangesTests.cs");

        Assert.True(File.Exists(saveChangesTestsPath), "LC012 analyzer SaveChanges/control-flow suppression tests should live in a focused partial test file.");

        var analyzerTestsSource = File.ReadAllText(analyzerTestsPath);
        Assert.Contains("public partial class OptimizeRemoveRangeAnalyzerTests", analyzerTestsSource);
        Assert.DoesNotContain("RemoveRange_FollowedBySaveChanges_ShouldNotTrigger", analyzerTestsSource);
        Assert.DoesNotContain("RemoveRange_InTryWithSaveChangesInCatch_ShouldNotTrigger", analyzerTestsSource);

        var saveChangesTestsSource = File.ReadAllText(saveChangesTestsPath);
        Assert.Contains("public partial class OptimizeRemoveRangeAnalyzerTests", saveChangesTestsSource);
        Assert.Contains("RemoveRange_FollowedBySaveChanges_ShouldNotTrigger", saveChangesTestsSource);
        Assert.Contains("RemoveRange_InTryWithSaveChangesInCatch_ShouldNotTrigger", saveChangesTestsSource);
    }

    [Fact]
    public void LC014_ColumnDerivedArgumentTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC014_AvoidStringCaseConversion");
        var generalTestsPath = Path.Combine(testDir, "AvoidStringCaseConversionTests.cs");
        var argumentTestsPath = Path.Combine(testDir, "AvoidStringCaseConversionColumnDerivedArgumentTests.cs");

        Assert.True(File.Exists(argumentTestsPath), "LC014 column-derived method argument tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class AvoidStringCaseConversionTests", generalTestsSource);
        Assert.DoesNotContain("ToLower_OnMethodResultWithParameterArgument_ShouldTrigger", generalTestsSource);
        Assert.DoesNotContain("ConstantReceiver_ColumnAsStringArgument_ShouldTrigger", generalTestsSource);

        var argumentTestsSource = File.ReadAllText(argumentTestsPath);
        Assert.Contains("public partial class AvoidStringCaseConversionTests", argumentTestsSource);
        Assert.Contains("ToLower_OnMethodResultWithParameterArgument_ShouldTrigger", argumentTestsSource);
        Assert.Contains("ConstantReceiver_ColumnAsStringArgument_ShouldTrigger", argumentTestsSource);
    }

    [Fact]
    public void LC015_PositionOperatorTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC015_MissingOrderBy");
        var generalTestsPath = Path.Combine(testDir, "MissingOrderByTests.cs");
        var positionOperatorTestsPath = Path.Combine(testDir, "MissingOrderByPositionOperatorTests.cs");

        Assert.True(File.Exists(positionOperatorTestsPath), "LC015 ElementAt/Last operator coverage should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class MissingOrderByTests", generalTestsSource);
        Assert.DoesNotContain("ElementAt_WithoutOrderBy_ShouldTrigger", generalTestsSource);
        Assert.DoesNotContain("TakeLast_IsNotFlagged_BecauseEfCannotTranslateItAtAll", generalTestsSource);

        var positionOperatorTestsSource = File.ReadAllText(positionOperatorTestsPath);
        Assert.Contains("public partial class MissingOrderByTests", positionOperatorTestsSource);
        Assert.Contains("ElementAt_WithoutOrderBy_ShouldTrigger", positionOperatorTestsSource);
        Assert.Contains("TakeLast_IsNotFlagged_BecauseEfCannotTranslateItAtAll", positionOperatorTestsSource);
    }

    [Fact]
    public void LC023_QueryFilterTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC023_FindInsteadOfFirstOrDefault");
        var generalTestsPath = Path.Combine(testDir, "FindInsteadOfFirstOrDefaultTests.cs");
        var queryFilterTestsPath = Path.Combine(testDir, "FindInsteadOfFirstOrDefaultQueryFilterTests.cs");

        Assert.True(File.Exists(queryFilterTestsPath), "LC023 query-filter gate tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class FindInsteadOfFirstOrDefaultTests", generalTestsSource);
        Assert.DoesNotContain("FirstOrDefault_WithId_WhenEntityHasQueryFilter_ShouldNotTrigger", generalTestsSource);
        Assert.DoesNotContain("FirstOrDefault_WithUnrelatedGenericHasQueryFilter_ShouldStillTrigger", generalTestsSource);

        var queryFilterTestsSource = File.ReadAllText(queryFilterTestsPath);
        Assert.Contains("public partial class FindInsteadOfFirstOrDefaultTests", queryFilterTestsSource);
        Assert.Contains("FirstOrDefault_WithId_WhenEntityHasQueryFilter_ShouldNotTrigger", queryFilterTestsSource);
        Assert.Contains("FirstOrDefault_WithUnrelatedGenericHasQueryFilter_ShouldStillTrigger", queryFilterTestsSource);
    }

    [Fact]
    public void LC023_KeyConfigurationTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC023_FindInsteadOfFirstOrDefault");
        var generalTestsPath = Path.Combine(testDir, "FindInsteadOfFirstOrDefaultTests.cs");
        var keyConfigurationTestsPath = Path.Combine(testDir, "FindInsteadOfFirstOrDefaultKeyConfigurationTests.cs");

        Assert.True(File.Exists(keyConfigurationTestsPath), "LC023 key-configuration tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class FindInsteadOfFirstOrDefaultTests", generalTestsSource);
        Assert.DoesNotContain("FirstOrDefault_WithConventionId_WhenFluentApiConfiguresDifferentKey_ShouldNotTrigger", generalTestsSource);
        Assert.DoesNotContain("FirstOrDefault_WithPartialCompositeFluentApiKey_ShouldNotTrigger", generalTestsSource);

        var keyConfigurationTestsSource = File.ReadAllText(keyConfigurationTestsPath);
        Assert.Contains("public partial class FindInsteadOfFirstOrDefaultTests", keyConfigurationTestsSource);
        Assert.Contains("FirstOrDefault_WithConventionId_WhenFluentApiConfiguresDifferentKey_ShouldNotTrigger", keyConfigurationTestsSource);
        Assert.Contains("FirstOrDefault_WithPartialCompositeFluentApiKey_ShouldNotTrigger", keyConfigurationTestsSource);
    }

    [Fact]
    public void LC023_QueryFilterCache_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC023_FindInsteadOfFirstOrDefault");
        var keyAnalysisPath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultKeyAnalysis.cs");
        var primaryKeyCachePath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultPrimaryKeyCache.cs");
        var queryFilterCachePath = Path.Combine(analyzerDir, "FindInsteadOfFirstOrDefaultQueryFilterCache.cs");

        Assert.True(File.Exists(primaryKeyCachePath), "LC023 primary-key cache state should live in a focused partial file.");
        Assert.True(File.Exists(queryFilterCachePath), "LC023 query-filter cache registration and lookup should live in a focused partial file.");

        var keyAnalysisSource = File.ReadAllText(keyAnalysisPath);
        Assert.Contains("internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis", keyAnalysisSource);
        Assert.DoesNotContain("internal sealed partial class PrimaryKeyCache", keyAnalysisSource);
        Assert.DoesNotContain("public void RegisterQueryFilter", keyAnalysisSource);
        Assert.DoesNotContain("public bool HasQueryFilter", keyAnalysisSource);
        Assert.DoesNotContain("private bool HasRegisteredQueryFilter", keyAnalysisSource);

        var primaryKeyCacheSource = File.ReadAllText(primaryKeyCachePath);
        Assert.Contains("internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis", primaryKeyCacheSource);
        Assert.Contains("internal sealed partial class PrimaryKeyCache", primaryKeyCacheSource);
        Assert.DoesNotContain("public void RegisterQueryFilter", primaryKeyCacheSource);
        Assert.DoesNotContain("public bool HasQueryFilter", primaryKeyCacheSource);

        var queryFilterCacheSource = File.ReadAllText(queryFilterCachePath);
        Assert.Contains("internal static partial class FindInsteadOfFirstOrDefaultKeyAnalysis", queryFilterCacheSource);
        Assert.Contains("internal sealed partial class PrimaryKeyCache", queryFilterCacheSource);
        Assert.Contains("public void RegisterQueryFilter", queryFilterCacheSource);
        Assert.Contains("public bool HasQueryFilter", queryFilterCacheSource);
        Assert.Contains("private bool HasRegisteredQueryFilter", queryFilterCacheSource);
    }

    [Fact]
    public void LC023_FixerContext_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC023_FindInsteadOfFirstOrDefault");
        var fixerPath = Path.Combine(fixerDir, "FindInsteadOfFirstOrDefaultFixer.cs");
        var contextPath = Path.Combine(fixerDir, "FindInsteadOfFirstOrDefaultFixerContext.cs");

        Assert.True(File.Exists(contextPath), "LC023 fixer context derivation should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class FindInsteadOfFirstOrDefaultFixer", fixerSource);
        Assert.DoesNotContain("private static bool TryCreateFixContext", fixerSource);
        Assert.DoesNotContain("private static bool TryGetKeyValueExpression", fixerSource);
        Assert.DoesNotContain("private sealed class FixContext", fixerSource);

        var contextSource = File.ReadAllText(contextPath);
        Assert.Contains("private static bool TryCreateFixContext", contextSource);
        Assert.DoesNotContain("private static bool TryGetKeyValueExpression", contextSource);
        Assert.Contains("private sealed class FixContext", contextSource);
    }

    [Fact]
    public void LC027_RelationshipBuilderLocalTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC027_MissingExplicitForeignKey");
        var generalTestsPath = Path.Combine(testDir, "MissingExplicitForeignKeyEdgeCasesTests.cs");
        var relationshipBuilderTestsPath = Path.Combine(testDir, "MissingExplicitForeignKeyRelationshipBuilderLocalTests.cs");

        Assert.True(File.Exists(relationshipBuilderTestsPath), "LC027 relationship-builder local and shadowing tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class MissingExplicitForeignKeyEdgeCasesTests", generalTestsSource);
        Assert.DoesNotContain("Navigation_WithRelationshipBuilderLocalShadowForeignKey_ShouldNotTrigger", generalTestsSource);
        Assert.DoesNotContain("Navigation_WithMemberAssignmentSharingRelationshipBuilderLocalName_ShouldNotTrigger", generalTestsSource);

        var relationshipBuilderTestsSource = File.ReadAllText(relationshipBuilderTestsPath);
        Assert.Contains("public partial class MissingExplicitForeignKeyEdgeCasesTests", relationshipBuilderTestsSource);
        Assert.Contains("Navigation_WithRelationshipBuilderLocalShadowForeignKey_ShouldNotTrigger", relationshipBuilderTestsSource);
        Assert.Contains("Navigation_WithMemberAssignmentSharingRelationshipBuilderLocalName_ShouldNotTrigger", relationshipBuilderTestsSource);
    }

    [Fact]
    public void LC041_NullConditionalTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC041_SingleEntityScalarProjection");
        var generalTestsPath = Path.Combine(testDir, "SingleEntityScalarProjectionTests.cs");
        var nullConditionalTestsPath = Path.Combine(testDir, "SingleEntityScalarProjectionNullConditionalTests.cs");

        Assert.True(File.Exists(nullConditionalTestsPath), "LC041 null-conditional projection tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class SingleEntityScalarProjectionTests", generalTestsSource);
        Assert.DoesNotContain("FirstOrDefault_WithNullConditionalSinglePropertyUsage_Triggers", generalTestsSource);
        Assert.DoesNotContain("First_WithNullConditionalSinglePropertyMethodUsage_ShouldNotOfferFix", generalTestsSource);

        var nullConditionalTestsSource = File.ReadAllText(nullConditionalTestsPath);
        Assert.Contains("public partial class SingleEntityScalarProjectionTests", nullConditionalTestsSource);
        Assert.Contains("FirstOrDefault_WithNullConditionalSinglePropertyUsage_Triggers", nullConditionalTestsSource);
        Assert.Contains("First_WithNullConditionalSinglePropertyMethodUsage_ShouldNotOfferFix", nullConditionalTestsSource);
    }

    [Fact]
    public void LC041_FixerTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC041_SingleEntityScalarProjection");
        var generalTestsPath = Path.Combine(testDir, "SingleEntityScalarProjectionTests.cs");
        var fixerTestsPath = Path.Combine(testDir, "SingleEntityScalarProjectionFixerTests.cs");

        Assert.True(File.Exists(fixerTestsPath), "LC041 fixer and fix-all coverage should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class SingleEntityScalarProjectionTests", generalTestsSource);
        Assert.DoesNotContain("Fixer_ShouldProjectSingleConsumedProperty", generalTestsSource);
        Assert.DoesNotContain("FixAll_RewritesMultipleScalarProjections", generalTestsSource);

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class SingleEntityScalarProjectionTests", fixerTestsSource);
        Assert.Contains("Fixer_ShouldProjectSingleConsumedProperty", fixerTestsSource);
        Assert.Contains("FixAll_RewritesMultipleScalarProjections", fixerTestsSource);
    }

    [Fact]
    public void LC041_FixerContext_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC041_SingleEntityScalarProjection");
        var fixerPath = Path.Combine(fixerDir, "SingleEntityScalarProjectionFixer.cs");
        var contextPath = Path.Combine(fixerDir, "SingleEntityScalarProjectionFixerContext.cs");

        Assert.True(File.Exists(contextPath), "LC041 fixer context derivation and predicate safety should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class SingleEntityScalarProjectionFixer", fixerSource);
        Assert.DoesNotContain("private static bool TryGetFixContext", fixerSource);
        Assert.DoesNotContain("private static bool HasUnsupportedPredicateArgument", fixerSource);
        Assert.DoesNotContain("private sealed class FixContext", fixerSource);

        var contextSource = File.ReadAllText(contextPath);
        Assert.Contains("private static bool TryGetFixContext", contextSource);
        Assert.Contains("private static bool HasUnsupportedPredicateArgument", contextSource);
        Assert.Contains("private sealed class FixContext", contextSource);
    }

    [Fact]
    public void LC041_PrimaryKeyLookupAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC041_SingleEntityScalarProjection");
        var queryAnalysisPath = Path.Combine(analyzerDir, "SingleEntityScalarProjectionQueryAnalysis.cs");
        var primaryKeyLookupPath = Path.Combine(analyzerDir, "SingleEntityScalarProjectionPrimaryKeyLookup.cs");

        Assert.True(File.Exists(primaryKeyLookupPath), "LC041 primary-key lookup exemption analysis should live in a focused partial file.");

        var queryAnalysisSource = File.ReadAllText(queryAnalysisPath);
        Assert.DoesNotContain("private static bool IsPrimaryKeyLookupInChain", queryAnalysisSource);
        Assert.DoesNotContain("private static bool IsPrimaryKeyLookup", queryAnalysisSource);
        Assert.DoesNotContain("private static bool IsPrimaryKeyProperty", queryAnalysisSource);

        var primaryKeyLookupSource = File.ReadAllText(primaryKeyLookupPath);
        Assert.Contains("private static bool IsPrimaryKeyLookupInChain", primaryKeyLookupSource);
        Assert.Contains("private static bool IsPrimaryKeyLookup", primaryKeyLookupSource);
        Assert.Contains("private static bool IsPrimaryKeyProperty", primaryKeyLookupSource);
    }

    [Fact]
    public void LC033_InitializerClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC033_UseFrozenSetForStaticMembershipCaches");
        var analysisPath = Path.Combine(analyzerDir, "UseFrozenSetForStaticMembershipCachesAnalysis.cs");
        var classificationPath = Path.Combine(analyzerDir, "UseFrozenSetForStaticMembershipCachesInitializerClassification.cs");
        var toHashSetPath = Path.Combine(analyzerDir, "UseFrozenSetForStaticMembershipCachesToHashSetClassification.cs");

        Assert.True(File.Exists(classificationPath), "LC033 initializer classification should live in a focused partial file.");
        Assert.True(File.Exists(toHashSetPath), "LC033 ToHashSet classification should live in a focused partial file.");

        var analysisSource = File.ReadAllText(analysisPath);
        Assert.Contains("internal static partial class UseFrozenSetForStaticMembershipCachesAnalysis", analysisSource);
        Assert.DoesNotContain("public static bool TryClassifyInitializer", analysisSource);
        Assert.DoesNotContain("private static bool IsSupportedCollectionInitializer", analysisSource);
        Assert.DoesNotContain("private static bool IsSupportedToHashSetInvocation", analysisSource);

        var classificationSource = File.ReadAllText(classificationPath);
        Assert.Contains("internal static partial class UseFrozenSetForStaticMembershipCachesAnalysis", classificationSource);
        Assert.Contains("public static bool TryClassifyInitializer", classificationSource);
        Assert.Contains("private static bool IsSupportedCollectionInitializer", classificationSource);
        Assert.DoesNotContain("private static bool IsSupportedToHashSetInvocation", classificationSource);

        var toHashSetSource = File.ReadAllText(toHashSetPath);
        Assert.Contains("private static bool IsSupportedToHashSetInvocation", toHashSetSource);
    }

    [Fact]
    public void LC018_ConstantInterpolationTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC018_AvoidFromSqlRawWithInterpolation");
        var generalTestsPath = Path.Combine(testDir, "AvoidFromSqlRawWithInterpolationTests.cs");
        var constantInterpolationTestsPath = Path.Combine(testDir, "AvoidFromSqlRawWithInterpolationConstantInterpolationTests.cs");

        Assert.True(File.Exists(constantInterpolationTestsPath), "LC018 constant-interpolation boundary tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class AvoidFromSqlRawWithInterpolationTests", generalTestsSource);
        Assert.DoesNotContain("FromSqlRaw_WithConstantOnlyInterpolation_ShouldNotTriggerLC018", generalTestsSource);
        Assert.DoesNotContain("FromSqlRaw_WithStaticReadonlyFieldInterpolation_ShouldTriggerLC018", generalTestsSource);

        var constantInterpolationTestsSource = File.ReadAllText(constantInterpolationTestsPath);
        Assert.Contains("public partial class AvoidFromSqlRawWithInterpolationTests", constantInterpolationTestsSource);
        Assert.Contains("FromSqlRaw_WithConstantOnlyInterpolation_ShouldNotTriggerLC018", constantInterpolationTestsSource);
        Assert.Contains("FromSqlRaw_WithStaticReadonlyFieldInterpolation_ShouldTriggerLC018", constantInterpolationTestsSource);
    }

    [Fact]
    public void LC018_FixerTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC018_AvoidFromSqlRawWithInterpolation");
        var generalTestsPath = Path.Combine(testDir, "AvoidFromSqlRawWithInterpolationTests.cs");
        var fixerTestsPath = Path.Combine(testDir, "AvoidFromSqlRawWithInterpolationFixerTests.cs");

        Assert.True(File.Exists(fixerTestsPath), "LC018 fixer and fix-all coverage should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class AvoidFromSqlRawWithInterpolationTests", generalTestsSource);
        Assert.DoesNotContain("Fixer_ShouldReplaceFromSqlRawWithFromSqlInterpolated", generalTestsSource);
        Assert.DoesNotContain("FixAll_RewritesAllFromSqlRawWithInterpolatedStringInstances", generalTestsSource);

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class AvoidFromSqlRawWithInterpolationTests", fixerTestsSource);
        Assert.Contains("Fixer_ShouldReplaceFromSqlRawWithFromSqlInterpolated", fixerTestsSource);
        Assert.Contains("FixAll_RewritesAllFromSqlRawWithInterpolatedStringInstances", fixerTestsSource);
    }

    [Fact]
    public void LC018_FixerSqlSafety_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC018_AvoidFromSqlRawWithInterpolation");
        var fixerPath = Path.Combine(fixerDir, "AvoidFromSqlRawWithInterpolationFixer.cs");
        var sqlSafetyPath = Path.Combine(fixerDir, "AvoidFromSqlRawWithInterpolationFixerSqlSafety.cs");

        Assert.True(File.Exists(sqlSafetyPath), "LC018 fixer SQL interpolation safety checks should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class AvoidFromSqlRawWithInterpolationFixer", fixerSource);
        Assert.DoesNotContain("private static bool HasInterpolationInsideSqlStringLiteral", fixerSource);
        Assert.DoesNotContain("private static bool HasInterpolationOutsideLikelySqlValuePosition", fixerSource);
        Assert.DoesNotContain("private static bool IsLikelySqlValuePosition", fixerSource);
        Assert.DoesNotContain("private static bool ToggleSqlStringLiteralState", fixerSource);

        var sqlSafetySource = File.ReadAllText(sqlSafetyPath);
        Assert.Contains("private static bool HasInterpolationInsideSqlStringLiteral", sqlSafetySource);
        Assert.Contains("private static bool HasInterpolationOutsideLikelySqlValuePosition", sqlSafetySource);
        Assert.Contains("private static bool IsLikelySqlValuePosition", sqlSafetySource);
        Assert.Contains("private static bool ToggleSqlStringLiteralState", sqlSafetySource);
    }

    [Fact]
    public void LC034_FixerTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC034_AvoidExecuteSqlRawWithInterpolation");
        var generalTestsPath = Path.Combine(testDir, "AvoidExecuteSqlRawWithInterpolationTests.cs");
        var fixerTestsPath = Path.Combine(testDir, "AvoidExecuteSqlRawWithInterpolationFixerTests.cs");

        Assert.True(File.Exists(fixerTestsPath), "LC034 fixer coverage should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class AvoidExecuteSqlRawWithInterpolationTests", generalTestsSource);
        Assert.DoesNotContain("Fixer_ShouldReplaceExecuteSqlRawWithExecuteSql", generalTestsSource);
        Assert.DoesNotContain("FixAll_RewritesAllExecuteSqlRawCalls", generalTestsSource);

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class AvoidExecuteSqlRawWithInterpolationTests", fixerTestsSource);
        Assert.Contains("Fixer_ShouldReplaceExecuteSqlRawWithExecuteSql", fixerTestsSource);
        Assert.Contains("FixAll_RewritesAllExecuteSqlRawCalls", fixerTestsSource);
    }

    [Fact]
    public void LC011_FluentConfigurationTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC011_EntityMissingPrimaryKey");
        var edgeCasesPath = Path.Combine(testDir, "EntityMissingPrimaryKeyEdgeCasesTests.cs");
        var fluentConfigurationTestsPath = Path.Combine(testDir, "EntityMissingPrimaryKeyFluentConfigurationTests.cs");

        Assert.True(File.Exists(fluentConfigurationTestsPath), "LC011 fluent configuration edge cases should live in a focused partial test file.");

        var edgeCasesSource = File.ReadAllText(edgeCasesPath);
        Assert.Contains("public partial class EntityMissingPrimaryKeyEdgeCasesTests", edgeCasesSource);
        Assert.DoesNotContain("TestCrime_UnappliedEntityTypeConfiguration_ShouldTrigger", edgeCasesSource);
        Assert.DoesNotContain("TestInnocent_ScopedBuilderVariableReuse_ShouldNotTrigger", edgeCasesSource);

        var fluentConfigurationTestsSource = File.ReadAllText(fluentConfigurationTestsPath);
        Assert.Contains("public partial class EntityMissingPrimaryKeyEdgeCasesTests", fluentConfigurationTestsSource);
        Assert.Contains("TestCrime_UnappliedEntityTypeConfiguration_ShouldTrigger", fluentConfigurationTestsSource);
        Assert.Contains("TestInnocent_ScopedBuilderVariableReuse_ShouldNotTrigger", fluentConfigurationTestsSource);
    }

    [Fact]
    public void LC011_KeyConventionTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC011_EntityMissingPrimaryKey");
        var generalTestsPath = Path.Combine(testDir, "EntityMissingPrimaryKeyTests.cs");
        var conventionTestsPath = Path.Combine(testDir, "EntityMissingPrimaryKeyKeyConventionTests.cs");

        Assert.True(File.Exists(conventionTestsPath), "LC011 key convention and key-shape tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class EntityMissingPrimaryKeyTests", generalTestsSource);
        Assert.DoesNotContain("TestInnocent_EntityWithId_ShouldNotTrigger", generalTestsSource);
        Assert.DoesNotContain("TestInnocent_StringIdProperty_ShouldNotTrigger", generalTestsSource);

        var conventionTestsSource = File.ReadAllText(conventionTestsPath);
        Assert.Contains("public partial class EntityMissingPrimaryKeyTests", conventionTestsSource);
        Assert.Contains("TestInnocent_EntityWithId_ShouldNotTrigger", conventionTestsSource);
        Assert.Contains("TestInnocent_StringIdProperty_ShouldNotTrigger", conventionTestsSource);
    }

    [Fact]
    public void LC030_RegistrationTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC030_DbContextInSingleton");
        var generalTestsPath = Path.Combine(testDir, "DbContextInSingletonTests.cs");
        var registrationTestsPath = Path.Combine(testDir, "DbContextInSingletonRegistrationTests.cs");

        Assert.True(File.Exists(registrationTestsPath), "LC030 dependency-injection registration tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class DbContextInSingletonTests", generalTestsSource);
        Assert.DoesNotContain("AddSingletonRegisteredService_WithStoredDbContext_ShouldTriggerLC030", generalTestsSource);
        Assert.DoesNotContain("AmbiguousServiceRegistration_ShouldNotTrigger", generalTestsSource);

        var registrationTestsSource = File.ReadAllText(registrationTestsPath);
        Assert.Contains("public partial class DbContextInSingletonTests", registrationTestsSource);
        Assert.Contains("AddSingletonRegisteredService_WithStoredDbContext_ShouldTriggerLC030", registrationTestsSource);
        Assert.Contains("AmbiguousServiceRegistration_ShouldNotTrigger", registrationTestsSource);
    }

    [Fact]
    public void LC030_RegistrationTypeResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC030_DbContextInSingleton");
        var registrationPath = Path.Combine(analyzerDir, "DbContextInSingletonRegistrationAnalysis.cs");
        var typeResolutionPath = Path.Combine(analyzerDir, "DbContextInSingletonRegistrationTypeResolution.cs");

        Assert.True(File.Exists(typeResolutionPath), "LC030 registration type resolution should live in a focused partial file.");

        var registrationSource = File.ReadAllText(registrationPath);
        Assert.DoesNotContain("private static IEnumerable<INamedTypeSymbol> GetRegisteredTypes", registrationSource);
        Assert.DoesNotContain("private static ITypeSymbol? GetTypeOfOperand", registrationSource);

        var typeResolutionSource = File.ReadAllText(typeResolutionPath);
        Assert.Contains("private static IEnumerable<INamedTypeSymbol> GetRegisteredTypes", typeResolutionSource);
        Assert.Contains("private static ITypeSymbol? GetTypeOfOperand", typeResolutionSource);
    }

    [Fact]
    public void LC017_PropertyAccessEdgeCaseTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC017_WholeEntityProjection");
        var edgeCasesPath = Path.Combine(testDir, "WholeEntityProjectionEdgeCasesTests.cs");
        var propertyAccessTestsPath = Path.Combine(testDir, "WholeEntityProjectionPropertyAccessEdgeCasesTests.cs");

        Assert.True(File.Exists(propertyAccessTestsPath), "LC017 property-access edge cases should live in a focused partial test file.");

        var edgeCasesSource = File.ReadAllText(edgeCasesPath);
        Assert.Contains("public partial class WholeEntityProjectionEdgeCasesTests", edgeCasesSource);
        Assert.DoesNotContain("TestCrime_NullConditionalPropertyAccess_TriggersDiagnostic", edgeCasesSource);
        Assert.DoesNotContain("TestCrime_BinaryExpression_TriggersDiagnostic", edgeCasesSource);

        var propertyAccessTestsSource = File.ReadAllText(propertyAccessTestsPath);
        Assert.Contains("public partial class WholeEntityProjectionEdgeCasesTests", propertyAccessTestsSource);
        Assert.Contains("TestCrime_NullConditionalPropertyAccess_TriggersDiagnostic", propertyAccessTestsSource);
        Assert.Contains("TestCrime_BinaryExpression_TriggersDiagnostic", propertyAccessTestsSource);
    }

    [Fact]
    public void LC017_FixerSafetyTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC017_WholeEntityProjection");
        var fixerTestsPath = Path.Combine(testDir, "WholeEntityProjectionFixerTests.cs");
        var safetyTestsPath = Path.Combine(testDir, "WholeEntityProjectionFixerSafetyTests.cs");

        Assert.True(File.Exists(safetyTestsPath), "LC017 fixer safety/no-fix cases should live in a focused partial test file.");

        var fixerTestsSource = File.ReadAllText(fixerTestsPath);
        Assert.Contains("public partial class WholeEntityProjectionFixerTests", fixerTestsSource);
        Assert.DoesNotContain("IndexedEntityEscape_HasNoFix", fixerTestsSource);
        Assert.DoesNotContain("ConditionalInterfaceCastAccessedProperty_HasNoFix", fixerTestsSource);

        var safetyTestsSource = File.ReadAllText(safetyTestsPath);
        Assert.Contains("public partial class WholeEntityProjectionFixerTests", safetyTestsSource);
        Assert.Contains("IndexedEntityEscape_HasNoFix", safetyTestsSource);
        Assert.Contains("ConditionalInterfaceCastAccessedProperty_HasNoFix", safetyTestsSource);
    }

    [Fact]
    public void RuleCatalog_LC001ToLC015Entries_LiveInDedicatedPartial()
    {
        var catalogDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Catalog");
        var catalogPath = Path.Combine(catalogDir, "RuleCatalog.cs");
        var firstRangePath = Path.Combine(catalogDir, "RuleCatalog.LC001ToLC015.cs");

        Assert.True(File.Exists(firstRangePath), "RuleCatalog LC001-LC015 entries should live in a focused partial file.");

        var catalogSource = File.ReadAllText(catalogPath);
        Assert.Contains("public static partial class RuleCatalog", catalogSource);
        Assert.DoesNotContain("id: \"LC001\"", catalogSource);
        Assert.DoesNotContain("id: \"LC015\"", catalogSource);

        var firstRangeSource = File.ReadAllText(firstRangePath);
        Assert.Contains("public static partial class RuleCatalog", firstRangeSource);
        Assert.Contains("id: \"LC001\"", firstRangeSource);
        Assert.Contains("id: \"LC015\"", firstRangeSource);
    }

    [Fact]
    public void LC039_TransactionTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC039_NestedSaveChanges");
        var generalTestsPath = Path.Combine(testDir, "NestedSaveChangesTests.cs");
        var transactionTestsPath = Path.Combine(testDir, "NestedSaveChangesTransactionTests.cs");

        Assert.True(File.Exists(transactionTestsPath), "LC039 transaction-boundary tests should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class NestedSaveChangesTests", generalTestsSource);
        Assert.DoesNotContain("TransactionBoundaryBetweenSaves_DoesNotTrigger", generalTestsSource);
        Assert.DoesNotContain("RepeatedSavesInsideAwaitUsingDeclarationTransaction_DoesNotTrigger", generalTestsSource);

        var transactionTestsSource = File.ReadAllText(transactionTestsPath);
        Assert.Contains("public partial class NestedSaveChangesTests", transactionTestsSource);
        Assert.Contains("TransactionBoundaryBetweenSaves_DoesNotTrigger", transactionTestsSource);
        Assert.Contains("RepeatedSavesInsideAwaitUsingDeclarationTransaction_DoesNotTrigger", transactionTestsSource);
    }

    [Fact]
    public void LC039_TransactionBoundaryClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC039_NestedSaveChanges");
        var analyzerPath = Path.Combine(analyzerDir, "NestedSaveChangesAnalyzer.cs");
        var transactionBoundaryPath = Path.Combine(analyzerDir, "NestedSaveChangesTransactionBoundaryClassification.cs");

        Assert.True(File.Exists(transactionBoundaryPath), "LC039 EF transaction-boundary classification should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool IsTransactionBoundaryInvocation", analyzerSource);
        Assert.DoesNotContain("private static bool IsEfCoreTransactionBoundaryType", analyzerSource);
        Assert.DoesNotContain("private static bool IsEfCoreTransactionBoundaryNamedType", analyzerSource);

        var transactionBoundarySource = File.ReadAllText(transactionBoundaryPath);
        Assert.Contains("private static bool IsTransactionBoundaryInvocation", transactionBoundarySource);
        Assert.Contains("private static bool IsEfCoreTransactionBoundaryType", transactionBoundarySource);
        Assert.Contains("private static bool IsEfCoreTransactionBoundaryNamedType", transactionBoundarySource);
    }

    [Fact]
    public void LC039_BranchExclusivity_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC039_NestedSaveChanges");
        var boundaryPath = Path.Combine(analyzerDir, "NestedSaveChangesBoundaryAnalysis.cs");
        var branchExclusivityPath = Path.Combine(analyzerDir, "NestedSaveChangesBranchExclusivity.cs");

        Assert.True(File.Exists(branchExclusivityPath), "LC039 mutually-exclusive branch detection should live in a focused partial file.");

        var boundarySource = File.ReadAllText(boundaryPath);
        Assert.Contains("private static bool HasTransactionBoundaryBetween", boundarySource);
        Assert.DoesNotContain("private static bool AreMutuallyExclusiveBranches", boundarySource);
        Assert.DoesNotContain("private static SyntaxNode? GetContainingTryBranch", boundarySource);
        Assert.DoesNotContain("private static StatementSyntax? GetContainingBranch", boundarySource);
        Assert.DoesNotContain("private static SwitchSectionSyntax? GetContainingSwitchSection", boundarySource);
        Assert.DoesNotContain("private static SwitchExpressionArmSyntax? GetContainingSwitchExpressionArm", boundarySource);

        var branchExclusivitySource = File.ReadAllText(branchExclusivityPath);
        Assert.Contains("private static bool AreMutuallyExclusiveBranches", branchExclusivitySource);
        Assert.Contains("private static SyntaxNode? GetContainingTryBranch", branchExclusivitySource);
        Assert.Contains("private static StatementSyntax? GetContainingBranch", branchExclusivitySource);
        Assert.Contains("private static SwitchSectionSyntax? GetContainingSwitchSection", branchExclusivitySource);
        Assert.Contains("private static SwitchExpressionArmSyntax? GetContainingSwitchExpressionArm", branchExclusivitySource);
    }

    [Fact]
    public void LC039_DiagnosticReporting_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC039_NestedSaveChanges");
        var analyzerPath = Path.Combine(analyzerDir, "NestedSaveChangesAnalyzer.cs");
        var reportingPath = Path.Combine(analyzerDir, "NestedSaveChangesReporting.cs");

        Assert.True(File.Exists(reportingPath), "LC039 compilation-end diagnostic grouping/reporting should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class NestedSaveChangesAnalyzer", analyzerSource);
        Assert.Contains("public void AnalyzeInvocation", analyzerSource);
        Assert.DoesNotContain("public void ReportDiagnostics", analyzerSource);

        var reportingSource = File.ReadAllText(reportingPath);
        Assert.Contains("public void ReportDiagnostics", reportingSource);
        Assert.Contains("GroupBy(record => record.Root", reportingSource);
        Assert.Contains("HasTransactionBoundaryBetween", reportingSource);
        Assert.Contains("AreMutuallyExclusiveBranches", reportingSource);
        Assert.Contains("AreInsideSameTransactionUsing", reportingSource);
        Assert.Contains("Diagnostic.Create(Rule", reportingSource);
    }

    [Fact]
    public void LC020_QueryableParameterTracing_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC020_StringContainsWithComparison");
        var analyzerPath = Path.Combine(analyzerDir, "StringContainsWithComparisonAnalyzer.cs");
        var queryableParametersPath = Path.Combine(analyzerDir, "StringContainsWithComparisonQueryableParameters.cs");

        Assert.True(File.Exists(queryableParametersPath), "LC020 queryable lambda parameter tracing should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class StringContainsWithComparisonAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static ImmutableArray<IParameterSymbol> GetQueryableExpressionLambdaParameters", analyzerSource);
        Assert.DoesNotContain("private static ImmutableArray<IParameterSymbol> GetQueryDependentLambdaParameters", analyzerSource);
        Assert.DoesNotContain("private static bool LambdaSourceDependsOnParameters", analyzerSource);
        Assert.DoesNotContain("private static bool IsQueryableInvocation", analyzerSource);

        var queryableParametersSource = File.ReadAllText(queryableParametersPath);
        Assert.Contains("private static ImmutableArray<IParameterSymbol> GetQueryableExpressionLambdaParameters", queryableParametersSource);
        Assert.Contains("private static ImmutableArray<IParameterSymbol> GetQueryDependentLambdaParameters", queryableParametersSource);
        Assert.Contains("private static bool LambdaSourceDependsOnParameters", queryableParametersSource);
        Assert.Contains("private static bool IsQueryableInvocation", queryableParametersSource);
    }

    [Fact]
    public void LC024_ProjectionAccessScanning_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC024_GroupByNonTranslatable");
        var analyzerPath = Path.Combine(analyzerDir, "GroupByNonTranslatableAnalyzer.cs");
        var projectionAccessPath = Path.Combine(analyzerDir, "GroupByNonTranslatableProjectionAccess.cs");

        Assert.True(File.Exists(projectionAccessPath), "LC024 projection access scanning/reporting should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class GroupByNonTranslatableAnalyzer", analyzerSource);
        Assert.Contains("private void AnalyzeSelectProjection", analyzerSource);
        Assert.Contains("private void AnalyzeGroupByResultSelector", analyzerSource);
        Assert.DoesNotContain("private void CheckOperationForNonTranslatableAccess", analyzerSource);
        Assert.DoesNotContain("private static System.Collections.Generic.IEnumerable<IOperation> GetAllOperations", analyzerSource);

        var projectionAccessSource = File.ReadAllText(projectionAccessPath);
        Assert.Contains("private static void CheckOperationForNonTranslatableAccess", projectionAccessSource);
        Assert.Contains("private static System.Collections.Generic.IEnumerable<IOperation> GetAllOperations", projectionAccessSource);
        Assert.Contains("private static void ReportNonTranslatableAccess", projectionAccessSource);
        Assert.Contains("Diagnostic.Create(Rule", projectionAccessSource);
    }

    [Fact]
    public void LC014_QueryableLambdaScope_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC014_AvoidStringCaseConversion");
        var analyzerPath = Path.Combine(analyzerDir, "AvoidStringCaseConversionAnalyzer.cs");
        var queryableLambdaScopePath = Path.Combine(analyzerDir, "AvoidStringCaseConversionQueryableLambdaScope.cs");

        Assert.True(File.Exists(queryableLambdaScopePath), "LC014 queryable lambda scope and target method classification should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AvoidStringCaseConversionAnalyzer", analyzerSource);
        Assert.Contains("private void AnalyzeInvocation", analyzerSource);
        Assert.DoesNotContain("private ImmutableArray<IParameterSymbol> GetEnclosingQueryableLambdaParameters", analyzerSource);
        Assert.DoesNotContain("private static bool IsTargetQueryableMethod", analyzerSource);
        Assert.DoesNotContain("private static bool IsLambdaScopedToEntityFrameworkSource", analyzerSource);
        Assert.DoesNotContain("TargetLinqMethods", analyzerSource);
        Assert.DoesNotContain("TargetEfAsyncPredicateMethods", analyzerSource);

        var queryableLambdaScopeSource = File.ReadAllText(queryableLambdaScopePath);
        Assert.Contains("private static ImmutableArray<IParameterSymbol> GetEnclosingQueryableLambdaParameters", queryableLambdaScopeSource);
        Assert.Contains("private static bool IsTargetQueryableMethod", queryableLambdaScopeSource);
        Assert.Contains("private static bool IsLambdaScopedToEntityFrameworkSource", queryableLambdaScopeSource);
        Assert.Contains("TargetLinqMethods", queryableLambdaScopeSource);
        Assert.Contains("TargetEfAsyncPredicateMethods", queryableLambdaScopeSource);
        Assert.Contains("HasEntityFrameworkQuerySource", queryableLambdaScopeSource);
    }

    [Fact]
    public void LC045_EntityLocalCollection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var usageAnalysisPath = Path.Combine(analyzerDir, "MissingIncludeUsageAnalysis.cs");
        var entityLocalCollectionPath = Path.Combine(analyzerDir, "MissingIncludeEntityLocalCollection.cs");

        Assert.True(File.Exists(entityLocalCollectionPath), "LC045 materialized-result entity local collection should live in a focused partial file.");

        var usageAnalysisSource = File.ReadAllText(usageAnalysisPath);
        Assert.Contains("private static List<NavigationAccess>? CollectNavigationAccesses", usageAnalysisSource);
        Assert.DoesNotContain("private static HashSet<ILocalSymbol> CollectEntityLocals", usageAnalysisSource);
        Assert.Contains("var entityLocals = CollectEntityLocals", usageAnalysisSource);

        var entityLocalCollectionSource = File.ReadAllText(entityLocalCollectionPath);
        Assert.Contains("private static HashSet<ILocalSymbol> CollectEntityLocals", entityLocalCollectionSource);
        Assert.Contains("IForEachLoopOperation", entityLocalCollectionSource);
        Assert.Contains("IsIndexedAccessOf", entityLocalCollectionSource);
        Assert.Contains("LocalAssignmentCache.GetAssignments", entityLocalCollectionSource);
    }

    [Fact]
    public void LC025_FixerNoTrackingSourceTracing_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC025_AsNoTrackingWithUpdate");
        var sourceResolutionPath = Path.Combine(fixerDir, "AsNoTrackingWithUpdateFixerSourceResolution.cs");
        var noTrackingSourcePath = Path.Combine(fixerDir, "AsNoTrackingWithUpdateFixerNoTrackingSource.cs");

        Assert.True(File.Exists(noTrackingSourcePath), "LC025 fixer recursive no-tracking source tracing should live in a focused partial file.");

        var sourceResolutionSource = File.ReadAllText(sourceResolutionPath);
        Assert.Contains("private static InvocationExpressionSyntax? FindAsNoTrackingOrigin", sourceResolutionSource);
        Assert.DoesNotContain("private static bool IsNoTrackingSource", sourceResolutionSource);
        Assert.DoesNotContain("private static bool IsLocalFromNoTracking", sourceResolutionSource);
        Assert.Contains("IsConditionalRelativeTo", sourceResolutionSource);

        var noTrackingSource = File.ReadAllText(noTrackingSourcePath);
        Assert.Contains("private static bool IsNoTrackingSource", noTrackingSource);
        Assert.Contains("private static bool IsLocalFromNoTracking", noTrackingSource);
        Assert.Contains("HasAsNoTrackingInChain", noTrackingSource);
        Assert.Contains("IsMaterializerMethod", noTrackingSource);
        Assert.Contains("new AsNoTrackingOrigin", noTrackingSource);
    }

    [Fact]
    public void LC025_AnalyzerNoTrackingSourceTracing_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC025_AsNoTrackingWithUpdate");
        var sourceResolutionPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateSourceResolution.cs");
        var noTrackingSourcePath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateNoTrackingSource.cs");

        Assert.True(File.Exists(noTrackingSourcePath), "LC025 analyzer no-tracking source evaluation should live in a focused partial file.");

        var sourceResolutionSource = File.ReadAllText(sourceResolutionPath);
        Assert.Contains("private bool IsFromNoTrackingQuery", sourceResolutionSource);
        Assert.DoesNotContain("private bool IsNoTrackingSource", sourceResolutionSource);
        Assert.DoesNotContain("private static IEnumerable<IOperation> EnumerateOperations", sourceResolutionSource);

        var noTrackingSource = File.ReadAllText(noTrackingSourcePath);
        Assert.Contains("private bool IsNoTrackingSource", noTrackingSource);
        Assert.Contains("private static IEnumerable<IOperation> EnumerateOperations", noTrackingSource);
        Assert.Contains("IsAsNoTrackingQuery", noTrackingSource);
        Assert.Contains("IsMaterializerMethod", noTrackingSource);
    }

    [Fact]
    public void LC040_DiagnosticReporting_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC040_MixedTrackingAndNoTracking");
        var analyzerPath = Path.Combine(analyzerDir, "MixedTrackingAndNoTrackingAnalyzer.cs");
        var reportingPath = Path.Combine(analyzerDir, "MixedTrackingAndNoTrackingReporting.cs");

        Assert.True(File.Exists(reportingPath), "LC040 compilation-end diagnostic grouping/reporting should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class MixedTrackingAndNoTrackingAnalyzer", analyzerSource);
        Assert.DoesNotContain("public void ReportDiagnostics", analyzerSource);
        Assert.Contains("public void AnalyzeInvocation", analyzerSource);

        var reportingSource = File.ReadAllText(reportingPath);
        Assert.Contains("public void ReportDiagnostics", reportingSource);
        Assert.Contains("GroupBy(record => record.Root", reportingSource);
        Assert.Contains("AreMutuallyExclusiveBranches", reportingSource);
        Assert.Contains("Diagnostic.Create(Rule", reportingSource);
    }

    [Fact]
    public void LC043_BufferedLocalPattern_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC043_AsyncEnumerableBuffering");
        var analyzerPath = Path.Combine(analyzerDir, "AsyncEnumerableBufferingAnalyzer.cs");
        var patternPath = Path.Combine(analyzerDir, "AsyncEnumerableBufferingPattern.cs");

        Assert.True(File.Exists(patternPath), "LC043 buffered-local syntax matching should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AsyncEnumerableBufferingAnalyzer", analyzerSource);
        Assert.DoesNotContain("internal static bool TryGetImmediateBufferedLocal", analyzerSource);
        Assert.DoesNotContain("internal sealed class BufferInfo", analyzerSource);

        var patternSource = File.ReadAllText(patternPath);
        Assert.Contains("internal static bool TryGetImmediateBufferedLocal", patternSource);
        Assert.Contains("internal sealed class BufferInfo", patternSource);
    }

    [Fact]
    public void LC043_FixerLoopDiscovery_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC043_AsyncEnumerableBuffering");
        var fixerPath = Path.Combine(analyzerDir, "AsyncEnumerableBufferingFixer.cs");
        var loopDiscoveryPath = Path.Combine(analyzerDir, "AsyncEnumerableBufferingFixerLoopDiscovery.cs");

        Assert.True(File.Exists(loopDiscoveryPath), "LC043 fixer loop/declaration discovery should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class AsyncEnumerableBufferingFixer", fixerSource);
        Assert.DoesNotContain("private static bool TryGetContainingLoopAndDeclaration", fixerSource);

        var loopDiscoverySource = File.ReadAllText(loopDiscoveryPath);
        Assert.Contains("private static bool TryGetContainingLoopAndDeclaration", loopDiscoverySource);
    }

    [Fact]
    public void LC044_TrackedStateEdgeCases_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC044_AsNoTrackingThenModify");
        var edgeCasesPath = Path.Combine(testDir, "AsNoTrackingThenModifyEdgeCasesTests.cs");
        var trackedStateTestsPath = Path.Combine(testDir, "AsNoTrackingThenModifyTrackedStateTests.cs");

        Assert.True(File.Exists(trackedStateTestsPath), "LC044 attach/update/state-reset edge cases should live in a focused partial test file.");

        var edgeCasesSource = File.ReadAllText(edgeCasesPath);
        Assert.Contains("public partial class AsNoTrackingThenModifyEdgeCasesTests", edgeCasesSource);
        Assert.DoesNotContain("AsNoTracking_Mutate_ContextUpdate_ThenSave_DoesNotTrigger", edgeCasesSource);
        Assert.DoesNotContain("AsNoTracking_ContextAttach_ThenChangeTrackerClear_ThenMutate_ThenSave_StillTriggers", edgeCasesSource);

        var trackedStateTestsSource = File.ReadAllText(trackedStateTestsPath);
        Assert.Contains("public partial class AsNoTrackingThenModifyEdgeCasesTests", trackedStateTestsSource);
        Assert.Contains("AsNoTracking_Mutate_ContextUpdate_ThenSave_DoesNotTrigger", trackedStateTestsSource);
        Assert.Contains("AsNoTracking_ContextAttach_ThenChangeTrackerClear_ThenMutate_ThenSave_StillTriggers", trackedStateTestsSource);
    }

    [Fact]
    public void LC009_SetGenericSourceTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC009_MissingAsNoTracking");
        var generalTestsPath = Path.Combine(testDir, "MissingAsNoTrackingTests.cs");
        var setSourceTestsPath = Path.Combine(testDir, "MissingAsNoTrackingSetSourceTests.cs");

        Assert.True(File.Exists(setSourceTestsPath), "LC009 DbContext.Set<T> query-source coverage should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class MissingAsNoTrackingTests", generalTestsSource);
        Assert.DoesNotContain("TestCrime_SetGenericSource_ReadOnly_TriggersDiagnostic", generalTestsSource);
        Assert.DoesNotContain("TestInnocent_SetGenericSource_WithSelect_NoDiagnostic", generalTestsSource);

        var setSourceTestsSource = File.ReadAllText(setSourceTestsPath);
        Assert.Contains("public partial class MissingAsNoTrackingTests", setSourceTestsSource);
        Assert.Contains("TestCrime_SetGenericSource_ReadOnly_TriggersDiagnostic", setSourceTestsSource);
        Assert.Contains("TestInnocent_SetGenericSource_WithSelect_NoDiagnostic", setSourceTestsSource);
    }

    [Fact]
    public void LC009_ResultLocalResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC009_MissingAsNoTracking");
        var mutationDetectionPath = Path.Combine(analyzerDir, "MissingAsNoTrackingMutationDetection.cs");
        var resultLocalPath = Path.Combine(analyzerDir, "MissingAsNoTrackingResultLocal.cs");

        Assert.True(File.Exists(resultLocalPath), "LC009 materialized-result local and wrapper resolution should live in a focused partial file.");

        var mutationDetectionSource = File.ReadAllText(mutationDetectionPath);
        Assert.Contains("private static bool MaterializedEntityIsMutated", mutationDetectionSource);
        Assert.DoesNotContain("private static ILocalSymbol? FindResultLocal", mutationDetectionSource);
        Assert.DoesNotContain("private static IOperation? WalkUpThroughWrappers", mutationDetectionSource);

        var resultLocalSource = File.ReadAllText(resultLocalPath);
        Assert.Contains("private static ILocalSymbol? FindResultLocal", resultLocalSource);
        Assert.Contains("private static IOperation? WalkUpThroughWrappers", resultLocalSource);
        Assert.DoesNotContain("private static bool MaterializedEntityIsMutated", resultLocalSource);
    }

    [Fact]
    public void LC007_IgnoredSourceTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC007_NPlusOneLooper");
        var generalTestsPath = Path.Combine(testDir, "NPlusOneLooperTests.cs");
        var ignoredSourceTestsPath = Path.Combine(testDir, "NPlusOneLooperIgnoredSourceTests.cs");

        Assert.True(File.Exists(ignoredSourceTestsPath), "LC007 ignored and ambiguous query-source coverage should live in a focused partial test file.");

        var generalTestsSource = File.ReadAllText(generalTestsPath);
        Assert.Contains("public partial class NPlusOneLooperTests", generalTestsSource);
        Assert.DoesNotContain("InMemoryAsQueryable_IsIgnored", generalTestsSource);
        Assert.DoesNotContain("InvocationInsideLambdaDeclaredInLoop_IsIgnored", generalTestsSource);

        var ignoredSourceTestsSource = File.ReadAllText(ignoredSourceTestsPath);
        Assert.Contains("public partial class NPlusOneLooperTests", ignoredSourceTestsSource);
        Assert.Contains("InMemoryAsQueryable_IsIgnored", ignoredSourceTestsSource);
        Assert.Contains("InvocationInsideLambdaDeclaredInLoop_IsIgnored", ignoredSourceTestsSource);
    }

    [Fact]
    public void LC045_EscapedResultEdgeCaseTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC045_MissingInclude");
        var edgeCasesPath = Path.Combine(testDir, "MissingIncludeEdgeCasesTests.cs");
        var escapedResultTestsPath = Path.Combine(testDir, "MissingIncludeEscapedResultTests.cs");

        Assert.True(File.Exists(escapedResultTestsPath), "LC045 escaped or ambiguous result edge cases should live in a focused partial test file.");

        var edgeCasesSource = File.ReadAllText(edgeCasesPath);
        Assert.Contains("public partial class MissingIncludeEdgeCasesTests", edgeCasesSource);
        Assert.DoesNotContain("TestInnocent_ResultPassedAsArgument_NoDiagnostic", edgeCasesSource);
        Assert.DoesNotContain("TestInnocent_IndexedEntityPassedAsArgument_NoDiagnostic", edgeCasesSource);

        var escapedResultTestsSource = File.ReadAllText(escapedResultTestsPath);
        Assert.Contains("public partial class MissingIncludeEdgeCasesTests", escapedResultTestsSource);
        Assert.Contains("TestInnocent_ResultPassedAsArgument_NoDiagnostic", escapedResultTestsSource);
        Assert.Contains("TestInnocent_IndexedEntityPassedAsArgument_NoDiagnostic", escapedResultTestsSource);
    }

    [Fact]
    public void LC045_OriginAwareFlowTests_LiveInDedicatedPartial()
    {
        var testDir = Path.Combine(
            _repoRoot,
            "tests",
            "LinqContraband.Tests",
            "Analyzers",
            "LC045_MissingInclude");
        var escapedResultTestsPath = Path.Combine(testDir, "MissingIncludeEscapedResultTests.cs");
        var originFlowTestsPath = Path.Combine(testDir, "MissingIncludeOriginFlowTests.cs");
        var uncertainReadTestsPath = Path.Combine(testDir, "MissingIncludeOriginFlowUncertainReadTests.cs");

        Assert.True(File.Exists(originFlowTestsPath), "LC045 origin-aware control-flow coverage should live in a focused partial test file.");
        Assert.True(File.Exists(uncertainReadTestsPath), "LC045 uncertain-read control-flow guardrails should live in a focused partial test file.");

        var escapedResultTestsSource = File.ReadAllText(escapedResultTestsPath);
        Assert.DoesNotContain("TestCrime_OriginFlow_", escapedResultTestsSource);
        Assert.DoesNotContain("TestInnocent_OriginFlow_", escapedResultTestsSource);

        var originFlowTestsSource = File.ReadAllText(originFlowTestsPath);
        Assert.Contains("public partial class MissingIncludeEdgeCasesTests", originFlowTestsSource);
        Assert.Contains("TestCrime_OriginFlow_ReadBeforeHelperEscape_StillReports", originFlowTestsSource);
        Assert.Contains("TestCrime_OriginFlow_OneBranchNavigationAssignment_StillReports", originFlowTestsSource);
        Assert.Contains("TestCrime_OriginFlow_WriteOnDifferentEntity_DoesNotSatisfyRead", originFlowTestsSource);
        Assert.Contains("TestCrime_OriginFlow_LoopReadBeforeWrite_StillReports", originFlowTestsSource);
        Assert.DoesNotContain("TestInnocent_OriginFlow_HelperEscapeBeforeRead_NoDiagnostic", originFlowTestsSource);

        var uncertainReadTestsSource = File.ReadAllText(uncertainReadTestsPath);
        Assert.Contains("public partial class MissingIncludeEdgeCasesTests", uncertainReadTestsSource);
        Assert.Contains("TestInnocent_OriginFlow_HelperEscapeBeforeRead_NoDiagnostic", uncertainReadTestsSource);
        Assert.Contains("TestInnocent_OriginFlow_BothBranchesAssignNavigationBeforeRead_NoDiagnostic", uncertainReadTestsSource);
    }

    [Fact]
    public void LC031_QuerySourceResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC031_UnboundedQueryMaterialization");
        var analyzerPath = Path.Combine(analyzerDir, "UnboundedQueryMaterializationAnalyzer.cs");
        var querySourcePath = Path.Combine(analyzerDir, "UnboundedQueryMaterializationQuerySource.cs");

        Assert.True(File.Exists(querySourcePath), "LC031 DbSet/query-source chain resolution should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class UnboundedQueryMaterializationAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static QuerySourceResolution ResolveQuerySource", analyzerSource);
        Assert.DoesNotContain("private readonly struct QuerySourceResolution", analyzerSource);
        Assert.DoesNotContain("private static bool IsDbContextSetInvocation", analyzerSource);
        Assert.DoesNotContain("private static bool TryResolveSingleAssignedValue", analyzerSource);

        var querySource = File.ReadAllText(querySourcePath);
        Assert.Contains("private static QuerySourceResolution ResolveQuerySource", querySource);
        Assert.Contains("private readonly struct QuerySourceResolution", querySource);
        Assert.Contains("private static bool IsDbContextSetInvocation", querySource);
        Assert.Contains("private static bool TryResolveSingleAssignedValue", querySource);
    }

    [Fact]
    public void LC003_ExistenceComparisonRecognition_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC003_AnyOverCount");
        var analyzerPath = Path.Combine(analyzerDir, "AnyOverCountAnalyzer.cs");
        var comparisonPath = Path.Combine(analyzerDir, "AnyOverCountExistenceComparison.cs");

        Assert.True(File.Exists(comparisonPath), "LC003 Count comparison recognition should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AnyOverCountAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool TryGetCountExistenceCheck", analyzerSource);
        Assert.DoesNotContain("private static bool IsInvocation", analyzerSource);
        Assert.DoesNotContain("private static bool IsConstant", analyzerSource);
        Assert.DoesNotContain("private static bool IsZero", analyzerSource);
        Assert.DoesNotContain("private static bool IsOne", analyzerSource);

        var comparisonSource = File.ReadAllText(comparisonPath);
        Assert.Contains("private static bool TryGetCountExistenceCheck", comparisonSource);
        Assert.Contains("private static bool IsInvocation", comparisonSource);
        Assert.Contains("private static bool IsConstant", comparisonSource);
        Assert.Contains("private static bool IsZero", comparisonSource);
        Assert.Contains("private static bool IsOne", comparisonSource);
    }

    [Fact]
    public void LC025_AnalyzerTrackingDirectiveAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC025_AsNoTrackingWithUpdate");
        var analyzerPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateAnalyzer.cs");
        var trackingDirectivePath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateTrackingDirectiveAnalysis.cs");

        Assert.True(File.Exists(trackingDirectivePath), "LC025 analyzer tracking-directive chain analysis should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AsNoTrackingWithUpdateAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsAsNoTrackingQuery", analyzerSource);
        Assert.DoesNotContain("private static bool HasAsNoTrackingInChain", analyzerSource);
        Assert.DoesNotContain("private static bool IsProjectionToConstructedObject", analyzerSource);
        Assert.DoesNotContain("private static bool IsEfCoreNoTrackingDirective", analyzerSource);
        Assert.DoesNotContain("private static bool IsEfCoreAsTracking", analyzerSource);

        var trackingDirectiveSource = File.ReadAllText(trackingDirectivePath);
        Assert.Contains("private static bool IsAsNoTrackingQuery", trackingDirectiveSource);
        Assert.Contains("private static bool HasAsNoTrackingInChain", trackingDirectiveSource);
        Assert.Contains("private static bool IsProjectionToConstructedObject", trackingDirectiveSource);
        Assert.Contains("private static bool IsEfCoreNoTrackingDirective", trackingDirectiveSource);
        Assert.Contains("private static bool IsEfCoreAsTracking", trackingDirectiveSource);
    }

    [Fact]
    public void LC032_FixerAsyncSupportDetection_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var saveChangesModePath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixerSaveChangesMode.cs");
        var asyncSupportPath = Path.Combine(fixerDir, "ExecuteUpdateForBulkUpdatesFixerAsyncSupport.cs");

        Assert.True(File.Exists(asyncSupportPath), "LC032 fixer ExecuteUpdateAsync capability detection should live in a focused partial file.");

        var saveChangesModeSource = File.ReadAllText(saveChangesModePath);
        Assert.DoesNotContain("private static bool HasExecuteUpdateAsyncTokenOverload", saveChangesModeSource);
        Assert.DoesNotContain("private static bool HasExecuteUpdateAsyncSupport", saveChangesModeSource);
        Assert.DoesNotContain("private static bool HasExecuteUpdateAsyncMethod", saveChangesModeSource);
        Assert.DoesNotContain("private static bool IsExecuteUpdateAsyncLikeMethod", saveChangesModeSource);
        Assert.DoesNotContain("private static bool IsEntityFrameworkCoreNamespace", saveChangesModeSource);

        var asyncSupportSource = File.ReadAllText(asyncSupportPath);
        Assert.Contains("private static bool HasExecuteUpdateAsyncTokenOverload", asyncSupportSource);
        Assert.Contains("private static bool HasExecuteUpdateAsyncSupport", asyncSupportSource);
        Assert.Contains("private static bool HasExecuteUpdateAsyncMethod", asyncSupportSource);
        Assert.Contains("private static bool IsExecuteUpdateAsyncLikeMethod", asyncSupportSource);
        Assert.Contains("private static bool IsEntityFrameworkCoreNamespace", asyncSupportSource);
    }

    [Fact]
    public void LC032_LocalWriteDetection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var statementAnalysisPath = Path.Combine(analyzerDir, "ExecuteUpdateForBulkUpdatesStatementAnalysis.cs");
        var localWriteDetectionPath = Path.Combine(analyzerDir, "ExecuteUpdateForBulkUpdatesLocalWriteDetection.cs");

        Assert.True(File.Exists(localWriteDetectionPath), "LC032 local write detection should live in a focused analyzer partial file.");

        var statementAnalysisSource = File.ReadAllText(statementAnalysisPath);
        Assert.DoesNotContain("private static bool HasLocalWrites", statementAnalysisSource);

        var localWriteDetectionSource = File.ReadAllText(localWriteDetectionPath);
        Assert.Contains("private static bool HasLocalWrites", localWriteDetectionSource);
    }

    [Fact]
    public void LC011_AssemblyUsingScopeTraversal_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var visibilityPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyUsingVisibility.cs");
        var usingScopePath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyUsingScope.cs");

        Assert.True(File.Exists(usingScopePath), "LC011 assembly using-scope traversal should live in a focused partial file.");

        var visibilitySource = File.ReadAllText(visibilityPath);
        Assert.DoesNotContain("private static bool AnyUsingDirectiveInScope", visibilitySource);
        Assert.DoesNotContain("foreach (var namespaceDeclaration in node.Ancestors().OfType<NamespaceDeclarationSyntax>())", visibilitySource);
        Assert.DoesNotContain("foreach (var fileScopedNamespace in node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>())", visibilitySource);

        var usingScopeSource = File.ReadAllText(usingScopePath);
        Assert.Contains("private static bool AnyUsingDirectiveInScope", usingScopeSource);
        Assert.Contains("foreach (var namespaceDeclaration in node.Ancestors().OfType<NamespaceDeclarationSyntax>())", usingScopeSource);
        Assert.Contains("foreach (var fileScopedNamespace in node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>())", usingScopeSource);
    }

    [Fact]
    public void LC011_AssemblyMemberResolution_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var localResolutionPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyLocalResolution.cs");
        var memberResolutionPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAssemblyMemberResolution.cs");

        Assert.True(File.Exists(memberResolutionPath), "LC011 assembly member initializer resolution should live in a focused partial file.");

        var localResolutionSource = File.ReadAllText(localResolutionPath);
        Assert.DoesNotContain("private static bool TryResolveMemberCurrentAssembly", localResolutionSource);

        var memberResolutionSource = File.ReadAllText(memberResolutionPath);
        Assert.Contains("private static bool TryResolveMemberCurrentAssembly", memberResolutionSource);
        Assert.Contains("foreach (var member in members)", memberResolutionSource);
        Assert.Contains("DeclaringSyntaxReferences", memberResolutionSource);
    }

    [Fact]
    public void LC016_FixerExpressionBodyClockAccessDiscovery_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC016_AvoidDateTimeNow");
        var expressionBodyPath = Path.Combine(fixerDir, "AvoidDateTimeNowFixerExpressionBody.cs");
        var clockAccessPath = Path.Combine(fixerDir, "AvoidDateTimeNowFixerExpressionBodyClockAccesses.cs");

        Assert.True(File.Exists(clockAccessPath), "LC016 expression-bodied clock access discovery should live in a focused partial file.");

        var expressionBodySource = File.ReadAllText(expressionBodyPath);
        Assert.DoesNotContain("private static IReadOnlyList<ClockReplacement> BuildExpressionBodyReplacements", expressionBodySource);
        Assert.DoesNotContain("private static ClockReplacement? FindReplacementFor", expressionBodySource);
        Assert.DoesNotContain("private static IEnumerable<MemberAccessExpressionSyntax> FindExpressionBodyClockAccesses", expressionBodySource);
        Assert.DoesNotContain("private static bool IsClockPropertyAccess", expressionBodySource);
        Assert.DoesNotContain("private sealed class ClockReplacement", expressionBodySource);

        var clockAccessSource = File.ReadAllText(clockAccessPath);
        Assert.Contains("private static IReadOnlyList<ClockReplacement> BuildExpressionBodyReplacements", clockAccessSource);
        Assert.Contains("private static ClockReplacement? FindReplacementFor", clockAccessSource);
        Assert.Contains("private static IEnumerable<MemberAccessExpressionSyntax> FindExpressionBodyClockAccesses", clockAccessSource);
        Assert.Contains("private static bool IsClockPropertyAccess", clockAccessSource);
        Assert.Contains("private sealed class ClockReplacement", clockAccessSource);
    }

    [Fact]
    public void LC007_FixerIncludeRewrite_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC007_NPlusOneLooper");
        var syntaxAnalysisPath = Path.Combine(fixerDir, "NPlusOneLooperFixerSyntaxAnalysis.cs");
        var includeRewritePath = Path.Combine(fixerDir, "NPlusOneLooperFixerIncludeRewrite.cs");

        Assert.True(File.Exists(includeRewritePath), "LC007 fixer Include rewrite construction should live in a focused partial file.");

        var syntaxAnalysisSource = File.ReadAllText(syntaxAnalysisPath);
        Assert.DoesNotContain("private static bool TryAddInclude", syntaxAnalysisSource);

        var includeRewriteSource = File.ReadAllText(includeRewritePath);
        Assert.Contains("private static bool TryAddInclude", includeRewriteSource);
        Assert.Contains("SyntaxFactory.InvocationExpression", includeRewriteSource);
        Assert.Contains("SyntaxFactory.IdentifierName(\"Include\")", includeRewriteSource);
    }

    [Fact]
    public void LC010_FreshContextExemption_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC010_SaveChangesInLoop");
        var analyzerPath = Path.Combine(analyzerDir, "SaveChangesInLoopAnalyzer.cs");
        var freshContextPath = Path.Combine(analyzerDir, "SaveChangesInLoopFreshContext.cs");

        Assert.True(File.Exists(freshContextPath), "LC010 fresh DbContext-in-loop exemption should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool IsSaveReceiverFreshContextDeclaredInsideLoopBody", analyzerSource);
        Assert.DoesNotContain("private static bool IsLocalWrittenBeforeOperation", analyzerSource);
        Assert.DoesNotContain("private static bool IsLocalReference", analyzerSource);

        var freshContextSource = File.ReadAllText(freshContextPath);
        Assert.Contains("private static bool IsSaveReceiverFreshContextDeclaredInsideLoopBody", freshContextSource);
        Assert.Contains("private static bool IsLocalWrittenBeforeOperation", freshContextSource);
        Assert.Contains("private static bool IsLocalReference", freshContextSource);
    }

    [Fact]
    public void LC033_ToHashSetInvocationClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC033_UseFrozenSetForStaticMembershipCaches");
        var initializerPath = Path.Combine(analyzerDir, "UseFrozenSetForStaticMembershipCachesInitializerClassification.cs");
        var toHashSetPath = Path.Combine(analyzerDir, "UseFrozenSetForStaticMembershipCachesToHashSetClassification.cs");

        Assert.True(File.Exists(toHashSetPath), "LC033 ToHashSet invocation classification should live in a focused partial file.");

        var initializerSource = File.ReadAllText(initializerPath);
        Assert.DoesNotContain("private static bool IsSupportedToHashSetInvocation", initializerSource);
        Assert.DoesNotContain("private static bool IsStaticTypeOrNamespaceAccess", initializerSource);
        Assert.DoesNotContain("private static bool IsTypeOrNamespaceSymbol", initializerSource);

        var toHashSetSource = File.ReadAllText(toHashSetPath);
        Assert.Contains("private static bool IsSupportedToHashSetInvocation", toHashSetSource);
        Assert.Contains("private static bool IsStaticTypeOrNamespaceAccess", toHashSetSource);
        Assert.Contains("private static bool IsTypeOrNamespaceSymbol", toHashSetSource);
    }

    [Fact]
    public void LC022_GroupingQueryableExemption_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC022_ToListInSelectProjection");
        var analyzerPath = Path.Combine(analyzerDir, "ToListInSelectProjectionAnalyzer.cs");
        var groupingPath = Path.Combine(analyzerDir, "ToListInSelectProjectionGrouping.cs");

        Assert.True(File.Exists(groupingPath), "LC022 grouping-queryable exemption should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class ToListInSelectProjectionAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsGroupingQueryable", analyzerSource);
        Assert.DoesNotContain("private static ITypeSymbol? GetQueryableElementType", analyzerSource);
        Assert.DoesNotContain("private static bool IsGroupingType", analyzerSource);

        var groupingSource = File.ReadAllText(groupingPath);
        Assert.Contains("private static bool IsGroupingQueryable", groupingSource);
        Assert.Contains("private static ITypeSymbol? GetQueryableElementType", groupingSource);
        Assert.Contains("private static bool IsGroupingType", groupingSource);
    }

    [Fact]
    public void LC003_FixerZeroConstantDetection_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC003_AnyOverCount");
        var fixerPath = Path.Combine(fixerDir, "AnyOverCountFixer.cs");
        var zeroConstantPath = Path.Combine(fixerDir, "AnyOverCountFixerZeroConstant.cs");

        Assert.True(File.Exists(zeroConstantPath), "LC003 fixer zero-constant detection should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class AnyOverCountFixer", fixerSource);
        Assert.DoesNotContain("private static bool HasZeroConstant", fixerSource);
        Assert.DoesNotContain("private static bool IsZeroConstant", fixerSource);
        Assert.DoesNotContain("private static bool IsZeroValue", fixerSource);

        var zeroConstantSource = File.ReadAllText(zeroConstantPath);
        Assert.Contains("private static bool HasZeroConstant", zeroConstantSource);
        Assert.Contains("private static bool IsZeroConstant", zeroConstantSource);
        Assert.Contains("private static bool IsZeroValue", zeroConstantSource);
    }

    [Fact]
    public void LC027_FixerPrimaryKeyDiscovery_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC027_MissingExplicitForeignKey");
        var fixerPath = Path.Combine(fixerDir, "MissingExplicitForeignKeyFixer.cs");
        var primaryKeyPath = Path.Combine(fixerDir, "MissingExplicitForeignKeyFixerPrimaryKeyDiscovery.cs");

        Assert.True(File.Exists(primaryKeyPath), "LC027 fixer primary-key discovery should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class MissingExplicitForeignKeyFixer", fixerSource);
        Assert.DoesNotContain("private static IPropertySymbol? TryFindConventionPrimaryKey", fixerSource);
        Assert.DoesNotContain("private static IPropertySymbol? TryFindConfiguredPrimaryKey", fixerSource);
        Assert.DoesNotContain("private static bool TryGetEntityTypeBuilderEntity", fixerSource);

        var primaryKeySource = File.ReadAllText(primaryKeyPath);
        Assert.Contains("private static IPropertySymbol? TryFindConventionPrimaryKey", primaryKeySource);
        Assert.Contains("private static IPropertySymbol? TryFindConfiguredPrimaryKey", primaryKeySource);
        Assert.Contains("private static bool TryGetEntityTypeBuilderEntity", primaryKeySource);
    }

    [Fact]
    public void LC016_QueryableScopeDetection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC016_AvoidDateTimeNow");
        var analyzerPath = Path.Combine(analyzerDir, "AvoidDateTimeNowAnalyzer.cs");
        var queryableScopePath = Path.Combine(analyzerDir, "AvoidDateTimeNowQueryableScope.cs");

        Assert.True(File.Exists(queryableScopePath), "LC016 queryable-lambda scope detection should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AvoidDateTimeNowAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static IAnonymousFunctionOperation? FindQueryableLambda", analyzerSource);
        Assert.DoesNotContain("private static bool IsTargetQueryableInvocation", analyzerSource);
        Assert.DoesNotContain("private static IAnonymousFunctionOperation? FindEnclosingLambda", analyzerSource);
        Assert.DoesNotContain("private static IEnumerable<IOperation> EnumerateOperations", analyzerSource);

        var queryableScopeSource = File.ReadAllText(queryableScopePath);
        Assert.Contains("private static IAnonymousFunctionOperation? FindQueryableLambda", queryableScopeSource);
        Assert.Contains("private static bool IsTargetQueryableInvocation", queryableScopeSource);
        Assert.Contains("private static IAnonymousFunctionOperation? FindEnclosingLambda", queryableScopeSource);
        Assert.Contains("private static IEnumerable<IOperation> EnumerateOperations", queryableScopeSource);
    }

    [Fact]
    public void LC027_TypeIndexConstruction_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC027_MissingExplicitForeignKey");
        var typeLookupPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyTypeLookup.cs");
        var typeIndexPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyTypeIndex.cs");

        Assert.True(File.Exists(typeIndexPath), "LC027 type-index construction should live in a focused partial file.");

        var typeLookupSource = File.ReadAllText(typeLookupPath);
        Assert.Contains("private sealed partial class CompilationModel", typeLookupSource);
        Assert.DoesNotContain("private TypeIndex BuildTypeIndex", typeLookupSource);
        Assert.DoesNotContain("private sealed class TypeIndex", typeLookupSource);
        Assert.DoesNotContain("private static void AddNamespaceTypes", typeLookupSource);
        Assert.DoesNotContain("private static void AddTypeAndNestedTypes", typeLookupSource);
        Assert.DoesNotContain("private static void AddLookupName", typeLookupSource);

        var typeIndexSource = File.ReadAllText(typeIndexPath);
        Assert.Contains("private TypeIndex BuildTypeIndex", typeIndexSource);
        Assert.Contains("private sealed class TypeIndex", typeIndexSource);
        Assert.Contains("private static void AddNamespaceTypes", typeIndexSource);
        Assert.Contains("private static void AddTypeAndNestedTypes", typeIndexSource);
        Assert.Contains("private static void AddLookupName", typeIndexSource);
    }

    [Fact]
    public void LC001_QueryableScopeDetection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC001_LocalMethod");
        var analyzerPath = Path.Combine(analyzerDir, "LocalMethodAnalyzer.cs");
        var queryableScopePath = Path.Combine(analyzerDir, "LocalMethodAnalyzerQueryableScope.cs");

        Assert.True(File.Exists(queryableScopePath), "LC001 queryable-scope detection should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool IsTranslationCriticalQueryableInvocation", analyzerSource);
        Assert.DoesNotContain("private static IArgumentOperation? GetInputSequenceArgument", analyzerSource);
        Assert.DoesNotContain("private static bool InvocationDependsOnLambdaParameter", analyzerSource);

        var queryableScopeSource = File.ReadAllText(queryableScopePath);
        Assert.Contains("private static bool IsTranslationCriticalQueryableInvocation", queryableScopeSource);
        Assert.Contains("private static IArgumentOperation? GetInputSequenceArgument", queryableScopeSource);
        Assert.Contains("private static bool InvocationDependsOnLambdaParameter", queryableScopeSource);
    }

    [Fact]
    public void LC029_IdentitySelectorClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC029_RedundantIdentitySelect");
        var analyzerPath = Path.Combine(analyzerDir, "RedundantIdentitySelectAnalyzer.cs");
        var identitySelectorPath = Path.Combine(analyzerDir, "RedundantIdentitySelectIdentitySelector.cs");

        Assert.True(File.Exists(identitySelectorPath), "LC029 identity-selector classification should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class RedundantIdentitySelectAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static IAnonymousFunctionOperation? TryGetLambda", analyzerSource);
        Assert.DoesNotContain("private static bool IsExactEnumerableInterface", analyzerSource);
        Assert.DoesNotContain("private static bool IsTypePreservingSelector", analyzerSource);
        Assert.DoesNotContain("private static bool IsIdentityLambda", analyzerSource);
        Assert.DoesNotContain("private static IOperation UnwrapIdentityValue", analyzerSource);

        var identitySelectorSource = File.ReadAllText(identitySelectorPath);
        Assert.Contains("private static IAnonymousFunctionOperation? TryGetLambda", identitySelectorSource);
        Assert.Contains("private static bool IsExactEnumerableInterface", identitySelectorSource);
        Assert.Contains("private static bool IsTypePreservingSelector", identitySelectorSource);
        Assert.Contains("private static bool IsIdentityLambda", identitySelectorSource);
        Assert.Contains("private static IOperation UnwrapIdentityValue", identitySelectorSource);
    }

    [Fact]
    public void LC012_FixerQuerySourceContextResolution_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC012_OptimizeRemoveRange");
        var rewriteSafetyPath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixerRewriteSafety.cs");
        var querySourcePath = Path.Combine(fixerDir, "OptimizeRemoveRangeFixerQuerySourceContext.cs");

        Assert.True(File.Exists(querySourcePath), "LC012 fixer query-source context resolution should live in a focused partial file.");

        var rewriteSafetySource = File.ReadAllText(rewriteSafetyPath);
        Assert.DoesNotContain("private static bool TryResolveQuerySourceFreshContextLocal", rewriteSafetySource);
        Assert.DoesNotContain("private static bool TryGetTransparentQueryInvocationSource", rewriteSafetySource);
        Assert.DoesNotContain("private static bool IsSingleSourceTransparentQueryMethod", rewriteSafetySource);

        var querySource = File.ReadAllText(querySourcePath);
        Assert.Contains("private static bool TryResolveQuerySourceFreshContextLocal", querySource);
        Assert.Contains("private static bool TryGetTransparentQueryInvocationSource", querySource);
        Assert.Contains("private static bool IsSingleSourceTransparentQueryMethod", querySource);
    }

    [Fact]
    public void LC036_LocalFunctionCallbackCapture_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC036_DbContextCapturedAcrossThreads");
        var captureAnalysisPath = Path.Combine(analyzerDir, "DbContextCapturedAcrossThreadsCaptureAnalysis.cs");
        var localFunctionPath = Path.Combine(analyzerDir, "DbContextCapturedAcrossThreadsLocalFunctionCapture.cs");

        Assert.True(File.Exists(localFunctionPath), "LC036 local-function callback capture should live in a focused partial file.");

        var captureAnalysisSource = File.ReadAllText(captureAnalysisPath);
        Assert.DoesNotContain("private static bool TryFindCapturedDbContextInLocalFunctionCallback", captureAnalysisSource);

        var localFunctionSource = File.ReadAllText(localFunctionPath);
        Assert.Contains("private static bool TryFindCapturedDbContextInLocalFunctionCallback", localFunctionSource);
        Assert.Contains("LocalFunctionStatementSyntax", localFunctionSource);
        Assert.Contains("MethodKind.LocalFunction", localFunctionSource);
    }

    [Fact]
    public void LC035_WhereChainTraversal_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC035_MissingWhereBeforeExecuteDeleteUpdate");
        var analyzerPath = Path.Combine(analyzerDir, "MissingWhereBeforeExecuteDeleteUpdateAnalyzer.cs");
        var whereChainPath = Path.Combine(analyzerDir, "MissingWhereBeforeExecuteDeleteUpdateWhereChain.cs");

        Assert.True(File.Exists(whereChainPath), "LC035 Where-chain traversal should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool HasWhereInChain", analyzerSource);
        Assert.DoesNotContain("private static bool IsKnownLinqWhere", analyzerSource);
        Assert.DoesNotContain("private static bool HasQuerySyntaxWhere", analyzerSource);

        var whereChainSource = File.ReadAllText(whereChainPath);
        Assert.Contains("private static bool HasWhereInChain", whereChainSource);
        Assert.Contains("private static bool IsKnownLinqWhere", whereChainSource);
        Assert.Contains("private static bool HasQuerySyntaxWhere", whereChainSource);
    }

    [Fact]
    public void LC002_FixerRedundantMaterializationRewrite_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC002_PrematureMaterialization");
        var fixerPath = Path.Combine(fixerDir, "PrematureMaterializationFixer.cs");
        var redundantRewritePath = Path.Combine(fixerDir, "PrematureMaterializationFixerRedundantMaterialization.cs");

        Assert.True(File.Exists(redundantRewritePath), "LC002 redundant-materialization fixer rewrite should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.DoesNotContain("private static async Task<Document> RemoveRedundantMaterializationAsync", fixerSource);

        var redundantRewriteSource = File.ReadAllText(redundantRewritePath);
        Assert.Contains("private static async Task<Document> RemoveRedundantMaterializationAsync", redundantRewriteSource);
        Assert.Contains("TryGetInlineMaterializerParts", redundantRewriteSource);
        Assert.Contains("Formatter.Annotation", redundantRewriteSource);
    }

    [Fact]
    public void LC037_JumpSkippingFlow_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var terminationFlowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionTerminationFlow.cs");
        var jumpFlowPath = Path.Combine(analyzerDir, "RawSqlStringConstructionJumpFlow.cs");

        Assert.True(File.Exists(jumpFlowPath), "LC037 jump/reference skipping flow should live in a focused partial file.");

        var terminationFlowSource = File.ReadAllText(terminationFlowPath);
        Assert.DoesNotContain("private static bool HasPotentiallySkippingAncestor", terminationFlowSource);
        Assert.DoesNotContain("private static bool ContainsPosition", terminationFlowSource);
        Assert.DoesNotContain("private static bool JumpSkipsReference", terminationFlowSource);

        var jumpFlowSource = File.ReadAllText(jumpFlowPath);
        Assert.Contains("private static bool HasPotentiallySkippingAncestor", jumpFlowSource);
        Assert.Contains("private static bool ContainsPosition", jumpFlowSource);
        Assert.Contains("private static bool JumpSkipsReference", jumpFlowSource);
    }

    [Fact]
    public void LocalAssignmentCache_RootScan_LivesInDedicatedPartial()
    {
        var extensionsDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Extensions");
        var cachePath = Path.Combine(extensionsDir, "LocalAssignmentCache.cs");
        var rootScanPath = Path.Combine(extensionsDir, "LocalAssignmentCacheRootScan.cs");

        Assert.True(File.Exists(rootScanPath), "LocalAssignmentCache root scanning should live in a focused partial file.");

        var cacheSource = File.ReadAllText(cachePath);
        Assert.Contains("internal static partial class LocalAssignmentCache", cacheSource);
        Assert.DoesNotContain("private sealed class RootScan", cacheSource);
        Assert.DoesNotContain("public static RootScan Build", cacheSource);

        var rootScanSource = File.ReadAllText(rootScanPath);
        Assert.Contains("private sealed class RootScan", rootScanSource);
        Assert.Contains("public static RootScan Build", rootScanSource);
        Assert.Contains("private static void Add", rootScanSource);
    }

    [Fact]
    public void LC002_ProviderSafeStringMethods_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC002_PrematureMaterialization");
        var providerSafePath = Path.Combine(analyzerDir, "PrematureMaterializationProviderSafeExpressions.cs");
        var stringMethodsPath = Path.Combine(analyzerDir, "PrematureMaterializationProviderSafeStringMethods.cs");

        Assert.True(File.Exists(stringMethodsPath), "LC002 provider-safe string method rules should live in a focused partial file.");

        var providerSafeSource = File.ReadAllText(providerSafePath);
        Assert.DoesNotContain("private static bool IsAllowedProviderSafeStringMethod", providerSafeSource);
        Assert.DoesNotContain("private static bool HasStringComparisonParameter", providerSafeSource);

        var stringMethodsSource = File.ReadAllText(stringMethodsPath);
        Assert.Contains("private static bool IsAllowedProviderSafeStringMethod", stringMethodsSource);
        Assert.Contains("private static bool HasStringComparisonParameter", stringMethodsSource);
        Assert.Contains("StringComparison", stringMethodsSource);
    }

    [Fact]
    public void LC016_FixerStaticLambdaSafety_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC016_AvoidDateTimeNow");
        var fixerPath = Path.Combine(fixerDir, "AvoidDateTimeNowFixer.cs");
        var staticLambdaPath = Path.Combine(fixerDir, "AvoidDateTimeNowFixerStaticLambda.cs");

        Assert.True(File.Exists(staticLambdaPath), "LC016 static-lambda fixer safety should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.DoesNotContain("private static bool IsInsideStaticLambda", fixerSource);
        Assert.DoesNotContain("private static bool HasStaticModifier", fixerSource);

        var staticLambdaSource = File.ReadAllText(staticLambdaPath);
        Assert.Contains("private static bool IsInsideStaticLambda", staticLambdaSource);
        Assert.Contains("private static bool HasStaticModifier", staticLambdaSource);
        Assert.Contains("SyntaxKind.StaticKeyword", staticLambdaSource);
    }

    [Fact]
    public void LC044_ReachabilityBlockNavigation_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var reachabilityPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyReachability.cs");
        var blockNavigationPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyReachabilityBlocks.cs");

        Assert.True(File.Exists(blockNavigationPath), "LC044 reachability block navigation should live in a focused partial file.");

        var reachabilitySource = File.ReadAllText(reachabilityPath);
        Assert.DoesNotContain("private static bool IsBlockAncestor", reachabilitySource);
        Assert.DoesNotContain("private static IOperation? FindDirectChildOperationContainingSpan", reachabilitySource);
        Assert.DoesNotContain("private static IBlockOperation? FindEnclosingBlock", reachabilitySource);

        var blockNavigationSource = File.ReadAllText(blockNavigationPath);
        Assert.Contains("private static bool IsBlockAncestor", blockNavigationSource);
        Assert.Contains("private static IOperation? FindDirectChildOperationContainingSpan", blockNavigationSource);
        Assert.Contains("private static IBlockOperation? FindEnclosingBlock", blockNavigationSource);
    }

    [Fact]
    public void LC030_LongLivedTypeRecognition_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC030_DbContextInSingleton");
        var evidencePath = Path.Combine(analyzerDir, "DbContextInSingletonLongLivedEvidence.cs");
        var typeRecognitionPath = Path.Combine(analyzerDir, "DbContextInSingletonLongLivedTypeRecognition.cs");

        Assert.True(File.Exists(typeRecognitionPath), "LC030 long-lived/scoped type recognition helpers should live in a focused partial file.");

        var evidenceSource = File.ReadAllText(evidencePath);
        Assert.DoesNotContain("private static bool IsKnownScopedType", evidenceSource);
        Assert.DoesNotContain("private static bool HasConventionalMiddlewareSignature", evidenceSource);
        Assert.DoesNotContain("private static bool ImplementsInterface", evidenceSource);
        Assert.DoesNotContain("private static bool InheritsFrom", evidenceSource);

        var typeRecognitionSource = File.ReadAllText(typeRecognitionPath);
        Assert.Contains("private static bool IsKnownScopedType", typeRecognitionSource);
        Assert.Contains("private static bool HasConventionalMiddlewareSignature", typeRecognitionSource);
        Assert.Contains("private static bool ImplementsInterface", typeRecognitionSource);
        Assert.Contains("private static bool InheritsFrom", typeRecognitionSource);
    }

    [Fact]
    public void LC012_FixerSaveChangesBranchExclusion_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC012_OptimizeRemoveRange");
        var saveChangesSafetyPath = Path.Combine(analyzerDir, "OptimizeRemoveRangeFixerSaveChangesSafety.cs");
        var branchExclusionPath = Path.Combine(analyzerDir, "OptimizeRemoveRangeFixerSaveChangesBranchExclusion.cs");

        Assert.True(File.Exists(branchExclusionPath), "LC012 fixer save-changes branch exclusion should live in a focused partial file.");

        var saveChangesSafetySource = File.ReadAllText(saveChangesSafetyPath);
        Assert.DoesNotContain("private static bool AreMutuallyExclusiveBranches", saveChangesSafetySource);
        Assert.DoesNotContain("private static SyntaxNode? GetContainingIfBranch", saveChangesSafetySource);
        Assert.DoesNotContain("private static SwitchSectionSyntax? GetContainingSwitchSection", saveChangesSafetySource);

        var branchExclusionSource = File.ReadAllText(branchExclusionPath);
        Assert.Contains("private static bool AreMutuallyExclusiveBranches", branchExclusionSource);
        Assert.Contains("private static SyntaxNode? GetContainingIfBranch", branchExclusionSource);
        Assert.Contains("private static SwitchSectionSyntax? GetContainingSwitchSection", branchExclusionSource);
    }

    [Fact]
    public void LC037_StringBuilderIdentityReferenceTraversal_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC037_RawSqlStringConstruction");
        var identityPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderIdentity.cs");
        var referenceTraversalPath = Path.Combine(analyzerDir, "RawSqlStringConstructionStringBuilderIdentityReferences.cs");

        Assert.True(File.Exists(referenceTraversalPath), "LC037 StringBuilder identity reference traversal should live in a focused partial file.");

        var identitySource = File.ReadAllText(identityPath);
        Assert.DoesNotContain("private static bool MayReferenceIdentity", identitySource);

        var referenceTraversalSource = File.ReadAllText(referenceTraversalPath);
        Assert.Contains("private static bool MayReferenceIdentity", referenceTraversalSource);
        Assert.Contains("IInterpolatedStringOperation", referenceTraversalSource);
        Assert.Contains("IObjectCreationOperation", referenceTraversalSource);
        Assert.Contains("IInvocationOperation", referenceTraversalSource);
    }

    [Fact]
    public void LC013_AssignedValueCollection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC013_DisposedContextQuery");
        var originResolutionPath = Path.Combine(analyzerDir, "DisposedContextQueryAssignedOriginResolution.cs");
        var assignedValuesPath = Path.Combine(analyzerDir, "DisposedContextQueryAssignedValues.cs");

        Assert.True(File.Exists(assignedValuesPath), "LC013 assigned-value collection should live in a focused partial file.");

        var originResolutionSource = File.ReadAllText(originResolutionPath);
        Assert.DoesNotContain("private static bool TryGetSingleAssignedValue", originResolutionSource);
        Assert.DoesNotContain("private static List<IOperation> GetAssignedValues", originResolutionSource);
        Assert.DoesNotContain("private static bool IsLocalTarget", originResolutionSource);
        Assert.DoesNotContain("private static IEnumerable<IOperation> EnumerateOperations", originResolutionSource);

        var assignedValuesSource = File.ReadAllText(assignedValuesPath);
        Assert.Contains("private static bool TryGetSingleAssignedValue", assignedValuesSource);
        Assert.Contains("private static List<IOperation> GetAssignedValues", assignedValuesSource);
        Assert.Contains("private static bool IsLocalTarget", assignedValuesSource);
        Assert.Contains("private static IEnumerable<IOperation> EnumerateOperations", assignedValuesSource);
    }

    [Fact]
    public void LC041_FixerInvocationRewrite_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC041_SingleEntityScalarProjection");
        var fixerPath = Path.Combine(fixerDir, "SingleEntityScalarProjectionFixer.cs");
        var rewritePath = Path.Combine(fixerDir, "SingleEntityScalarProjectionFixerRewrite.cs");

        Assert.True(File.Exists(rewritePath), "LC041 fixer invocation rewrite should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.DoesNotContain("private static ExpressionSyntax RewriteInvocation", fixerSource);
        Assert.DoesNotContain("private static int FindPredicateIndex", fixerSource);

        var rewriteSource = File.ReadAllText(rewritePath);
        Assert.Contains("private static ExpressionSyntax RewriteInvocation", rewriteSource);
        Assert.Contains("private static int FindPredicateIndex", rewriteSource);
        Assert.Contains(".Select(x => x.", rewriteSource);
    }

    [Fact]
    public void LC002_MaterializerClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "MaterializationAndProjection",
            "LC002_PrematureMaterialization");
        var methodRulesPath = Path.Combine(analyzerDir, "PrematureMaterializationMethodRules.cs");
        var materializersPath = Path.Combine(analyzerDir, "PrematureMaterializationMaterializerRules.cs");

        Assert.True(File.Exists(materializersPath), "LC002 materializer classification rules should live in a focused partial file.");

        var methodRulesSource = File.ReadAllText(methodRulesPath);
        Assert.DoesNotContain("private static bool IsMaterializingMethod", methodRulesSource);
        Assert.DoesNotContain("private static bool IsDirectCollectionMaterializer", methodRulesSource);
        Assert.DoesNotContain("private static bool IsMaterializingConstructor", methodRulesSource);

        var materializersSource = File.ReadAllText(materializersPath);
        Assert.Contains("private static bool IsMaterializingMethod", materializersSource);
        Assert.Contains("private static bool IsDirectCollectionMaterializer", materializersSource);
        Assert.Contains("private static bool IsDeduplicatingSetMaterializer", materializersSource);
        Assert.Contains("private static bool IsKeyedOrGroupedMaterializer", materializersSource);
        Assert.Contains("private static bool IsMaterializingConstructor", materializersSource);
    }

    [Fact]
    public void LC044_TrackingStateDetachInvalidation_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC044_AsNoTrackingThenModify");
        var trackingStatePath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyTrackingState.cs");
        var detachInvalidationPath = Path.Combine(analyzerDir, "AsNoTrackingThenModifyTrackingDetachInvalidation.cs");

        Assert.True(File.Exists(detachInvalidationPath), "LC044 detach/tracker-clear invalidation should live in a focused partial file.");

        var trackingStateSource = File.ReadAllText(trackingStatePath);
        Assert.DoesNotContain("private static bool HasInterveningDetach", trackingStateSource);

        var detachInvalidationSource = File.ReadAllText(detachInvalidationPath);
        Assert.Contains("private static bool HasInterveningDetach", detachInvalidationSource);
        Assert.Contains("scan.DetachesByLocal", detachInvalidationSource);
        Assert.Contains("scan.TrackerClears", detachInvalidationSource);
    }

    [Fact]
    public void LC025_SourceResolutionConditionalOrigins_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC025_AsNoTrackingWithUpdate");
        var sourceResolutionPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateSourceResolution.cs");
        var conditionalOriginsPath = Path.Combine(analyzerDir, "AsNoTrackingWithUpdateSourceConditionalOrigins.cs");

        Assert.True(File.Exists(conditionalOriginsPath), "LC025 conditional origin state should live in a focused partial file.");

        var sourceResolutionSource = File.ReadAllText(sourceResolutionPath);
        Assert.DoesNotContain("private readonly struct LocalOrigin", sourceResolutionSource);
        Assert.DoesNotContain("private static bool IsConditionalRelativeTo", sourceResolutionSource);

        var conditionalOriginsSource = File.ReadAllText(conditionalOriginsPath);
        Assert.Contains("private readonly struct LocalOrigin", conditionalOriginsSource);
        Assert.Contains("private static bool IsConditionalRelativeTo", conditionalOriginsSource);
        Assert.Contains("SwitchExpressionSyntax", conditionalOriginsSource);
        Assert.Contains("CommonForEachStatementSyntax", conditionalOriginsSource);
    }

    [Fact]
    public void LC034_SqlSafetyDetection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC034_AvoidExecuteSqlRawWithInterpolation");
        var analyzerPath = Path.Combine(analyzerDir, "AvoidExecuteSqlRawWithInterpolationAnalyzer.cs");
        var sqlSafetyPath = Path.Combine(analyzerDir, "AvoidExecuteSqlRawWithInterpolationSqlSafety.cs");

        Assert.True(File.Exists(sqlSafetyPath), "LC034 SQL safety classification should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AvoidExecuteSqlRawWithInterpolationAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsPotentiallyUnsafeSql", analyzerSource);
        Assert.DoesNotContain("private static bool IsUnsafeConcatenation", analyzerSource);
        Assert.DoesNotContain("private static bool HasNonConstantInterpolation", analyzerSource);

        var sqlSafetySource = File.ReadAllText(sqlSafetyPath);
        Assert.Contains("private static bool IsPotentiallyUnsafeSql", sqlSafetySource);
        Assert.Contains("private static bool IsUnsafeConcatenation", sqlSafetySource);
        Assert.Contains("private static bool HasNonConstantInterpolation", sqlSafetySource);
        Assert.Contains("IInterpolatedStringOperation", sqlSafetySource);
    }

    [Fact]
    public void LC005_FixerRewriteValidation_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "QueryShapeAndTranslation",
            "LC005_MultipleOrderBy");
        var fixerPath = Path.Combine(fixerDir, "MultipleOrderByFixer.cs");
        var validationPath = Path.Combine(fixerDir, "MultipleOrderByFixerRewriteValidation.cs");

        Assert.True(File.Exists(validationPath), "LC005 fixer rewrite validation should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class MultipleOrderByFixer", fixerSource);
        Assert.DoesNotContain("private static bool CanRewriteToThenBy", fixerSource);
        Assert.DoesNotContain("private static ExpressionSyntax? GetLogicalReceiverExpression", fixerSource);
        Assert.DoesNotContain("private static bool IsOrderedSequence", fixerSource);

        var validationSource = File.ReadAllText(validationPath);
        Assert.Contains("private static bool CanRewriteToThenBy", validationSource);
        Assert.Contains("private static ExpressionSyntax? GetLogicalReceiverExpression", validationSource);
        Assert.Contains("private static bool IsOrderedSequence", validationSource);
        Assert.Contains("IOrderedQueryable", validationSource);
    }

    [Fact]
    public void LC008_FixerAwaitContextValidation_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC008_SyncBlocker");
        var fixerPath = Path.Combine(fixerDir, "SyncBlockerFixer.cs");
        var awaitContextPath = Path.Combine(fixerDir, "SyncBlockerFixerAwaitContext.cs");

        Assert.True(File.Exists(awaitContextPath), "LC008 fixer await-context validation should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class SyncBlockerFixer", fixerSource);
        Assert.DoesNotContain("private static bool IsInvalidAwaitContext", fixerSource);
        Assert.DoesNotContain("FromClauseSyntax", fixerSource);
        Assert.DoesNotContain("AnonymousFunctionExpressionSyntax", fixerSource);

        var awaitContextSource = File.ReadAllText(awaitContextPath);
        Assert.Contains("private static bool IsInvalidAwaitContext", awaitContextSource);
        Assert.Contains("FromClauseSyntax", awaitContextSource);
        Assert.Contains("JoinClauseSyntax", awaitContextSource);
        Assert.Contains("AnonymousFunctionExpressionSyntax", awaitContextSource);
        Assert.Contains("LocalFunctionStatementSyntax", awaitContextSource);
    }

    [Fact]
    public void LC018_AnalyzerSqlSafetyDetection_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC018_AvoidFromSqlRawWithInterpolation");
        var analyzerPath = Path.Combine(analyzerDir, "AvoidFromSqlRawWithInterpolationAnalyzer.cs");
        var sqlSafetyPath = Path.Combine(analyzerDir, "AvoidFromSqlRawWithInterpolationAnalyzerSqlSafety.cs");

        Assert.True(File.Exists(sqlSafetyPath), "LC018 analyzer SQL safety classification should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class AvoidFromSqlRawWithInterpolationAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool IsPotentiallyUnsafe", analyzerSource);
        Assert.DoesNotContain("private static bool IsUnsafeConcatenation", analyzerSource);
        Assert.DoesNotContain("private static bool HasNonConstantInterpolation", analyzerSource);

        var sqlSafetySource = File.ReadAllText(sqlSafetyPath);
        Assert.Contains("private static bool IsPotentiallyUnsafe", sqlSafetySource);
        Assert.Contains("private static bool IsUnsafeConcatenation", sqlSafetySource);
        Assert.Contains("private static bool HasNonConstantInterpolation", sqlSafetySource);
        Assert.Contains("IInterpolatedStringOperation", sqlSafetySource);
    }

    [Fact]
    public void LC042_QueryMethodLookupTables_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC042_MissingQueryTags");
        var analyzerPath = Path.Combine(analyzerDir, "MissingQueryTagsAnalyzer.cs");
        var methodSetsPath = Path.Combine(analyzerDir, "MissingQueryTagsMethodSets.cs");

        Assert.True(File.Exists(methodSetsPath), "LC042 query method lookup tables should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static readonly ImmutableHashSet<string> TargetMethods", analyzerSource);
        Assert.DoesNotContain("private static readonly ImmutableHashSet<string> QuerySteps", analyzerSource);

        var methodSetsSource = File.ReadAllText(methodSetsPath);
        Assert.Contains("private static readonly ImmutableHashSet<string> TargetMethods", methodSetsSource);
        Assert.Contains("private static readonly ImmutableHashSet<string> QuerySteps", methodSetsSource);
        Assert.Contains("\"TagWithCallSite\"", methodSetsSource);
        Assert.Contains("\"ToHashSetAsync\"", methodSetsSource);
    }

    [Fact]
    public void LC028_ThenIncludeChainAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC028_DeepThenInclude");
        var analyzerPath = Path.Combine(analyzerDir, "DeepThenIncludeAnalyzer.cs");
        var chainAnalysisPath = Path.Combine(analyzerDir, "DeepThenIncludeChainAnalysis.cs");

        Assert.True(File.Exists(chainAnalysisPath), "LC028 ThenInclude chain analysis should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class DeepThenIncludeAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static int CountThenIncludeDepth", analyzerSource);
        Assert.DoesNotContain("private static Location GetDiagnosticLocation", analyzerSource);
        Assert.DoesNotContain("TextSpan.FromBounds", analyzerSource);

        var chainAnalysisSource = File.ReadAllText(chainAnalysisPath);
        Assert.Contains("private static int CountThenIncludeDepth", chainAnalysisSource);
        Assert.Contains("private static Location GetDiagnosticLocation", chainAnalysisSource);
        Assert.Contains("TextSpan.FromBounds", chainAnalysisSource);
        Assert.Contains("MemberAccessExpressionSyntax", chainAnalysisSource);
    }

    [Fact]
    public void SymbolAnalysis_PrimaryKeyDiscovery_LivesInDedicatedPartial()
    {
        var extensionsDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Extensions");
        var symbolExtensionsPath = Path.Combine(extensionsDir, "SymbolAnalysisExtensions.cs");
        var primaryKeyPath = Path.Combine(extensionsDir, "SymbolAnalysisPrimaryKeyExtensions.cs");

        Assert.True(File.Exists(primaryKeyPath), "Primary-key symbol discovery should live in a focused extension partial file.");

        var symbolExtensionsSource = File.ReadAllText(symbolExtensionsPath);
        Assert.DoesNotContain("public static string? TryFindPrimaryKey", symbolExtensionsSource);
        Assert.DoesNotContain("private static bool IsDataAnnotationsKeyAttribute", symbolExtensionsSource);

        var primaryKeySource = File.ReadAllText(primaryKeyPath);
        Assert.Contains("public static string? TryFindPrimaryKey", primaryKeySource);
        Assert.Contains("private static bool IsDataAnnotationsKeyAttribute", primaryKeySource);
        Assert.Contains("System.ComponentModel.DataAnnotations", primaryKeySource);
    }

    [Fact]
    public void LC019_ConditionalIncludePathAnalysis_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC019_ConditionalInclude");
        var analyzerPath = Path.Combine(analyzerDir, "ConditionalIncludeAnalyzer.cs");
        var pathAnalysisPath = Path.Combine(analyzerDir, "ConditionalIncludePathAnalysis.cs");

        Assert.True(File.Exists(pathAnalysisPath), "LC019 conditional include path traversal should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.Contains("public sealed partial class ConditionalIncludeAnalyzer", analyzerSource);
        Assert.DoesNotContain("private static bool HasConditionalIncludePath", analyzerSource);
        Assert.DoesNotContain("private static bool HasConditionalIncludeInvocationSource", analyzerSource);

        var pathAnalysisSource = File.ReadAllText(pathAnalysisPath);
        Assert.Contains("private static bool HasConditionalIncludePath", pathAnalysisSource);
        Assert.Contains("private static bool HasConditionalIncludeInvocationSource", pathAnalysisSource);
        Assert.Contains("IConditionalOperation or ICoalesceOperation", pathAnalysisSource);
    }

    [Fact]
    public void LC026_FixerExplicitTokenArgument_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC026_MissingCancellationToken");
        var fixerPath = Path.Combine(fixerDir, "MissingCancellationTokenFixer.cs");
        var explicitArgumentPath = Path.Combine(fixerDir, "MissingCancellationTokenFixerExplicitArgument.cs");

        Assert.True(File.Exists(explicitArgumentPath), "LC026 fixer explicit-token argument lookup should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class MissingCancellationTokenFixer", fixerSource);
        Assert.DoesNotContain("private static ArgumentSyntax? FindExplicitCancellationTokenArgument", fixerSource);
        Assert.DoesNotContain("private static bool IsCancellationTokenParameter", fixerSource);

        var explicitArgumentSource = File.ReadAllText(explicitArgumentPath);
        Assert.Contains("private static ArgumentSyntax? FindExplicitCancellationTokenArgument", explicitArgumentSource);
        Assert.Contains("private static bool IsCancellationTokenParameter", explicitArgumentSource);
        Assert.Contains("IInvocationOperation", explicitArgumentSource);
    }

    [Fact]
    public void LC032_ScalarTypeClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var assignmentAnalysisPath = Path.Combine(analyzerDir, "ExecuteUpdateForBulkUpdatesAssignmentAnalysis.cs");
        var scalarTypesPath = Path.Combine(analyzerDir, "ExecuteUpdateForBulkUpdatesScalarTypes.cs");

        Assert.True(File.Exists(scalarTypesPath), "LC032 scalar type classification should live in a focused partial file.");

        var assignmentAnalysisSource = File.ReadAllText(assignmentAnalysisPath);
        Assert.DoesNotContain("private static bool IsScalarLikeType", assignmentAnalysisSource);
        Assert.DoesNotContain("SymbolDisplayFormat.FullyQualifiedFormat", assignmentAnalysisSource);

        var scalarTypesSource = File.ReadAllText(scalarTypesPath);
        Assert.Contains("private static bool IsScalarLikeType", scalarTypesSource);
        Assert.Contains("SymbolDisplayFormat.FullyQualifiedFormat", scalarTypesSource);
        Assert.Contains("global::System.DateTimeOffset", scalarTypesSource);
    }

    [Fact]
    public void LC032_QueryStepLookupTables_LiveInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "BulkOperationsAndSetBasedWrites",
            "LC032_ExecuteUpdateForBulkUpdates");
        var queryAnalysisPath = Path.Combine(analyzerDir, "ExecuteUpdateForBulkUpdatesQueryAnalysis.cs");
        var queryStepsPath = Path.Combine(analyzerDir, "ExecuteUpdateForBulkUpdatesQuerySteps.cs");

        Assert.True(File.Exists(queryStepsPath), "LC032 allowed query/materializer step lookup tables should live in a focused partial file.");

        var queryAnalysisSource = File.ReadAllText(queryAnalysisPath);
        Assert.DoesNotContain("private static readonly ImmutableHashSet<string> AllowedQuerySteps", queryAnalysisSource);
        Assert.DoesNotContain("private static readonly ImmutableHashSet<string> MaterializerSteps", queryAnalysisSource);

        var queryStepsSource = File.ReadAllText(queryStepsPath);
        Assert.Contains("private static readonly ImmutableHashSet<string> AllowedQuerySteps", queryStepsSource);
        Assert.Contains("private static readonly ImmutableHashSet<string> MaterializerSteps", queryStepsSource);
        Assert.Contains("\"TagWithCallSite\"", queryStepsSource);
        Assert.Contains("\"ToArrayAsync\"", queryStepsSource);
    }

    [Fact]
    public void LC045_FixerQuerySourceResolution_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC045_MissingInclude");
        var fixerPath = Path.Combine(fixerDir, "MissingIncludeFixer.cs");
        var querySourcePath = Path.Combine(fixerDir, "MissingIncludeFixerQuerySource.cs");

        Assert.True(File.Exists(querySourcePath), "LC045 fixer query-source resolution should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class MissingIncludeFixer", fixerSource);
        Assert.DoesNotContain("private static async Task<ExpressionSyntax?> GetQueryableSourceAsync", fixerSource);
        Assert.DoesNotContain("semanticModel?.GetTypeInfo(querySource, cancellationToken)", fixerSource);

        var querySource = File.ReadAllText(querySourcePath);
        Assert.Contains("private static async Task<ExpressionSyntax?> GetQueryableSourceAsync", querySource);
        Assert.Contains("semanticModel?.GetTypeInfo(querySource, cancellationToken)", querySource);
        Assert.Contains("query source", querySource);
    }

    [Fact]
    public void LC006_ReceiverChainTraversal_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC006_CartesianExplosion");
        var chainAnalysisPath = Path.Combine(analyzerDir, "CartesianExplosionChainAnalysis.cs");
        var receiverChainPath = Path.Combine(analyzerDir, "CartesianExplosionReceiverChain.cs");

        Assert.True(File.Exists(receiverChainPath), "LC006 receiver-chain traversal should live in a focused partial file.");

        var chainAnalysisSource = File.ReadAllText(chainAnalysisPath);
        Assert.DoesNotContain("private static ImmutableArray<IInvocationOperation> CollectReceiverChainInvocations", chainAnalysisSource);
        Assert.DoesNotContain("private static bool HasRelevantQueryOperatorAncestor", chainAnalysisSource);
        Assert.DoesNotContain("private static bool InvocationUsesReceiverChain", chainAnalysisSource);

        var receiverChainSource = File.ReadAllText(receiverChainPath);
        Assert.Contains("private static ImmutableArray<IInvocationOperation> CollectReceiverChainInvocations", receiverChainSource);
        Assert.Contains("private static bool HasRelevantQueryOperatorAncestor", receiverChainSource);
        Assert.Contains("private static bool InvocationUsesReceiverChain", receiverChainSource);
        Assert.Contains("LocalAssignmentCache.TryGetSingleAssignedValueBefore", receiverChainSource);
    }

    [Fact]
    public void LC011_DbSetMemberExtraction_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var analyzerPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyAnalyzer.cs");
        var dbSetMembersPath = Path.Combine(analyzerDir, "EntityMissingPrimaryKeyDbSetMembers.cs");

        Assert.True(File.Exists(dbSetMembersPath), "LC011 DbSet member extraction should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool TryGetDbSetMember", analyzerSource);
        Assert.DoesNotContain("field.Name.StartsWith(\"<\", StringComparison.Ordinal)", analyzerSource);

        var dbSetMembersSource = File.ReadAllText(dbSetMembersPath);
        Assert.Contains("private static bool TryGetDbSetMember", dbSetMembersSource);
        Assert.Contains("IPropertySymbol property", dbSetMembersSource);
        Assert.Contains("IFieldSymbol field", dbSetMembersSource);
        Assert.Contains("field.Name.StartsWith(\"<\", StringComparison.Ordinal)", dbSetMembersSource);
    }

    [Fact]
    public void LC011_FixerEntityTypeResolution_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC011_EntityMissingPrimaryKey");
        var fixerPath = Path.Combine(fixerDir, "EntityMissingPrimaryKeyFixer.cs");
        var entityTypePath = Path.Combine(fixerDir, "EntityMissingPrimaryKeyFixerEntityType.cs");

        Assert.True(File.Exists(entityTypePath), "LC011 fixer entity-type and Id-member checks should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class EntityMissingPrimaryKeyFixer", fixerSource);
        Assert.DoesNotContain("private static bool TryGetEntityType", fixerSource);
        Assert.DoesNotContain("private static bool HasIdMember", fixerSource);

        var entityTypeSource = File.ReadAllText(entityTypePath);
        Assert.Contains("private static bool TryGetEntityType", entityTypeSource);
        Assert.Contains("private static bool HasIdMember", entityTypeSource);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", entityTypeSource);
    }

    [Fact]
    public void LC034_FixerSqlArgumentSafety_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "RawSqlAndSecurity",
            "LC034_AvoidExecuteSqlRawWithInterpolation");
        var fixerPath = Path.Combine(fixerDir, "AvoidExecuteSqlRawWithInterpolationFixer.cs");
        var sqlArgumentPath = Path.Combine(fixerDir, "AvoidExecuteSqlRawWithInterpolationFixerSqlArgument.cs");

        Assert.True(File.Exists(sqlArgumentPath), "LC034 fixer SQL argument and literal-safety helpers should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class AvoidExecuteSqlRawWithInterpolationFixer", fixerSource);
        Assert.DoesNotContain("private static ArgumentSyntax? GetSqlArgument", fixerSource);
        Assert.DoesNotContain("private static bool HasInterpolationInsideSqlStringLiteral", fixerSource);
        Assert.DoesNotContain("private static void UpdateSqlDelimiterState", fixerSource);

        var sqlArgumentSource = File.ReadAllText(sqlArgumentPath);
        Assert.Contains("private static ArgumentSyntax? GetSqlArgument", sqlArgumentSource);
        Assert.Contains("private static bool HasInterpolationInsideSqlStringLiteral", sqlArgumentSource);
        Assert.Contains("private static void UpdateSqlDelimiterState", sqlArgumentSource);
        Assert.Contains("InterpolationSyntax when insideSqlStringLiteral", sqlArgumentSource);
    }

    [Fact]
    public void LC006_FixerSyntaxChainSearch_LivesInDedicatedPartial()
    {
        var fixerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "LoadingAndIncludes",
            "LC006_CartesianExplosion");
        var fixerPath = Path.Combine(fixerDir, "CartesianExplosionFixer.cs");
        var syntaxChainPath = Path.Combine(fixerDir, "CartesianExplosionFixerSyntaxChain.cs");

        Assert.True(File.Exists(syntaxChainPath), "LC006 fixer syntax-chain search helpers should live in a focused partial file.");

        var fixerSource = File.ReadAllText(fixerPath);
        Assert.Contains("public sealed partial class CartesianExplosionFixer", fixerSource);
        Assert.DoesNotContain("private static InvocationExpressionSyntax? FindEffectiveAsSingleQueryInvocation", fixerSource);
        Assert.DoesNotContain("private static InvocationExpressionSyntax? FindFirstIncludeInvocation", fixerSource);
        Assert.DoesNotContain("private static bool IsInvocationOf", fixerSource);

        var syntaxChainSource = File.ReadAllText(syntaxChainPath);
        Assert.Contains("private static InvocationExpressionSyntax? FindEffectiveAsSingleQueryInvocation", syntaxChainSource);
        Assert.Contains("private static InvocationExpressionSyntax? FindFirstIncludeInvocation", syntaxChainSource);
        Assert.Contains("private static bool IsInvocationOf", syntaxChainSource);
        Assert.Contains("\"AsSingleQuery\"", syntaxChainSource);
    }

    [Fact]
    public void LC027_CollectionTypeClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "SchemaAndModeling",
            "LC027_MissingExplicitForeignKey");
        var entityAnalysisPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyEntityAnalysis.cs");
        var collectionTypesPath = Path.Combine(analyzerDir, "MissingExplicitForeignKeyCollectionTypes.cs");

        Assert.True(File.Exists(collectionTypesPath), "LC027 collection-type classification should live in a focused partial file.");

        var entityAnalysisSource = File.ReadAllText(entityAnalysisPath);
        Assert.DoesNotContain("private static bool IsCollectionType", entityAnalysisSource);
        Assert.DoesNotContain("IReadOnlyCollection", entityAnalysisSource);
        Assert.DoesNotContain("iface.Name == \"IEnumerable\"", entityAnalysisSource);

        var collectionTypesSource = File.ReadAllText(collectionTypesPath);
        Assert.Contains("private static bool IsCollectionType", collectionTypesSource);
        Assert.Contains("System.Collections.Generic", collectionTypesSource);
        Assert.Contains("IReadOnlyCollection", collectionTypesSource);
    }

    [Fact]
    public void LC009_MaterializerMethodClassification_LivesInDedicatedPartial()
    {
        var analyzerDir = Path.Combine(
            _repoRoot,
            "src",
            "LinqContraband",
            "Analyzers",
            "ChangeTrackingAndContextLifetime",
            "LC009_MissingAsNoTracking");
        var analyzerPath = Path.Combine(analyzerDir, "MissingAsNoTrackingAnalyzer.cs");
        var materializersPath = Path.Combine(analyzerDir, "MissingAsNoTrackingMaterializers.cs");

        Assert.True(File.Exists(materializersPath), "LC009 materializer method classification should live in a focused partial file.");

        var analyzerSource = File.ReadAllText(analyzerPath);
        Assert.DoesNotContain("private static bool IsEntityMaterializer", analyzerSource);
        Assert.DoesNotContain("\"ToDictionaryAsync\"", analyzerSource);

        var materializersSource = File.ReadAllText(materializersPath);
        Assert.Contains("private static bool IsEntityMaterializer", materializersSource);
        Assert.Contains("\"ToDictionaryAsync\"", materializersSource);
        Assert.Contains("\"SingleOrDefaultAsync\"", materializersSource);
    }

    [Fact]
    public void SampleDiagnosticsVerifier_SarifParsing_LivesInDedicatedPartial()
    {
        var verifierDir = Path.Combine(
            _repoRoot,
            "tools",
            "SampleDiagnosticsVerifier");
        var programPath = Path.Combine(verifierDir, "Program.cs");
        var sarifPath = Path.Combine(verifierDir, "Program.SarifDiagnostics.cs");

        Assert.True(File.Exists(sarifPath), "Sample diagnostics SARIF parsing should live in a focused Program partial file.");

        var programSource = File.ReadAllText(programPath);
        Assert.DoesNotContain("static IReadOnlyList<SampleDiagnostic> ParseDiagnostics", programSource);
        Assert.DoesNotContain("static bool TryGetDiagnosticPath", programSource);
        Assert.DoesNotContain("static string NormalizeRelativePath", programSource);

        var sarifSource = File.ReadAllText(sarifPath);
        Assert.Contains("private static IReadOnlyList<SampleDiagnostic> ParseDiagnostics", sarifSource);
        Assert.Contains("private static bool TryGetDiagnosticPath", sarifSource);
        Assert.Contains("private static string NormalizeRelativePath", sarifSource);
        Assert.Contains("internal sealed record SampleDiagnostic", sarifSource);
    }

    [Fact]
    public void SampleDiagnosticsVerifier_ManifestParsing_LivesInDedicatedPartial()
    {
        var verifierDir = Path.Combine(
            _repoRoot,
            "tools",
            "SampleDiagnosticsVerifier");
        var programPath = Path.Combine(verifierDir, "Program.cs");
        var manifestPath = Path.Combine(verifierDir, "Program.Manifest.cs");

        Assert.True(File.Exists(manifestPath), "Sample diagnostics manifest parsing should live in a focused Program partial file.");

        var programSource = File.ReadAllText(programPath);
        Assert.DoesNotContain("static SampleExpectationGroup[] LoadExpectationGroups", programSource);
        Assert.DoesNotContain("static string[] LoadSafeSamplePaths", programSource);
        Assert.DoesNotContain("internal sealed record SampleExpectation", programSource);

        var manifestSource = File.ReadAllText(manifestPath);
        Assert.Contains("private static SampleExpectationGroup[] LoadExpectationGroups", manifestSource);
        Assert.Contains("private static string[] LoadSafeSamplePaths", manifestSource);
        Assert.Contains("internal sealed record SampleExpectation", manifestSource);
        Assert.Contains("internal sealed record SampleExpectationGroup", manifestSource);
    }

    [Fact]
    public void SampleDiagnosticsVerifier_DotnetProcessExecution_LivesInDedicatedPartial()
    {
        var verifierDir = Path.Combine(
            _repoRoot,
            "tools",
            "SampleDiagnosticsVerifier");
        var programPath = Path.Combine(verifierDir, "Program.cs");
        var dotnetPath = Path.Combine(verifierDir, "Program.Dotnet.cs");

        Assert.True(File.Exists(dotnetPath), "Sample diagnostics dotnet process execution should live in a focused Program partial file.");

        var programSource = File.ReadAllText(programPath);
        Assert.DoesNotContain("static string RunDotnetBuild", programSource);
        Assert.DoesNotContain("static void RunDotnetRestore", programSource);
        Assert.DoesNotContain("static string Quote", programSource);

        var dotnetSource = File.ReadAllText(dotnetPath);
        Assert.Contains("private static string RunDotnetBuild", dotnetSource);
        Assert.Contains("private static void RunDotnetRestore", dotnetSource);
        Assert.Contains("private static string Quote", dotnetSource);
        Assert.Contains("ProcessStartInfo", dotnetSource);
    }
}
