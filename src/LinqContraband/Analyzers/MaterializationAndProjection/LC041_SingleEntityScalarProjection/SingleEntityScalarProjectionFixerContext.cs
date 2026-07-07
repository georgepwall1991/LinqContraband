using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

public sealed partial class SingleEntityScalarProjectionFixer
{
    private static bool IsSafeFixMaterializer(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var methodName = memberAccess.Name.Identifier.Text;
        return methodName is "First" or "FirstAsync" or "Single" or "SingleAsync";
    }

    private static bool HasUnsupportedPredicateArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return true;

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter?.Type is { } parameterType &&
                IsPredicateType(parameterType) &&
                !IsInlineLambdaArgument(argument.Syntax))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInlineLambdaArgument(SyntaxNode syntax)
    {
        var expression = syntax is ArgumentSyntax argumentSyntax
            ? argumentSyntax.Expression
            : syntax;

        return expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax;
    }

    private static bool IsPredicateType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
            return false;

        if (namedType.Name == "Expression" &&
            namedType.TypeArguments.Length == 1)
        {
            return IsPredicateType(namedType.TypeArguments[0]);
        }

        return namedType.Name == "Func" &&
               namedType.DelegateInvokeMethod?.ReturnType.SpecialType == SpecialType.System_Boolean;
    }

    private static bool TryGetFixContext(InvocationExpressionSyntax invocation, SemanticModel semanticModel, out FixContext fixContext)
    {
        fixContext = null!;

        var declarator = invocation.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        if (declarator == null)
            return false;

        if (declarator.Parent is not VariableDeclarationSyntax declaration ||
            !declaration.Type.IsVar)
        {
            return false;
        }

        if (semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol assignedLocal)
            return false;

        var operation = semanticModel.GetOperation(invocation);
        if (operation == null)
            return false;

        var executableRoot = operation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return false;

        if (!SingleEntityScalarProjectionAnalyzer.TryAnalyzeLocalUsage(executableRoot, assignedLocal, out var property))
            return false;

        if (SingleEntityScalarProjectionAnalyzer.HasNullConditionalPropertyUsage(executableRoot, assignedLocal, property))
            return false;

        fixContext = new FixContext(assignedLocal, property.Name, true);
        return true;
    }

    private sealed class FixContext
    {
        public FixContext(ILocalSymbol local, string propertyName, bool isVarDeclaration)
        {
            Local = local;
            PropertyName = propertyName;
            IsVarDeclaration = isVarDeclaration;
        }

        public ILocalSymbol Local { get; }

        public string PropertyName { get; }

        public bool IsVarDeclaration { get; }
    }
}
