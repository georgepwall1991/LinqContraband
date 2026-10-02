using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC062_BlockingEfAsyncCall;

/// <summary>
/// Analyzes code that blocks on the task of an EF Core async operation with <c>.Result</c>, <c>.Wait()</c> or
/// <c>.GetAwaiter().GetResult()</c>. Diagnostic ID: LC062
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> sync-over-async holds a thread while EF Core waits for the database, and the
/// continuation needs another thread to finish the task. Under load this starves the thread pool; under a
/// <c>SynchronizationContext</c> (WinForms, WPF, classic ASP.NET) the continuation waits for the thread that is
/// blocked on it, and the call deadlocks. Await the task, or call the synchronous EF Core API when the code cannot
/// be async. LC008 is the reverse case: a synchronous EF Core call inside an async method.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class BlockingEfAsyncCallAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC062";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Blocking on an EF Core async call";

    private static readonly LocalizableString MessageFormat =
        "'{0}' blocks the thread until EF Core's '{1}' completes, which starves the thread pool and can deadlock; await it, or call the synchronous API";

    private static readonly LocalizableString Description =
        "Blocking on an EF Core async operation with .Result, .Wait() or .GetAwaiter().GetResult() holds a thread while the database works. Under load this starves the thread pool, and under a SynchronizationContext (WinForms, WPF, classic ASP.NET) it deadlocks. Await the task, or use the synchronous EF Core method.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC062_BlockingEfAsyncCall.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (reference.Property.Name != "Result" ||
            reference.Instance == null ||
            !IsGenericTaskLike(reference.Property.ContainingType))
        {
            return;
        }

        Report(context, reference, reference.Instance, ".Result");
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.IsStatic || invocation.Instance == null)
            return;

        // task.Wait(), with or without a timeout or token, blocks; Wait(0) and Wait(TimeSpan.Zero) only poll.
        if (method.Name == "Wait" && IsTask(method.ContainingType))
        {
            if (IsZeroTimeoutPoll(invocation))
                return;

            Report(context, invocation, invocation.Instance, ".Wait()");
            return;
        }

        // task.GetAwaiter().GetResult(), also through ConfigureAwait(...).
        if (method.Name == "GetResult" &&
            method.Parameters.Length == 0 &&
            method.ContainingType?.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices" &&
            invocation.Instance.UnwrapConversions() is IInvocationOperation
            {
                TargetMethod: { Name: "GetAwaiter", Parameters.Length: 0, IsStatic: false },
                Instance: { } awaitable
            })
        {
            Report(context, invocation, awaitable, ".GetAwaiter().GetResult()");
        }
    }

    private static bool IsZeroTimeoutPoll(IInvocationOperation wait)
    {
        foreach (var argument in wait.Arguments)
        {
            if (argument.Parameter?.Name is not ("millisecondsTimeout" or "timeout"))
                continue;

            var value = argument.Value.UnwrapConversions();
            if (value.ConstantValue is { HasValue: true, Value: int milliseconds } && milliseconds == 0)
                return true;

            // default, default(int), default(TimeSpan), new TimeSpan() and new TimeSpan(0) are all a zero timeout.
            if (value is IDefaultValueOperation ||
                value is IObjectCreationOperation { Initializer: null, Type: { } created } creation &&
                IsTimeSpan(created) &&
                creation.Arguments.All(timeSpanArgument => IsConstantZero(timeSpanArgument.Value)))
            {
                return true;
            }

            if (value is IFieldReferenceOperation { Field: { Name: "Zero", IsStatic: true } field } && IsTimeSpan(field.ContainingType))
                return true;

            // TimeSpan.FromMilliseconds(0), TimeSpan.FromSeconds(0) and the other TimeSpan.FromXxx factories.
            if (value is IInvocationOperation { Instance: null, TargetMethod: { IsStatic: true } factory } fromCall &&
                factory.Name.StartsWith("From", System.StringComparison.Ordinal) &&
                IsTimeSpan(factory.ContainingType) &&
                fromCall.Arguments.Length > 0 &&
                fromCall.Arguments.All(factoryArgument => IsConstantZero(factoryArgument.Value)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConstantZero(IOperation value)
    {
        return value.UnwrapConversions().ConstantValue is { HasValue: true, Value: int or long or short or double or float } constant &&
               System.Convert.ToDouble(constant.Value, System.Globalization.CultureInfo.InvariantCulture) == 0;
    }

    private static bool IsTimeSpan(ITypeSymbol type)
    {
        return type is { Name: "TimeSpan" } && type.ContainingNamespace?.ToDisplayString() == "System";
    }

    private static void Report(OperationAnalysisContext context, IOperation site, IOperation taskExpression, string blockingText)
    {
        if (IsInsideNameOf(site))
            return;

        if (!TryGetBlockedEfOperation(taskExpression, site, out var efInvocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            site.Syntax.GetLocation(),
            blockingText,
            efInvocation.TargetMethod.Name));
    }

    /// <summary>
    /// Follows the blocked task back to the EF Core async call that produced it: directly, through
    /// <c>ConfigureAwait</c>/<c>AsTask</c>, or through a local assigned once from it, with no await and no other use
    /// of the local before the blocking site (either may already have completed the task).
    /// </summary>
    internal static bool TryGetBlockedEfOperation(IOperation taskExpression, IOperation site, out IInvocationOperation efInvocation)
    {
        efInvocation = null!;
        var current = UnwrapTaskAdapters(taskExpression);

        if (current is ILocalReferenceOperation localReference)
        {
            if (!TryGetSingleUnobservedAssignment(localReference.Local, site, out var value))
                return false;

            current = UnwrapTaskAdapters(value);
        }

        if (current is not IInvocationOperation invocation || !IsEfCoreAsyncOperation(invocation))
            return false;

        efInvocation = invocation;
        return true;
    }

    /// <summary>Unwraps conversions, parentheses, <c>ConfigureAwait(...)</c> and <c>ValueTask.AsTask()</c>.</summary>
    internal static IOperation UnwrapTaskAdapters(IOperation operation)
    {
        var current = operation;
        for (var depth = 0; depth < 8; depth++)
        {
            current = current.UnwrapConversions();
            if (current is IParenthesizedOperation parenthesized)
            {
                current = parenthesized.Operand;
                continue;
            }

            if (current is IInvocationOperation
                {
                    TargetMethod: { IsStatic: false } adapter,
                    Instance: { } adapted
                } &&
                IsTaskLike(adapter.ContainingType) &&
                ((adapter.Name == "ConfigureAwait" && adapter.Parameters.Length == 1) ||
                 (adapter.Name == "AsTask" && adapter.Parameters.Length == 0)))
            {
                current = adapted;
                continue;
            }

            break;
        }

        return current;
    }

    private static bool TryGetSingleUnobservedAssignment(ILocalSymbol local, IOperation site, out IOperation value)
    {
        value = null!;
        if (local.RefKind != RefKind.None)
            return false;

        var siteRoot = site.FindOwningExecutableRoot();
        if (siteRoot == null)
            return false;

        var topRoot = siteRoot;
        while (topRoot.Parent != null)
            topRoot = topRoot.Parent;

        var assignments = LocalAssignmentCache.GetAssignments(topRoot, local);
        if (assignments.Count != 1)
            return false;

        var assignment = assignments[0];
        var siteStart = site.Syntax.SpanStart;
        if (assignment.SpanStart >= siteStart ||
            !ReferenceEquals(assignment.Value.FindOwningExecutableRoot(), siteRoot))
        {
            return false;
        }

        var assignedEnd = assignment.Value.Syntax.Span.End;
        foreach (var operation in topRoot.Descendants())
        {
            var start = operation.Syntax.SpanStart;
            if (start >= siteStart)
                continue;

            switch (operation)
            {
                // Any other use of the local before the site (await t, Task.WhenAll(t, ...), t.Wait(),
                // t.IsCompletedSuccessfully, handing it to a helper) may complete it or prove it complete.
                // A zero-timeout poll (t.Wait(0)) is not reported and proves nothing, so it does not count, unless it
                // is the condition of an `if` whose true branch holds the site.
                case ILocalReferenceOperation reference when
                    SymbolEqualityComparer.Default.Equals(reference.Local, local) &&
                    !IsAssignmentTarget(reference) &&
                    (!IsZeroTimeoutPollOn(reference, out var poll) || GuardsSite(poll, site)):
                    return false;

                // An await between the assignment and the site may complete the task too.
                case IAwaitOperation when start >= assignedEnd && ReferenceEquals(operation.FindOwningExecutableRoot(), siteRoot):
                case IForEachLoopOperation { IsAsynchronous: true } when start >= assignedEnd && ReferenceEquals(operation.FindOwningExecutableRoot(), siteRoot):
                case IUsingOperation { IsAsynchronous: true } when start >= assignedEnd && ReferenceEquals(operation.FindOwningExecutableRoot(), siteRoot):
                case IUsingDeclarationOperation { IsAsynchronous: true } when start >= assignedEnd && ReferenceEquals(operation.FindOwningExecutableRoot(), siteRoot):
                    return false;
            }
        }

        value = assignment.Value;
        return true;
    }

    private static bool IsZeroTimeoutPollOn(ILocalReferenceOperation reference, out IInvocationOperation poll)
    {
        poll = null!;
        IOperation current = reference;
        while (current.Parent is IConversionOperation conversion && ReferenceEquals(conversion.Operand, current))
            current = conversion;

        if (current.Parent is not IInvocationOperation { TargetMethod.Name: "Wait" } wait ||
            !ReferenceEquals(wait.Instance, current) ||
            !IsTask(wait.TargetMethod.ContainingType) ||
            !IsZeroTimeoutPoll(wait))
        {
            return false;
        }

        poll = wait;
        return true;
    }

    /// <summary>
    /// True when <paramref name="poll"/> being true is required for the site to run: the poll is the condition of an
    /// <c>if</c> or <c>?:</c> whose true branch holds the site, possibly through <c>== true</c>, <c>!= false</c>,
    /// <c>is true</c> or an operand of <c>&amp;&amp;</c>, or the left operand of an <c>&amp;&amp;</c> whose right operand holds the site.
    /// </summary>
    private static bool GuardsSite(IInvocationOperation poll, IOperation site)
    {
        IOperation condition = poll;
        while (true)
        {
            while (condition.Parent is IConversionOperation conversion && ReferenceEquals(conversion.Operand, condition))
                condition = conversion;

            switch (condition.Parent)
            {
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction:
                    if (ReferenceEquals(conjunction.LeftOperand, condition) &&
                        conjunction.RightOperand.Syntax.Span.Contains(site.Syntax.Span))
                    {
                        return true;
                    }

                    condition = conjunction;
                    continue;

                case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison
                    when IsBooleanConstantOperand(comparison, condition, comparison.OperatorKind == BinaryOperatorKind.Equals):
                    condition = comparison;
                    continue;

                case IIsPatternOperation { Pattern: IConstantPatternOperation { Value: { } constant } } isPattern
                    when ReferenceEquals(isPattern.Value, condition) &&
                         constant.UnwrapConversions().ConstantValue is { HasValue: true, Value: true }:
                    condition = isPattern;
                    continue;
            }

            break;
        }

        return condition.Parent is IConditionalOperation { WhenTrue: { } whenTrue } conditional &&
               ReferenceEquals(conditional.Condition, condition) &&
               whenTrue.Syntax.Span.Contains(site.Syntax.Span);
    }

    /// <summary>True when the other operand of <paramref name="comparison"/> is the constant that keeps <paramref name="operand"/>'s truth (<c>== true</c> or <c>!= false</c>).</summary>
    private static bool IsBooleanConstantOperand(IBinaryOperation comparison, IOperation operand, bool expected)
    {
        var other = ReferenceEquals(comparison.LeftOperand, operand) ? comparison.RightOperand :
            ReferenceEquals(comparison.RightOperand, operand) ? comparison.LeftOperand : null;
        return other?.UnwrapConversions().ConstantValue is { HasValue: true, Value: bool value } && value == expected;
    }

    private static bool IsAssignmentTarget(ILocalReferenceOperation reference)
    {
        IOperation current = reference;
        while (current.Parent is IConversionOperation conversion && ReferenceEquals(conversion.Operand, current))
            current = conversion;

        return current.Parent is ISimpleAssignmentOperation assignment && ReferenceEquals(assignment.Target, current);
    }

    private static bool IsInsideNameOf(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is INameOfOperation)
                return true;
        }

        return false;
    }

    private static bool IsTask(INamedTypeSymbol? type)
    {
        return type is { Name: "Task" } &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    private static bool IsGenericTaskLike(INamedTypeSymbol? type)
    {
        return type is { Name: "Task" or "ValueTask", Arity: 1 } &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    internal static bool IsTaskLike(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "Task" or "ValueTask" } named &&
               named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }
}
