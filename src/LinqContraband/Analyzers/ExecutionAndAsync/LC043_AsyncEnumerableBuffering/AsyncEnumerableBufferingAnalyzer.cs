using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC043_AsyncEnumerableBuffering;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncEnumerableBufferingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC043";
    internal const string BufferMethodKey = "BufferMethod";

    private const string Category = "Performance";
    private static readonly ImmutableHashSet<string> BufferMethods = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "ToListAsync",
        "ToArrayAsync");

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
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeForEach, OperationKind.Loop);
    }

    private void AnalyzeForEach(OperationAnalysisContext context)
    {
        if (context.Operation is not IForEachLoopOperation forEach || forEach.IsAsynchronous)
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

        if (!HasSingleLocalUseInRoot(executableRoot, localReference.Local))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                bufferInfo.BufferInvocation.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(BufferMethodKey, bufferInfo.BufferMethodName),
                bufferInfo.BufferMethodName));
    }

    internal static bool TryGetImmediateBufferedLocal(
        ForEachStatementSyntax loopSyntax,
        ILocalSymbol local,
        out BufferInfo bufferInfo)
    {
        bufferInfo = null!;

        if (loopSyntax.Parent is not BlockSyntax block)
            return false;

        var statements = block.Statements;
        var loopIndex = -1;
        for (var i = 0; i < statements.Count; i++)
        {
            if (ReferenceEquals(statements[i], loopSyntax))
            {
                loopIndex = i;
                break;
            }
        }

        if (loopIndex <= 0)
            return false;

        if (statements[loopIndex - 1] is not LocalDeclarationStatementSyntax localDeclaration)
            return false;

        if (localDeclaration.Declaration.Variables.Count != 1)
            return false;

        var declarator = localDeclaration.Declaration.Variables[0];
        if (declarator.Identifier.ValueText != local.Name)
            return false;

        if (declarator.Initializer?.Value is not AwaitExpressionSyntax awaitExpression)
            return false;

        if (awaitExpression.Expression is not InvocationExpressionSyntax invocationSyntax)
            return false;

        if (invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (invocationSyntax.ArgumentList.Arguments.Count != 0)
            return false;

        if (!BufferMethods.Contains(memberAccess.Name.Identifier.ValueText))
            return false;

        bufferInfo = new BufferInfo(localDeclaration, loopSyntax, invocationSyntax, memberAccess.Expression, memberAccess.Name.Identifier.ValueText);
        return true;
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

            if (!ReferenceEquals(localReference.FindOwningExecutableRoot(), root))
                continue;

            localUseCount++;
            if (localUseCount > 1)
                return false;
        }

        return localUseCount == 1;
    }

    internal sealed class BufferInfo
    {
        public BufferInfo(
            LocalDeclarationStatementSyntax localDeclaration,
            ForEachStatementSyntax loopSyntax,
            InvocationExpressionSyntax bufferInvocation,
            ExpressionSyntax sourceExpression,
            string bufferMethodName)
        {
            LocalDeclaration = localDeclaration;
            LoopSyntax = loopSyntax;
            BufferInvocation = bufferInvocation;
            SourceExpression = sourceExpression;
            BufferMethodName = bufferMethodName;
        }

        public LocalDeclarationStatementSyntax LocalDeclaration { get; }

        public ForEachStatementSyntax LoopSyntax { get; }

        public InvocationExpressionSyntax BufferInvocation { get; }

        public ExpressionSyntax SourceExpression { get; }

        public string BufferMethodName { get; }
    }
}
