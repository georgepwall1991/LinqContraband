using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

public sealed partial class SaveChangesInLoopAnalyzer
{
    private const string DependencyInjectionNamespace = "Microsoft.Extensions.DependencyInjection";

    /// <summary>
    /// Recognizes the per-iteration context sources that hand out a new DbContext on every call:
    /// <c>new T()</c>, <c>IDbContextFactory&lt;T&gt;.CreateDbContext[Async]()</c>, a same-project helper whose whole
    /// body is <c>new T(...)</c>, and <c>scope.ServiceProvider.GetRequiredService&lt;T&gt;()</c> on a scope created in
    /// the same loop body.
    /// </summary>
    private static bool IsFreshContextCreation(
        IOperation initializer,
        ILoopOperation loop,
        IOperation saveOperation,
        IOperation executionOperation)
    {
        var value = initializer.UnwrapConversions();
        if (value is IAwaitOperation awaitOperation)
            value = awaitOperation.Operation.UnwrapConversions();

        return value switch
        {
            IObjectCreationOperation objectCreation => objectCreation.Type?.IsDbContext() == true,
            IInvocationOperation invocation =>
                IsDbContextFactoryCreateCall(invocation) ||
                IsSameProjectFreshContextHelperCall(invocation) ||
                IsServiceResolvedFromFreshLoopScope(invocation, loop, saveOperation, executionOperation),
            _ => false
        };
    }

    private static bool IsDbContextFactoryCreateCall(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.Name is not ("CreateDbContext" or "CreateDbContextAsync"))
            return false;

        var returnType = method.ReturnType;
        if (method.Name == "CreateDbContextAsync")
        {
            if (returnType is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } taskType ||
                taskType.Name is not ("Task" or "ValueTask"))
            {
                return false;
            }

            returnType = taskType.TypeArguments[0];
        }

        return returnType.IsDbContext() && IsDbContextFactoryType(method.ContainingType);
    }

    private static bool IsDbContextFactoryType(INamedTypeSymbol? type)
    {
        return type != null &&
               (IsDbContextFactoryInterface(type) || type.AllInterfaces.Any(IsDbContextFactoryInterface));
    }

    private static bool IsDbContextFactoryInterface(INamedTypeSymbol type)
    {
        return type.Name == "IDbContextFactory" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    /// <summary>
    /// A non-overridable source method whose entire body is <c>new T(...)</c> (expression-bodied or a single
    /// <c>return</c>) creates a new context per call. Anything else, such as returning a field, stays unproven.
    /// </summary>
    private static bool IsSameProjectFreshContextHelperCall(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!method.ReturnType.IsDbContext() ||
            method.IsVirtual || method.IsAbstract || method.IsOverride || method.IsExtern ||
            method.MethodKind != MethodKind.Ordinary ||
            method.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        if (method.DeclaringSyntaxReferences[0].GetSyntax() is not MethodDeclarationSyntax declaration)
            return false;

        var returned = declaration.ExpressionBody?.Expression;
        if (returned == null &&
            declaration.Body?.Statements is { Count: 1 } statements &&
            statements[0] is ReturnStatementSyntax returnStatement)
        {
            returned = returnStatement.Expression;
        }

        return returned is BaseObjectCreationExpressionSyntax;
    }

    private static bool IsServiceResolvedFromFreshLoopScope(
        IInvocationOperation invocation,
        ILoopOperation loop,
        IOperation saveOperation,
        IOperation executionOperation)
    {
        var method = invocation.TargetMethod;
        if (method.Name is not ("GetRequiredService" or "GetService") ||
            !method.IsGenericMethod ||
            method.ContainingNamespace?.ToDisplayString() != DependencyInjectionNamespace ||
            !method.ReturnType.IsDbContext())
        {
            return false;
        }

        var provider = method.IsExtensionMethod && invocation.Arguments.Length > 0
            ? invocation.Arguments[0].Value.UnwrapConversions()
            : invocation.Instance?.UnwrapConversions();

        if (provider is not IPropertyReferenceOperation { Property.Name: "ServiceProvider" } providerProperty ||
            providerProperty.Instance?.UnwrapConversions() is not ILocalReferenceOperation scopeReference)
        {
            return false;
        }

        var scopeLocal = scopeReference.Local;
        var scopeDeclaration = loop.Body
            .Descendants()
            .OfType<IVariableDeclaratorOperation>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, scopeLocal));

        return scopeDeclaration?.Initializer?.Value.UnwrapConversions() is IInvocationOperation scopeCreation &&
               scopeCreation.TargetMethod.Name is "CreateScope" or "CreateAsyncScope" &&
               scopeCreation.TargetMethod.ReturnType is { Name: "IServiceScope" or "AsyncServiceScope" } scopeType &&
               scopeType.ContainingNamespace?.ToDisplayString() == DependencyInjectionNamespace &&
               !IsLocalWrittenBeforeSaveExecution(loop.Body, saveOperation, executionOperation, scopeLocal);
    }
}
