using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC036_DbContextCapturedAcrossThreads;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DbContextCapturedAcrossThreadsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC036";
    private const string Category = "Safety";
    private static readonly LocalizableString Title = "DbContext captured by thread work item";

    private static readonly LocalizableString MessageFormat =
        "DbContext symbol '{0}' is captured inside '{1}', which can run on a different thread";

    private static readonly LocalizableString Description =
        "DbContext instances are not thread-safe. Capturing them in Task.Run, Parallel.ForEach, or ThreadPool.QueueUserWorkItem can cause race conditions and invalid usage.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC036_DbContextCapturedAcrossThreads.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsTargetThreadApi(method))
            return;

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel == null)
            return;

        foreach (var argument in invocation.Arguments)
        {
            if (TryFindCapturedDbContext(argument.Value.Syntax, semanticModel, out var symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), symbol.Name, method.Name));
                return;
            }
        }
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (!IsTargetThreadObject(creation.Constructor?.ContainingType))
            return;

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel == null)
            return;

        foreach (var argument in creation.Arguments)
        {
            if (TryFindCapturedDbContext(argument.Value.Syntax, semanticModel, out var symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation(), symbol.Name, creation.Type?.Name ?? "thread work item"));
                return;
            }
        }
    }

    private static bool IsTargetThreadApi(IMethodSymbol method)
    {
        return (method.Name == "Run" &&
                method.ContainingType.Name == "Task" &&
                method.ContainingNamespace?.ToString() == "System.Threading.Tasks") ||
               (method.Name == "StartNew" &&
                method.ContainingType.Name == "TaskFactory" &&
                method.ContainingNamespace?.ToString() == "System.Threading.Tasks") ||
               (method.Name == "ForEach" &&
                method.ContainingType.Name == "Parallel" &&
                method.ContainingNamespace?.ToString() == "System.Threading.Tasks") ||
               (method.Name == "QueueUserWorkItem" &&
                method.ContainingType.Name == "ThreadPool" &&
                method.ContainingNamespace?.ToString() == "System.Threading");
    }

    private static bool IsTargetThreadObject(INamedTypeSymbol? type)
    {
        if (type == null)
            return false;

        var ns = type.ContainingNamespace?.ToString();
        return ns == "System.Threading" && type.Name is "Thread" or "Timer";
    }

    private static bool TryFindCapturedDbContext(SyntaxNode syntax, SemanticModel semanticModel, out ISymbol capturedSymbol)
    {
        var lambdaSyntax = syntax.AncestorsAndSelf().FirstOrDefault(node =>
            node is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax);

        if (lambdaSyntax is null)
        {
            return TryFindCapturedDbContextInLocalFunctionCallback(syntax, semanticModel, out capturedSymbol);
        }

        var body = lambdaSyntax switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Body,
            SimpleLambdaExpressionSyntax simple => simple.Body,
            AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
            _ => null
        };

        if (body is null)
        {
            capturedSymbol = null!;
            return false;
        }

        foreach (var descendant in body.DescendantNodesAndSelf())
        {
            if (descendant is IdentifierNameSyntax or GenericNameSyntax or SimpleNameSyntax or MemberAccessExpressionSyntax)
            {
                var symbol = semanticModel.GetSymbolInfo(descendant).Symbol;
                if (symbol is null)
                {
                    continue;
                }

                if (IsCapturedDbContext(symbol, lambdaSyntax.Span))
                {
                    capturedSymbol = symbol;
                    return true;
                }
            }
        }

        capturedSymbol = null!;
        return false;
    }

    private static bool TryFindCapturedDbContextInLocalFunctionCallback(
        SyntaxNode syntax,
        SemanticModel semanticModel,
        out ISymbol capturedSymbol)
    {
        if (semanticModel.GetSymbolInfo(syntax).Symbol is not IMethodSymbol
            {
                MethodKind: MethodKind.LocalFunction
            } localFunction)
        {
            capturedSymbol = null!;
            return false;
        }

        var syntaxReference = localFunction.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxReference?.GetSyntax() is not LocalFunctionStatementSyntax localFunctionSyntax)
        {
            capturedSymbol = null!;
            return false;
        }

        SyntaxNode? body = localFunctionSyntax.Body ?? (SyntaxNode?)localFunctionSyntax.ExpressionBody?.Expression;
        if (body is null)
        {
            capturedSymbol = null!;
            return false;
        }

        foreach (var descendant in body.DescendantNodesAndSelf())
        {
            if (descendant is IdentifierNameSyntax or GenericNameSyntax or SimpleNameSyntax or MemberAccessExpressionSyntax)
            {
                var symbol = semanticModel.GetSymbolInfo(descendant).Symbol;
                if (symbol is null)
                    continue;

                if (IsCapturedDbContext(symbol, localFunctionSyntax.Span))
                {
                    capturedSymbol = symbol;
                    return true;
                }
            }
        }

        capturedSymbol = null!;
        return false;
    }

    private static bool IsCapturedDbContext(ISymbol symbol, TextSpan lambdaSpan)
    {
        if (symbol is not ILocalSymbol and not IParameterSymbol and not IFieldSymbol and not IPropertySymbol)
            return false;

        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        if (type == null || !type.IsDbContext())
            return false;

        if (symbol.DeclaringSyntaxReferences.Length == 0)
            return true;

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (lambdaSpan.Contains(syntax.Span))
                return false;
        }

        return true;
    }
}
