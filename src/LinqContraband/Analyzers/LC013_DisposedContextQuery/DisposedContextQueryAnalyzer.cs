using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC013_DisposedContextQuery;

/// <summary>
/// Analyzes deferred LINQ queries (IQueryable/IAsyncEnumerable) that are returned from disposed DbContext instances. Diagnostic ID: LC013
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> LINQ queries against DbContext are deferred and only execute when enumerated. Returning
/// an unenumerated query from a method where the DbContext is disposed (using statement or using declaration) will cause
/// runtime errors when the query is eventually executed. Queries should be materialized (ToList, ToArray, etc.) before
/// the context is disposed.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisposedContextQueryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC013";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Disposed Context Query";

    private static readonly LocalizableString MessageFormat =
        "The query is built from DbContext '{0}' which is disposed before enumeration. Materialize before returning.";

    private static readonly LocalizableString Description =
        "Returning a deferred query from a disposed context causes runtime errors.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // Register operation action
        context.RegisterOperationAction(AnalyzeReturn, OperationKind.Return);
    }

    private void AnalyzeReturn(OperationAnalysisContext context)
    {
        var returnOp = (IReturnOperation)context.Operation;
        var returnedValue = returnOp.ReturnedValue?.UnwrapConversions();

        if (returnedValue == null)
            return;

        var executableRoot = returnOp.FindOwningExecutableRoot();
        if (!IsSupportedExecutableRoot(executableRoot))
            return;

        if (!IsDeferredType(returnedValue.Type))
            return;

        CheckExpression(returnedValue, executableRoot!, context);
    }

    private void CheckExpression(IOperation? operation, IOperation executableRoot, OperationAnalysisContext context)
    {
        if (operation == null)
            return;

        operation = operation.UnwrapConversions();

        if (operation is IConditionalOperation conditional)
        {
            CheckExpression(conditional.WhenTrue, executableRoot, context);
            CheckExpression(conditional.WhenFalse, executableRoot, context);
            return;
        }

        if (operation is ICoalesceOperation coalesce)
        {
            CheckExpression(coalesce.Value, executableRoot, context);
            CheckExpression(coalesce.WhenNull, executableRoot, context);
            return;
        }

        if (operation is ISwitchExpressionOperation switchExpr)
        {
            foreach (var arm in switchExpr.Arms)
                CheckExpression(arm.Value, executableRoot, context);
            return;
        }

        if (TryResolveDisposedContextOrigin(
                operation,
                executableRoot,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                out var dbContextLocal))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation(), dbContextLocal.Name));
        }
    }

    private bool IsDeferredType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return type.IsIQueryable() ||
               ImplementsInterface(type, "System.Collections.Generic.IAsyncEnumerable`1");
    }

    private static bool ImplementsInterface(ITypeSymbol type, string interfaceMetadataName)
    {
        if (GetFullMetadataName(type) == interfaceMetadataName)
            return true;

        foreach (var i in type.AllInterfaces)
            if (GetFullMetadataName(i) == interfaceMetadataName)
                return true;
        return false;
    }

    private static string GetFullMetadataName(ITypeSymbol type)
    {
        return $"{type.ContainingNamespace}.{type.MetadataName}";
    }

    private static bool TryResolveDisposedContextOrigin(
        IOperation operation,
        IOperation executableRoot,
        HashSet<ILocalSymbol> visitedLocals,
        out ILocalSymbol dbContextLocal)
    {
        operation = operation.UnwrapConversions();
        dbContextLocal = null!;

        switch (operation)
        {
            case ILocalReferenceOperation localReference:
                if (IsDisposedDbContextLocal(localReference.Local, executableRoot))
                {
                    dbContextLocal = localReference.Local;
                    return true;
                }

                if (!visitedLocals.Add(localReference.Local))
                    return false;

                if (!TryResolveAssignedDisposedContextOrigin(
                        localReference.Local,
                        localReference.Syntax.SpanStart,
                        executableRoot,
                        visitedLocals,
                        out dbContextLocal))
                {
                    return false;
                }

                return true;

            case IInvocationOperation invocation when TryGetQueryChainReceiver(invocation, out var receiver):
                return TryResolveDisposedContextOrigin(receiver, executableRoot, visitedLocals, out dbContextLocal);

            case IMemberReferenceOperation memberReference when memberReference.Instance != null:
                return TryResolveDisposedContextOrigin(memberReference.Instance, executableRoot, visitedLocals,
                    out dbContextLocal);

            case IArgumentOperation argument:
                return TryResolveDisposedContextOrigin(argument.Value, executableRoot, visitedLocals, out dbContextLocal);

            default:
                return false;
        }
    }

    private static bool TryResolveAssignedDisposedContextOrigin(
        ILocalSymbol local,
        int position,
        IOperation executableRoot,
        HashSet<ILocalSymbol> visitedLocals,
        out ILocalSymbol dbContextLocal)
    {
        dbContextLocal = null!;

        if (TryGetSingleAssignedValue(local, position, executableRoot, out var assignedValue))
            return TryResolveDisposedContextOrigin(assignedValue, executableRoot, visitedLocals, out dbContextLocal);

        return TryGetSharedAssignedDisposedContextOrigin(local, position, executableRoot, visitedLocals,
            out dbContextLocal);
    }

    private static bool TryGetSharedAssignedDisposedContextOrigin(
        ILocalSymbol local,
        int position,
        IOperation executableRoot,
        HashSet<ILocalSymbol> visitedLocals,
        out ILocalSymbol dbContextLocal)
    {
        dbContextLocal = null!;
        var assignments = GetAssignedValues(local, position, executableRoot);

        if (assignments.Count <= 1)
            return false;

        foreach (var assignment in assignments)
        {
            if (!TryResolveDisposedContextOrigin(
                    assignment,
                    executableRoot,
                    new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                    out var candidate))
            {
                return false;
            }

            if (dbContextLocal == null)
            {
                dbContextLocal = candidate;
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(dbContextLocal, candidate))
                return false;
        }

        return dbContextLocal != null;
    }

    private static bool TryGetQueryChainReceiver(IInvocationOperation invocation, out IOperation receiver)
    {
        receiver = null!;

        if (invocation.Instance != null)
        {
            receiver = invocation.Instance;
            return true;
        }

        if (invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length > 0)
        {
            receiver = invocation.Arguments[0].Value;
            return true;
        }

        return false;
    }

    private static bool IsSupportedExecutableRoot(IOperation? executableRoot)
    {
        return executableRoot is IMethodBodyOperation or ILocalFunctionOperation or IAnonymousFunctionOperation;
    }

    private static bool TryGetSingleAssignedValue(
        ILocalSymbol local,
        int position,
        IOperation executableRoot,
        out IOperation value)
    {
        var assignments = GetAssignedValues(local, position, executableRoot);
        value = null!;

        if (assignments.Count != 1)
            return false;

        value = assignments[0];
        return true;
    }

    private static List<IOperation> GetAssignedValues(
        ILocalSymbol local,
        int position,
        IOperation executableRoot)
    {
        var assignments = new List<(int Position, IOperation Value)>();

        foreach (var operation in EnumerateOperations(executableRoot))
        {
            if (operation.Syntax.SpanStart >= position)
                continue;

            switch (operation)
            {
                case IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                         declarator.Initializer != null:
                    assignments.Add((declarator.Syntax.SpanStart, declarator.Initializer.Value.UnwrapConversions()));
                    break;

                case ISimpleAssignmentOperation assignment
                    when IsLocalTarget(assignment.Target, local):
                    assignments.Add((assignment.Syntax.SpanStart, assignment.Value.UnwrapConversions()));
                    break;

                case ICompoundAssignmentOperation compoundAssignment
                    when IsLocalTarget(compoundAssignment.Target, local):
                    return new List<IOperation>();

                case IIncrementOrDecrementOperation incrementOrDecrement
                    when IsLocalTarget(incrementOrDecrement.Target, local):
                    return new List<IOperation>();
            }
        }

        assignments.Sort(static (left, right) => left.Position.CompareTo(right.Position));
        return assignments.ConvertAll(static assignment => assignment.Value);
    }

    private static bool IsLocalTarget(IOperation target, ILocalSymbol local)
    {
        return target.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, local);
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation executableRoot)
    {
        yield return executableRoot;

        foreach (var operation in executableRoot.Descendants())
        {
            if (IsInsideNestedExecutable(operation, executableRoot))
                continue;

            yield return operation;
        }
    }

    private static bool IsInsideNestedExecutable(IOperation operation, IOperation executableRoot)
    {
        var current = operation.Parent;
        while (current != null && !ReferenceEquals(current, executableRoot))
        {
            if (current is ILocalFunctionOperation or IAnonymousFunctionOperation)
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static bool IsDisposedDbContextLocal(ILocalSymbol local, IOperation executableRoot)
    {
        if (!local.Type.IsDbContext())
            return false;

        foreach (var syntaxRef in local.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax.SyntaxTree != executableRoot.Syntax.SyntaxTree ||
                !executableRoot.Syntax.Span.Contains(syntax.Span))
            {
                continue;
            }

            if (syntax is VariableDeclaratorSyntax declarator)
            {
                // Case 1: using var x = ...; or await using var x = ...; (LocalDeclarationStatement)
                if (declarator.Parent is VariableDeclarationSyntax declaration &&
                    declaration.Parent is LocalDeclarationStatementSyntax localDecl)
                    if (!localDecl.UsingKeyword.IsKind(SyntaxKind.None) ||
                        !localDecl.AwaitKeyword.IsKind(SyntaxKind.None))
                        return true;

                // Case 2: using (var x = ...) { } (UsingStatement)
                if (declarator.Parent is VariableDeclarationSyntax declaration2 &&
                    declaration2.Parent is UsingStatementSyntax)
                    return true;
            }
        }

        return false;
    }
}
