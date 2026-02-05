using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

/// <summary>
/// Analyzes premature materialization of IQueryable collections before filtering operations. Diagnostic ID: LC002
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PrematureMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC002";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Premature materialization of IQueryable";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' on materialized collection but source was IQueryable. This fetches all data before filtering.";

    private static readonly LocalizableString RedundantMessageFormat =
        "The call to '{0}' is redundant because the sequence was already materialized by '{1}'";

    private static readonly LocalizableString Description =
        "Ensure filtering happens before materialization (ToList, ToArray, etc).";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description);

    public static readonly DiagnosticDescriptor RedundantRule = new(
        DiagnosticId,
        "Redundant materialization",
        RedundantMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, RedundantRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var methodSymbol = invocation.TargetMethod;

        var receiverOp = invocation.GetInvocationReceiver();
        if (receiverOp == null) return;

        var unwrappedReceiver = receiverOp.UnwrapConversions();
        var receiverType = receiverOp.Type;
        if (receiverType == null) return;

        // 1. Check for double materialization (RedundantRule)
        if (IsMaterializingMethod(methodSymbol))
        {
            if (unwrappedReceiver is IInvocationOperation previousInvocation && IsMaterializingMethod(previousInvocation.TargetMethod))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(RedundantRule, invocation.Syntax.GetLocation(), methodSymbol.Name, previousInvocation.TargetMethod.Name));
                return;
            }
        }

        // 2. Existing logic: filtering after materialization (Rule)
        if (receiverType.IsIQueryable()) return;
        if (!IsLinqOperator(methodSymbol)) return;

        if (CheckFilterAfterMaterializedInvocation(context, invocation, unwrappedReceiver, methodSymbol)) return;
        CheckFilterAfterMaterializedConstructor(context, invocation, unwrappedReceiver, methodSymbol);
    }

    private static bool CheckFilterAfterMaterializedInvocation(
        OperationAnalysisContext context, IInvocationOperation invocation,
        IOperation unwrappedReceiver, IMethodSymbol methodSymbol)
    {
        if (unwrappedReceiver is not IInvocationOperation prevInv) return false;
        if (!IsMaterializingMethod(prevInv.TargetMethod)) return false;

        var sourceType = prevInv.GetInvocationReceiverType();
        if (sourceType == null || !sourceType.IsIQueryable()) return false;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
        return true;
    }

    private static void CheckFilterAfterMaterializedConstructor(
        OperationAnalysisContext context, IInvocationOperation invocation,
        IOperation unwrappedReceiver, IMethodSymbol methodSymbol)
    {
        if (unwrappedReceiver is not IObjectCreationOperation objectCreation) return;
        if (objectCreation.Constructor == null || !IsMaterializingConstructor(objectCreation.Constructor)) return;
        if (objectCreation.Arguments.Length == 0) return;

        var sourceOp = objectCreation.Arguments[0].Value.UnwrapConversions();
        if (sourceOp?.Type == null || !sourceOp.Type.IsIQueryable()) return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
    }

    private bool IsLinqOperator(IMethodSymbol method)
    {
        return method.ContainingType.Name == "Enumerable" &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }

    private static bool IsMaterializingMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        if (ns is not ("System.Linq" or "Microsoft.EntityFrameworkCore" or "System.Collections.Immutable" or "System.Collections.Generic")) return false;

        if (method.Name == "AsEnumerable") return true;

        return method.Name == "ToList" ||
               method.Name == "ToListAsync" ||
               method.Name == "ToArray" ||
               method.Name == "ToArrayAsync" ||
               method.Name == "ToDictionary" ||
               method.Name == "ToDictionaryAsync" ||
               method.Name == "ToHashSet" ||
               method.Name == "ToHashSetAsync" ||
               method.Name == "ToLookup" ||
               method.Name.StartsWith("ToImmutable", StringComparison.Ordinal);
    }

    private static bool IsMaterializingConstructor(IMethodSymbol constructor)
    {
        var type = constructor.ContainingType;
        if (type.ContainingNamespace?.ToString() != "System.Collections.Generic") return false;

        return type.Name == "List" ||
               type.Name == "HashSet" ||
               type.Name == "Dictionary" ||
               type.Name == "SortedDictionary" ||
               type.Name == "SortedList" ||
               type.Name == "LinkedList" ||
               type.Name == "Queue" ||
               type.Name == "Stack";
    }
}
