using System.Collections.Immutable;
using System.Linq;
using System.Text;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSqlStringConstructionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC037";
    private const string Category = "Security";
    private static readonly LocalizableString Title = "Avoid constructed raw SQL strings";

    private static readonly LocalizableString MessageFormat =
        "The SQL passed to '{0}' is built from string construction and should be parameterized instead";

    private static readonly LocalizableString Description =
        "Raw SQL APIs should receive constant SQL text plus parameters. String.Format, String.Concat, StringBuilder, and aliased string construction all hide injection risk.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC037_RawSqlStringConstruction.md");

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "FromSqlRaw",
        "ExecuteSqlRaw",
        "ExecuteSqlRawAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!TargetMethods.Contains(method.Name) ||
            method.ContainingNamespace?.ToString().StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) != true)
        {
            return;
        }

        var sqlArgument = GetSqlArgument(invocation, method);
        if (sqlArgument == null)
            return;

        if (!IsConstructedRawSql(sqlArgument.Value, invocation.FindOwningExecutableRoot()))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.Value.Syntax.GetLocation(), method.Name));
    }

    private static IArgumentOperation? GetSqlArgument(IInvocationOperation invocation, IMethodSymbol method)
    {
        var sqlParameterIndex = method.Parameters.ToList().FindIndex(parameter => parameter.Name == "sql");
        if (sqlParameterIndex < 0 || sqlParameterIndex >= invocation.Arguments.Length)
            return null;

        return invocation.Arguments[sqlParameterIndex];
    }

    private static bool IsConstructedRawSql(IOperation operation, IOperation? executableRoot)
    {
        var current = operation.UnwrapConversions();

        if (current.ConstantValue.HasValue)
            return false;

        if (current is IInterpolatedStringOperation)
            return true;

        if (current is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Add)
            return IsConcatWithNonConstant(binary, executableRoot);

        if (current is IInvocationOperation invocation)
            return IsSuspiciousInvocation(invocation, executableRoot);

        if (current is ILocalReferenceOperation localReference)
            return TryResolveLocalValue(localReference.Local, executableRoot, out var resolvedValue) &&
                   IsConstructedRawSql(resolvedValue, executableRoot);

        return false;
    }

    private static bool IsConcatWithNonConstant(IBinaryOperation binary, IOperation? executableRoot)
    {
        return IsNonConstant(binary.LeftOperand, executableRoot) || IsNonConstant(binary.RightOperand, executableRoot);
    }

    private static bool IsNonConstant(IOperation operation, IOperation? executableRoot)
    {
        var current = operation.UnwrapConversions();
        if (current.ConstantValue.HasValue)
            return false;

        return current switch
        {
            IInterpolatedStringOperation => true,
            IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add => IsConcatWithNonConstant(binary, executableRoot),
            IInvocationOperation invocation => IsSuspiciousInvocation(invocation, executableRoot),
            ILocalReferenceOperation localReference => TryResolveLocalValue(localReference.Local, executableRoot, out var resolvedValue) &&
                                                       IsConstructedRawSql(resolvedValue, executableRoot),
            IFieldReferenceOperation => true,
            IPropertyReferenceOperation => true,
            _ => true
        };
    }

    private static bool IsSuspiciousInvocation(IInvocationOperation invocation, IOperation? executableRoot)
    {
        var method = invocation.TargetMethod;

        if (method.Name == "Format" &&
            method.ContainingType.Name == "String" &&
            method.ContainingNamespace?.ToString() == "System")
        {
            return invocation.Arguments.Any(arg => !arg.Value.UnwrapConversions().ConstantValue.HasValue);
        }

        if (method.Name == "Concat" &&
            method.ContainingType.Name == "String" &&
            method.ContainingNamespace?.ToString() == "System")
        {
            return invocation.Arguments.Any(arg => IsNonConstant(arg.Value, executableRoot));
        }

        if (method.Name == "ToString" &&
            invocation.GetInvocationReceiver()?.Type is INamedTypeSymbol receiverType &&
            receiverType.Name == "StringBuilder" &&
            receiverType.ContainingNamespace?.ToString() == "System.Text")
        {
            return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot);
        }

        return false;
    }

    private static bool ContainsSuspiciousStringBuilderAppend(IOperation? receiver, IOperation? executableRoot)
    {
        if (receiver == null)
            return false;

        var current = receiver.UnwrapConversions();

        if (current is IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.ContainingType.Name == nameof(StringBuilder) &&
                invocation.TargetMethod.ContainingNamespace?.ToString() == "System.Text" &&
                invocation.TargetMethod.Name.StartsWith("Append", System.StringComparison.Ordinal))
            {
                if (invocation.Arguments.Any(arg => IsNonConstant(arg.Value, executableRoot)))
                    return true;

                return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot);
            }

            return ContainsSuspiciousStringBuilderAppend(invocation.GetInvocationReceiver(), executableRoot);
        }

        if (current is ILocalReferenceOperation localReference)
        {
            return TryResolveLocalValue(localReference.Local, executableRoot, out var resolvedValue) &&
                   ContainsSuspiciousStringBuilderAppend(resolvedValue, executableRoot);
        }

        return false;
    }

    private static bool TryResolveLocalValue(ILocalSymbol local, IOperation? executableRoot, out IOperation value)
    {
        value = null!;

        if (executableRoot == null)
            return false;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) ||
                        declarator.Initializer == null)
                    {
                        continue;
                    }

                    value = declarator.Initializer.Value;
                    return true;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
            {
                value = assignment.Value;
                return true;
            }
        }

        return false;
    }
}
