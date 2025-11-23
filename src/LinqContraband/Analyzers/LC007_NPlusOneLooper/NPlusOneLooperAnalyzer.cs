using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NPlusOneLooperAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC007";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "N+1 Problem: Database query inside loop";

    private static readonly LocalizableString MessageFormat =
        "Executing '{0}' inside a loop causes N+1 queries. Fetch data in bulk outside the loop.";

    private static readonly LocalizableString Description =
        "Performing database queries inside a loop results in a database roundtrip for every iteration. This destroys performance via network latency.";

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
        var method = invocation.TargetMethod;

        // 1. Check if this is a DB execution method
        if (!IsDbExecutionMethod(method, invocation)) return;

        // 2. Check if inside a loop
        if (IsInsideLoop(invocation))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    private bool IsDbExecutionMethod(IMethodSymbol method, IInvocationOperation invocation)
    {
        // Case 1: DbSet.Find / FindAsync
        if (method.Name.StartsWith("Find") && IsDbSet(method.ContainingType))
        {
            return true;
        }

        // Case 2: IQueryable materializers (ToList, Count, First, etc.)
        if (!IsMaterializer(method.Name)) return false;

        ITypeSymbol? receiverType = null;

        if (invocation.Instance != null)
        {
            receiverType = invocation.Instance.Type;
        }
        else if (invocation.Arguments.Length > 0)
        {
            // Extension method: The first argument is the receiver.
            // It might be implicitly converted (e.g. IQueryable -> IEnumerable for ToList).
            var argVal = invocation.Arguments[0].Value;
            while (argVal is IConversionOperation conv) argVal = conv.Operand;
            receiverType = argVal.Type;
        }

        return receiverType.IsIQueryable();
    }

    private bool IsDbSet(ITypeSymbol type)
    {
        // Check if type is DbSet<T> or inherits from it.
        // Name check "DbSet" and namespace "Microsoft.EntityFrameworkCore"
        // But checking base types is safer.
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbSet" && 
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private bool IsMaterializer(string name)
    {
        // List of methods that execute the query
        return name == "ToList" || name == "ToListAsync" ||
               name == "ToArray" || name == "ToArrayAsync" ||
               name == "ToDictionary" || name == "ToDictionaryAsync" ||
               name == "ToHashSet" || name == "ToHashSetAsync" ||
               name == "First" || name == "FirstOrDefault" ||
               name == "FirstAsync" || name == "FirstOrDefaultAsync" ||
               name == "Single" || name == "SingleOrDefault" ||
               name == "SingleAsync" || name == "SingleOrDefaultAsync" ||
               name == "Last" || name == "LastOrDefault" ||
               name == "LastAsync" || name == "LastOrDefaultAsync" ||
               name == "Count" || name == "LongCount" ||
               name == "CountAsync" || name == "LongCountAsync" ||
               name == "Any" || name == "All" ||
               name == "AnyAsync" || name == "AllAsync" ||
               name == "Sum" || name == "Average" || name == "Min" || name == "Max" ||
               name == "SumAsync" || name == "AverageAsync" || name == "MinAsync" || name == "MaxAsync";
    }

    private bool IsInsideLoop(IOperation operation)
    {
        return operation.IsInsideLoop() || operation.IsInsideAsyncForEach();
    }
}
