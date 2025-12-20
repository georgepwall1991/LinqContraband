using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC029_RedundantIdentitySelect;

/// <summary>
/// Analyzes IQueryable operations to detect redundant Select(x => x) calls. Diagnostic ID: LC029
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantIdentitySelectAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC029";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Redundant identity Select";

    private static readonly LocalizableString MessageFormat =
        "The call to 'Select' is redundant because it uses an identity projection (x => x)";

    private static readonly LocalizableString Description =
        "Select(x => x) returns the object itself and is redundant. Removing it simplifies the query and improves readability.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC029_RedundantIdentitySelect.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name != "Select") return;

        // Check if receiver is IQueryable or IEnumerable
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null || (!receiver.Type.IsIQueryable() && !IsEnumerable(receiver.Type))) return;

        // Check if there is a predicate
        if (invocation.Arguments.Length < (method.IsExtensionMethod ? 2 : 1)) return;
        
        var predicateArg = method.IsExtensionMethod ? invocation.Arguments[1] : invocation.Arguments[0];
        var lambda = predicateArg.Value.UnwrapConversions() as IAnonymousFunctionOperation;
        if (lambda == null) return;

        if (IsIdentityLambda(lambda))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
        }
    }

    private bool IsIdentityLambda(IAnonymousFunctionOperation lambda)
    {
        var body = lambda.Body.Operations.FirstOrDefault();
        if (body is IReturnOperation returnOp) body = returnOp.ReturnedValue;
        if (body == null) return false;

        body = body.UnwrapConversions();

        if (body is IParameterReferenceOperation paramRef)
        {
            return SymbolEqualityComparer.Default.Equals(paramRef.Parameter, lambda.Symbol.Parameters.FirstOrDefault());
        }

        return false;
    }

    private bool IsEnumerable(ITypeSymbol? type)
    {
        if (type == null) return false;
        if (type.Name == "IEnumerable" && type.ContainingNamespace?.ToString() == "System.Collections.Generic") return true;
        return type.AllInterfaces.Any(i => i.Name == "IEnumerable" && i.ContainingNamespace?.ToString() == "System.Collections.Generic");
    }
}
