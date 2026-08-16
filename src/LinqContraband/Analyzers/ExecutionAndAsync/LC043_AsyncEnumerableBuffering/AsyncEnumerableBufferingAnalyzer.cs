using System;
using System.Threading;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC043_AsyncEnumerableBuffering;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class AsyncEnumerableBufferingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC043";
    internal const string BufferMethodKey = "BufferMethod";

    private const string Category = "Performance";

    private static readonly LocalizableString Title = "Prefer await foreach over buffering async streams";

    private static readonly LocalizableString MessageFormat =
        "The async sequence is buffered with '{0}' immediately before a single foreach loop. Consider awaiting the sequence directly with await foreach.";

    private static readonly LocalizableString Description =
        "Buffering an IAsyncEnumerable into a list or array and then looping once is usually unnecessary. This narrow rule only reports immediate buffer-then-loop patterns.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC043_AsyncEnumerableBuffering.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        // Registered per operation block, not per loop: the diagnostic is reported on the
        // buffering call that precedes the loop, which lies outside the loop's own span. A
        // loop-scoped action makes Roslyn classify that report as a non-local
        // (compilation-level) diagnostic, which suppresses live IDE analysis and makes the
        // code fix unreliable. The block scope contains both nodes, so it stays local.
        context.RegisterOperationBlockAction(AnalyzeOperationBlock);
    }

    private void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
    {
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is IForEachLoopOperation loop)
                    AnalyzeForEach(loop, context.ReportDiagnostic, context.CancellationToken);
            }
        }
    }

    private void AnalyzeForEach(
        IForEachLoopOperation forEach,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        if (forEach.IsAsynchronous)
            return;

        if (forEach.Syntax is not ForEachStatementSyntax loopSyntax)
            return;

        var collection = forEach.Collection.UnwrapConversions();
        if (collection is not ILocalReferenceOperation localReference)
            return;

        var executableRoot = forEach.FindOwningExecutableRoot();
        if (executableRoot == null)
            return;

        if (!TryGetImmediateBufferedLocal(loopSyntax, localReference.Local, out var bufferInfo))
            return;

        if (!IsAsyncEnumerableBufferInvocation(bufferInfo.BufferInvocation, forEach.SemanticModel, cancellationToken))
            return;

        if (!HasSingleLocalUseInRoot(executableRoot, localReference.Local))
            return;

        reportDiagnostic(
            Diagnostic.Create(
                Rule,
                bufferInfo.BufferInvocation.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(BufferMethodKey, bufferInfo.BufferMethodName),
                bufferInfo.BufferMethodName));
    }

    private static bool HasSingleLocalUseInRoot(IOperation root, ILocalSymbol local)
    {
        var localUseCount = 0;

        foreach (var descendant in root.Descendants())
        {
            if (descendant is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            localUseCount++;
            if (localUseCount > 1)
                return false;
        }

        return localUseCount == 1;
    }

    private static bool IsAsyncEnumerableBufferInvocation(
        InvocationExpressionSyntax bufferInvocation,
        SemanticModel? semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel?.GetOperation(bufferInvocation, cancellationToken) is not IInvocationOperation invocation)
            return false;

        return IsAsyncEnumerable(invocation.GetInvocationReceiverType());
    }

    private static bool IsAsyncEnumerable(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (IsAsyncEnumerableType(type))
            return true;

        foreach (var implementedInterface in type.AllInterfaces)
            if (IsAsyncEnumerableType(implementedInterface))
                return true;

        return false;
    }

    private static bool IsAsyncEnumerableType(ITypeSymbol type)
    {
        return type.MetadataName == "IAsyncEnumerable`1" &&
               type.ContainingNamespace?.ToString() == "System.Collections.Generic";
    }

}
