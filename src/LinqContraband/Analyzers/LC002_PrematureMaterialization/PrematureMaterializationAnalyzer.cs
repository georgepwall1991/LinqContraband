using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

/// <summary>
/// Analyzes premature materialization of IQueryable collections before filtering operations. Diagnostic ID: LC002
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Materializing an IQueryable (using ToList, ToArray, etc.) before applying filters causes
/// all data to be fetched from the database into memory before filtering occurs. This prevents the database from optimizing
/// the query and can result in fetching thousands or millions of unnecessary records. Always apply filters (Where, Take, Skip)
/// before materializing to leverage database-side query optimization and reduce network traffic and memory consumption.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PrematureMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC002";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Premature materialization of IQueryable";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' on materialized collection but source was IQueryable. This fetches all data before filtering.";

    private static readonly LocalizableString Description =
        "Ensure filtering happens before materialization (ToList, ToArray, etc).";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

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

        var receiverType = invocation.Instance?.Type ??
                           (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value.Type : null);

        if (receiverType == null) return;

        if (receiverType.IsIQueryable()) return;

        if (!IsLinqOperator(methodSymbol)) return;

        var receiverOp = invocation.Instance ??
                         (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

        receiverOp = Unwrap(receiverOp);

        if (receiverOp is IInvocationOperation previousInvocation)
        {
            if (IsMaterializingMethod(previousInvocation.TargetMethod))
            {
                // Check *that* method's receiver. Was it IQueryable?
                var sourceOp = previousInvocation.Instance ??
                               (previousInvocation.Arguments.Length > 0 ? previousInvocation.Arguments[0].Value : null);

                // Handle conversion on sourceOp too (e.g. implicit conversion from List to IEnumerable in chain)
                sourceOp = Unwrap(sourceOp);

                var sourceType = sourceOp?.Type;
                if (sourceType.IsIQueryable())
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
            }
        }
        else if (receiverOp is IObjectCreationOperation objectCreation)
        {
            if (objectCreation.Constructor != null && IsMaterializingConstructor(objectCreation.Constructor))
            {
                // Check constructor argument (usually the first one is the source collection)
                if (objectCreation.Arguments.Length > 0)
                {
                    var sourceOp = objectCreation.Arguments[0].Value;
                    sourceOp = Unwrap(sourceOp);
                    
                    if (sourceOp?.Type != null && sourceOp.Type.IsIQueryable())
                         context.ReportDiagnostic(
                             Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodSymbol.Name));
                }
            }
        }
    }

    private IOperation? Unwrap(IOperation? op)
    {
        var current = op;
        while (current is IConversionOperation conversion) current = conversion.Operand;
        while (current is IAwaitOperation awaitOp) current = awaitOp.Operation;
        return current;
    }

    private bool IsLinqOperator(IMethodSymbol method)
    {
        return method.ContainingType.Name == "Enumerable" &&
               method.ContainingNamespace?.ToString() == "System.Linq";
    }

    private bool IsMaterializingMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        if (ns is not ("System.Linq" or "Microsoft.EntityFrameworkCore")) return false;

        if (method.Name == "AsEnumerable") return true; // switches to client-side

        return method.Name == "ToList" ||
               method.Name == "ToListAsync" ||
               method.Name == "ToArray" ||
               method.Name == "ToArrayAsync" ||
               method.Name == "ToDictionary" ||
               method.Name == "ToDictionaryAsync" ||
               method.Name == "ToHashSet" ||
               method.Name == "ToHashSetAsync" ||
               method.Name == "ToLookup";
    }

    private bool IsMaterializingConstructor(IMethodSymbol constructor)
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
