using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal static class NPlusOneLooperDiagnosticProperties
{
    public const string PatternKind = "LC007.PatternKind";
    public const string MethodName = "LC007.MethodName";
    public const string LoopKind = "LC007.LoopKind";
    public const string FixerEligible = "LC007.FixerEligible";

    public const string Find = "Find";
    public const string ExplicitLoad = "ExplicitLoad";
    public const string NavigationQueryMaterializer = "NavigationQueryMaterializer";
    public const string EfQueryMaterializer = "EfQueryMaterializer";
    public const string EfSetBasedExecutor = "EfSetBasedExecutor";
}

internal sealed class NPlusOneLoopMatch
{
    public NPlusOneLoopMatch(string patternKind, string methodName, string loopKind, bool fixerEligible)
    {
        PatternKind = patternKind;
        MethodName = methodName;
        LoopKind = loopKind;
        FixerEligible = fixerEligible;
    }

    public string PatternKind { get; }
    public string MethodName { get; }
    public string LoopKind { get; }
    public bool FixerEligible { get; }
}

internal static class NPlusOneLooperAnalysis
{
    private static readonly HashSet<string> ImmediateQueryExecutionMethods = new(StringComparer.Ordinal)
    {
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
        "ToDictionary",
        "ToDictionaryAsync",
        "ToHashSet",
        "ToHashSetAsync",
        "First",
        "FirstOrDefault",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "Single",
        "SingleOrDefault",
        "SingleAsync",
        "SingleOrDefaultAsync",
        "Last",
        "LastOrDefault",
        "LastAsync",
        "LastOrDefaultAsync",
        "Count",
        "LongCount",
        "CountAsync",
        "LongCountAsync",
        "Any",
        "All",
        "AnyAsync",
        "AllAsync",
        "Sum",
        "Average",
        "Min",
        "Max",
        "SumAsync",
        "AverageAsync",
        "MinAsync",
        "MaxAsync",
        "ForEachAsync"
    };

    private static readonly HashSet<string> SetBasedExecutorMethods = new(StringComparer.Ordinal)
    {
        "ExecuteDelete",
        "ExecuteDeleteAsync",
        "ExecuteUpdate",
        "ExecuteUpdateAsync"
    };

    private static readonly ConditionalWeakTable<IOperation, LocalWriteCache> LocalWriteCaches = new();

    public static NPlusOneLoopMatch? AnalyzeInvocation(IInvocationOperation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var loop = invocation.FindEnclosingLoop();
        if (loop == null || !invocation.SharesOwningExecutableRoot(loop))
            return null;

        if (!IsPerIterationInvocation(invocation, loop))
            return null;

        if (!TryMatchDatabaseExecution(invocation, cancellationToken, out var match))
            return null;

        return new NPlusOneLoopMatch(match.PatternKind, match.MethodName, loop.GetLoopKind(), match.FixerEligible);
    }

    private static bool IsPerIterationInvocation(IInvocationOperation invocation, ILoopOperation loop)
    {
        var spanStart = invocation.Syntax.SpanStart;

        return loop.Syntax switch
        {
            ForEachStatementSyntax forEach => forEach.Statement.Span.Contains(spanStart),
            ForStatementSyntax forStatement =>
                forStatement.Statement.Span.Contains(spanStart) ||
                (forStatement.Condition?.Span.Contains(spanStart) == true) ||
                forStatement.Incrementors.Any(incrementor => incrementor.Span.Contains(spanStart)),
            WhileStatementSyntax whileStatement =>
                whileStatement.Statement.Span.Contains(spanStart) ||
                whileStatement.Condition.Span.Contains(spanStart),
            DoStatementSyntax doStatement =>
                doStatement.Statement.Span.Contains(spanStart) ||
                doStatement.Condition.Span.Contains(spanStart),
            _ => false
        };
    }

    public static bool IsProvenEfQuerySource(IOperation? operation, CancellationToken cancellationToken = default)
    {
        return AnalyzeQueryProvenance(operation, operation, cancellationToken).Kind == QueryProvenanceKind.Proven;
    }

    public static bool HasStronglyTypedNavigationAccessor(IInvocationOperation loadInvocation)
    {
        if (loadInvocation.GetInvocationReceiver() is not IInvocationOperation accessInvocation)
            return false;

        if (!IsNavigationAccessInvocation(accessInvocation))
            return false;

        return accessInvocation.Arguments.Length == 1 &&
               accessInvocation.Arguments[0].Value is IAnonymousFunctionOperation;
    }

    private static bool TryMatchDatabaseExecution(
        IInvocationOperation invocation,
        CancellationToken cancellationToken,
        out NPlusOneLoopMatch match)
    {
        var method = invocation.TargetMethod;

        if (method.Name is "Find" or "FindAsync" && method.ContainingType.IsDbSet())
        {
            match = new NPlusOneLoopMatch(
                NPlusOneLooperDiagnosticProperties.Find,
                method.Name,
                string.Empty,
                false);
            return true;
        }

        if (method.Name is "Load" or "LoadAsync" && IsExplicitLoadReceiver(invocation.GetInvocationReceiverType()))
        {
            match = new NPlusOneLoopMatch(
                NPlusOneLooperDiagnosticProperties.ExplicitLoad,
                method.Name,
                string.Empty,
                HasStronglyTypedNavigationAccessor(invocation));
            return true;
        }

        if (!ImmediateQueryExecutionMethods.Contains(method.Name) && !SetBasedExecutorMethods.Contains(method.Name))
        {
            match = null!;
            return false;
        }

        var provenance = AnalyzeQueryProvenance(invocation.GetInvocationReceiver(), invocation, cancellationToken);
        if (provenance.Kind != QueryProvenanceKind.Proven)
        {
            match = null!;
            return false;
        }

        match = new NPlusOneLoopMatch(
            SetBasedExecutorMethods.Contains(method.Name)
                ? NPlusOneLooperDiagnosticProperties.EfSetBasedExecutor
                : provenance.IsNavigationQuery
                    ? NPlusOneLooperDiagnosticProperties.NavigationQueryMaterializer
                    : NPlusOneLooperDiagnosticProperties.EfQueryMaterializer,
            method.Name,
            string.Empty,
            false);
        return true;
    }

    private static QueryProvenance AnalyzeQueryProvenance(
        IOperation? operation,
        IOperation? analysisScope,
        CancellationToken cancellationToken)
    {
        if (operation == null || analysisScope == null)
            return QueryProvenance.None;

        var visitedLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var current = operation.UnwrapConversions();

        while (current != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = current.UnwrapConversions();

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (IsClientBoundaryInvocation(invocation))
                        return QueryProvenance.None;

                    if (IsDbContextSetInvocation(invocation))
                        return QueryProvenance.Proven;

                    if (IsNavigationQueryInvocation(invocation))
                        return QueryProvenance.NavigationQuery;

                    if (IsAsQueryableInvocation(invocation))
                        return QueryProvenance.Ambiguous;

                    current = invocation.GetInvocationReceiver();
                    continue;

                case IPropertyReferenceOperation propertyReference:
                    if (propertyReference.Type.IsDbSet())
                        return QueryProvenance.Proven;

                    if (propertyReference.Type.IsIQueryable())
                        return QueryProvenance.Ambiguous;

                    return QueryProvenance.None;

                case IFieldReferenceOperation fieldReference:
                    if (fieldReference.Type.IsDbSet())
                        return QueryProvenance.Proven;

                    if (fieldReference.Type.IsIQueryable())
                        return QueryProvenance.Ambiguous;

                    return QueryProvenance.None;

                case ILocalReferenceOperation localReference:
                    if (!visitedLocals.Add(localReference.Local))
                        return QueryProvenance.Ambiguous;

                    if (!TryGetSingleAssignedLocalValue(localReference.Local, analysisScope, cancellationToken, out var valueOperation))
                    {
                        if (localReference.Type.IsDbSet() || localReference.Type.IsIQueryable())
                            return QueryProvenance.Ambiguous;

                        return QueryProvenance.None;
                    }

                    current = valueOperation;
                    continue;

                case IParameterReferenceOperation parameterReference:
                    if (parameterReference.Type.IsDbSet() ||
                        parameterReference.Type.IsIQueryable() ||
                        parameterReference.Type.IsDbContext())
                    {
                        return QueryProvenance.Ambiguous;
                    }

                    return QueryProvenance.None;

                default:
                    if (current.Type?.IsDbSet() == true)
                        return QueryProvenance.Proven;

                    if (current.Type?.IsIQueryable() == true)
                        return QueryProvenance.Ambiguous;

                    return QueryProvenance.None;
            }
        }

        return QueryProvenance.None;
    }

    private static bool TryGetSingleAssignedLocalValue(
        ILocalSymbol local,
        IOperation analysisScope,
        CancellationToken cancellationToken,
        out IOperation valueOperation)
    {
        valueOperation = null!;

        if (local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declarator = local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) as VariableDeclaratorSyntax;
        if (declarator?.Initializer?.Value == null)
            return false;

        var semanticModel = analysisScope.SemanticModel;
        var executableRoot = analysisScope.FindOwningExecutableRoot();
        if (semanticModel == null || executableRoot == null)
            return false;

        var localWrites = LocalWriteCaches.GetValue(
            executableRoot,
            root => new LocalWriteCache(root.Syntax, semanticModel));

        if (localWrites.HasWrite(local, cancellationToken))
            return false;

        var operation = semanticModel.GetOperation(declarator.Initializer.Value, cancellationToken);
        if (operation == null)
            return false;

        valueOperation = operation;
        return true;
    }

    private sealed class LocalWriteCache
    {
        private readonly SyntaxNode executableRootSyntax;
        private readonly SemanticModel semanticModel;
        private readonly object syncRoot = new();
        private HashSet<ILocalSymbol>? writtenLocals;

        public LocalWriteCache(SyntaxNode executableRootSyntax, SemanticModel semanticModel)
        {
            this.executableRootSyntax = executableRootSyntax;
            this.semanticModel = semanticModel;
        }

        public bool HasWrite(ILocalSymbol local, CancellationToken cancellationToken)
        {
            return GetWrittenLocals(cancellationToken).Contains(local);
        }

        private HashSet<ILocalSymbol> GetWrittenLocals(CancellationToken cancellationToken)
        {
            if (writtenLocals != null)
                return writtenLocals;

            lock (syncRoot)
            {
                writtenLocals ??= BuildWrittenLocals(cancellationToken);
                return writtenLocals;
            }
        }

        private HashSet<ILocalSymbol> BuildWrittenLocals(CancellationToken cancellationToken)
        {
            var locals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

            foreach (var node in executableRootSyntax.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                ExpressionSyntax? target = node switch
                {
                    AssignmentExpressionSyntax assignment => assignment.Left,
                    PrefixUnaryExpressionSyntax prefix when
                        prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                        prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                    PostfixUnaryExpressionSyntax postfix when
                        postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                        postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                    _ => null
                };

                if (target == null)
                    continue;

                if (semanticModel.GetSymbolInfo(target, cancellationToken).Symbol is ILocalSymbol local)
                    locals.Add(local);
            }

            return locals;
        }
    }

    private static bool IsDbContextSetInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Set" &&
               invocation.TargetMethod.ContainingType.IsDbContext();
    }

    private static bool IsClientBoundaryInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "AsEnumerable" ||
               ImmediateQueryExecutionMethods.Contains(invocation.TargetMethod.Name) ||
               SetBasedExecutorMethods.Contains(invocation.TargetMethod.Name);
    }

    private static bool IsNavigationQueryInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Query" &&
               IsChangeTrackingNamespace(invocation.TargetMethod.ContainingType.ContainingNamespace);
    }

    private static bool IsNavigationAccessInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name is "Reference" or "Collection" &&
               IsChangeTrackingNamespace(invocation.TargetMethod.ContainingType.ContainingNamespace);
    }

    private static bool IsAsQueryableInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "AsQueryable" &&
               invocation.TargetMethod.IsFrameworkMethod();
    }

    private static bool IsExplicitLoadReceiver(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return type.Name is "ReferenceEntry" or "CollectionEntry" &&
               IsChangeTrackingNamespace(type.ContainingNamespace);
    }

    private static bool IsChangeTrackingNamespace(INamespaceSymbol? ns)
    {
        return ns?.ToString() == "Microsoft.EntityFrameworkCore.ChangeTracking";
    }

    private enum QueryProvenanceKind
    {
        None,
        Proven,
        Ambiguous
    }

    private readonly struct QueryProvenance
    {
        public static QueryProvenance None => new(QueryProvenanceKind.None, false);
        public static QueryProvenance Proven => new(QueryProvenanceKind.Proven, false);
        public static QueryProvenance NavigationQuery => new(QueryProvenanceKind.Proven, true);
        public static QueryProvenance Ambiguous => new(QueryProvenanceKind.Ambiguous, false);

        public QueryProvenance(QueryProvenanceKind kind, bool isNavigationQuery)
        {
            Kind = kind;
            IsNavigationQuery = isNavigationQuery;
        }

        public QueryProvenanceKind Kind { get; }
        public bool IsNavigationQuery { get; }
    }
}
