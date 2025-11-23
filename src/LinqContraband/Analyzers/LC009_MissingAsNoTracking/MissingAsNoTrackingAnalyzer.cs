using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingAsNoTrackingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC009";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Performance: Missing AsNoTracking() in Read-Only path";

    private static readonly LocalizableString MessageFormat =
        "Method '{0}' appears to be read-only but returns tracked entities. Use AsNoTracking() to avoid tracking overhead.";

    private static readonly LocalizableString Description =
        "When querying entities for read-only operations, use .AsNoTracking() to prevent EF Core from creating unnecessary change tracking snapshots. This reduces memory usage and CPU time.";

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

        // 1. Must be a materializer (ToList, etc.) or Find
        if (!IsMaterializerOrFind(method)) return;

        // 2. Analyze the query chain
        var analysis = AnalyzeQueryChain(invocation);
        if (!analysis.IsEfQuery) return;
        if (analysis.HasAsNoTracking || analysis.HasAsTracking) return;
        if (analysis.HasSelect) return; // Projections don't track

        // 3. Analyze the containing method for writes (SaveChanges)
        if (HasWriteOperations(context.Operation)) return;

        // If we got here: It's an EF query returning entities, no tracking mod, no writes in method.
        var containingMethodName = GetContainingMethodName(context.Operation);
        
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), containingMethodName));
    }

    private bool IsMaterializerOrFind(IMethodSymbol method)
    {
        // Quick check
        if (method.Name.StartsWith("Find")) return true;
        
        return method.Name == "ToList" || method.Name == "ToListAsync" ||
               method.Name == "ToArray" || method.Name == "ToArrayAsync" ||
               method.Name == "First" || method.Name == "FirstOrDefault" ||
               method.Name == "FirstAsync" || method.Name == "FirstOrDefaultAsync" ||
               method.Name == "Single" || method.Name == "SingleOrDefault" ||
               method.Name == "SingleAsync" || method.Name == "SingleOrDefaultAsync" ||
               method.Name == "Last" || method.Name == "LastOrDefault" ||
               method.Name == "LastAsync" || method.Name == "LastOrDefaultAsync";
    }

    private class ChainAnalysis
    {
        public bool IsEfQuery { get; set; }
        public bool HasAsNoTracking { get; set; }
        public bool HasAsTracking { get; set; }
        public bool HasSelect { get; set; }
    }

    private ChainAnalysis AnalyzeQueryChain(IInvocationOperation invocation)
    {
        var result = new ChainAnalysis();
        var current = invocation.Instance ?? 
                      (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

        while (current != null)
        {
            // Unwrap implicit conversions
            while (current is IConversionOperation conversion) current = conversion.Operand;

            if (current is IInvocationOperation prevInvocation)
            {
                var method = prevInvocation.TargetMethod;
                var ns = method.ContainingNamespace?.ToString();

                if (method.Name == "AsNoTracking") result.HasAsNoTracking = true;
                if (method.Name == "AsTracking") result.HasAsTracking = true;
                if (method.Name == "Select") result.HasSelect = true; // Any projection invalidates need for AsNoTracking check

                // Move up
                current = prevInvocation.Instance ?? 
                          (prevInvocation.Arguments.Length > 0 ? prevInvocation.Arguments[0].Value : null);
            }
            else if (current is IPropertyReferenceOperation propRef)
            {
                // Check if it's a DbSet
                if (IsDbSet(propRef.Type))
                {
                    result.IsEfQuery = true;
                }
                // End of chain
                break;
            }
            else if (current is IFieldReferenceOperation fieldRef)
            {
                 if (IsDbSet(fieldRef.Type)) result.IsEfQuery = true;
                 break;
            }
            else if (current is IParameterReferenceOperation paramRef)
            {
                // If it's a parameter of type IQueryable, assume it's EF-like
                if (IsDbSet(paramRef.Type) || paramRef.Type.IsIQueryable()) 
                {
                    result.IsEfQuery = true;
                }
                break;
            }
            else
            {
                // Probably a local variable or parameter.
                // If variable type is IQueryable, we assume it's EF related for now if we haven't proven otherwise?
                // Safe to assume if we are in this analyzer context and already matched method names.
                // BUT better to be strict.
                if (IsDbSet(current.Type)) result.IsEfQuery = true;
                
                // Often we assign db.Users to a var.
                // var users = db.Users;
                // users.ToList();
                // 'current' is ILocalReferenceOperation. We'd need to trace variable assignment. 
                // That's complex. For now, let's stick to fluent chains or direct access.
                
                break;
            }
        }

        return result;
    }

    private bool IsDbSet(ITypeSymbol? type)
    {
        if (type == null) return false;
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbSet" && 
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                return true;
            if (current.Name == "IQueryable" && // Allow IQueryable interfaces too? No, too broad.
                current.ContainingNamespace?.ToString() == "System.Linq")
            {
                // Maybe?
            }
            current = current.BaseType;
        }
        // Interface check
        foreach (var iface in type.AllInterfaces)
        {
             if (iface.Name == "IQueryable" && iface.ContainingNamespace?.ToString() == "System.Linq")
             {
                 // If it's IQueryable, it's potentially EF. 
                 // But we want to be sure it's EF to avoid flagging Lists.
                 // The method names (ToListAsync, AsNoTracking) usually give it away.
                 // But we are checking the Source here.
             }
        }
        
        return false;
    }

    private bool IsDbContext(ITypeSymbol? type)
    {
        if (type == null) return false;
        var current = type;
        while (current != null)
        {
            if (current.Name == "DbContext" && 
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private bool HasWriteOperations(IOperation operation)
    {
        // Find method body
        IOperation? root = operation;
        while (root.Parent != null)
        {
            if (root is IMethodBodyOperation || 
                root is ILocalFunctionOperation || 
                root is IAnonymousFunctionOperation)
            {
                break;
            }
            root = root.Parent;
        }

        if (root == null) return false;

        // Walk the body to find SaveChanges
        var hasWrite = false;
        foreach (var descendant in root.Descendants())
        {
            if (descendant is IInvocationOperation inv)
            {
                if (inv.TargetMethod.Name == "SaveChanges" || 
                    inv.TargetMethod.Name == "SaveChangesAsync")
                {
                    hasWrite = true;
                    break;
                }
                
                // Also check for Add, Update, Remove on DbSet?
                // Ideally yes, but SaveChanges is the commit point. 
                // If they Add but don't SaveChanges in this method, tracking is still technically needed?
                // Yes, because the Context tracks it.
                // So if they call Add(), we should assume tracking is needed.
                
                var name = inv.TargetMethod.Name;
                var receiverType = inv.Instance?.Type ?? (inv.Arguments.Length > 0 ? inv.Arguments[0].Value.Type : null);
                
                if ((name == "Add" || name == "AddAsync" || 
                     name == "Update" || name == "Remove" || name == "RemoveRange" || name == "AddRange" || name == "AddRangeAsync") &&
                    (IsDbSet(receiverType) || IsDbContext(receiverType)))
                {
                    hasWrite = true; 
                    break;
                }
            }
        }

        return hasWrite;
    }

    private string GetContainingMethodName(IOperation operation)
    {
        var sym = operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart);
        return sym?.Name ?? "Unknown";
    }
}
