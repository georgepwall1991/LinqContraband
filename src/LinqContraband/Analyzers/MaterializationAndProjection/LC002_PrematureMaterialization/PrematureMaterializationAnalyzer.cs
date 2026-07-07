using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

/// <summary>
/// Analyzes query continuations that happen only after an IQueryable has already been materialized. Diagnostic ID: LC002
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class PrematureMaterializationAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null) return;

        var receiverType = receiver.Type;
        var unwrappedReceiver = receiver.UnwrapConversions();

        if (TryCreateRedundantDiagnostic(context, invocation, unwrappedReceiver, out var redundantDiagnostic))
        {
            context.ReportDiagnostic(redundantDiagnostic);
            return;
        }

        if (receiverType?.IsIQueryable() == true) return;
        if (!IsApprovedContinuationMethod(invocation.TargetMethod, out _)) return;
        if (!HasProviderSafeContinuationArguments(invocation)) return;

        if (!TryResolveInlineMaterializationOrigin(unwrappedReceiver, out var materializationOrigin))
        {
            return;
        }

        var properties = CreateProperties(
            ContinuationDiagnosticKind,
            materializationOrigin.OriginKind,
            invocation.TargetMethod.Name,
            materializationOrigin.MaterializerName);

        if (CanOfferMoveBeforeMaterializationFix(invocation.TargetMethod, materializationOrigin))
        {
            properties = properties.SetItem(FixKindKey, MoveBeforeMaterializationFixKind);
        }

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, invocation.TargetMethod.Name));
    }

    private readonly struct MaterializationOrigin
    {
        public MaterializationOrigin(string originKind, string materializerName)
        {
            OriginKind = originKind;
            MaterializerName = materializerName;
        }

        public string OriginKind { get; }
        public string MaterializerName { get; }
    }
}
