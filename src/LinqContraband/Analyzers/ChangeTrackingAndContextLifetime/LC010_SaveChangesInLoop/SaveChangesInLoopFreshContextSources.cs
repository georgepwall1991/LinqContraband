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
        // UnwrapConversions also steps through await, so this is the awaited call for an async initializer.
        var value = UnwrapConfigureAwait(initializer.UnwrapConversions());

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

    /// <summary>
    /// <c>await task.ConfigureAwait(...)</c> awaits the same task, so the call that created the task is what counts.
    /// </summary>
    private static IOperation UnwrapConfigureAwait(IOperation value)
    {
        while (value is IInvocationOperation { TargetMethod.Name: "ConfigureAwait", Instance: { } instance } invocation &&
               invocation.TargetMethod.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
        {
            value = instance.UnwrapConversions();
        }

        return value;
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

        return returnType.IsDbContext() && IsDbContextFactoryMember(method);
    }

    /// <summary>
    /// The <c>IDbContextFactory&lt;T&gt;</c> member itself, or the method a class uses to implement it. Another
    /// method of the same name on an implementing class (one that hands back a cached context while the interface
    /// member is implemented explicitly) is not the factory API.
    /// </summary>
    private static bool IsDbContextFactoryMember(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type == null)
            return false;

        if (IsDbContextFactoryInterface(type))
            return true;

        foreach (var factoryInterface in type.AllInterfaces.Where(IsDbContextFactoryInterface))
        {
            foreach (var member in factoryInterface.GetMembers(method.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), method))
                    return true;
            }
        }

        return false;
    }

    private static bool IsDbContextFactoryInterface(INamedTypeSymbol type)
    {
        return type.Name == "IDbContextFactory" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    /// <summary>
    /// A non-overridable source method whose entire body is <c>new T(...)</c> (expression-bodied or a single
    /// <c>return</c>) of a DbContext type creates a new context per call. Anything else, such as returning a field
    /// or a wrapper converted to a context, stays unproven.
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

        if (returned == null ||
            invocation.SemanticModel?.Compilation is not { } compilation ||
            !compilation.TryGetOwnedSemanticModel(returned.SyntaxTree, out var model))
        {
            return false;
        }

        // C# binds parentheses to the operation inside them, so strip them from the syntax first.
        while (returned is ParenthesizedExpressionSyntax parenthesized)
            returned = parenthesized.Expression;

        // The object created must itself be a DbContext: a user-defined conversion from another type can hand back
        // any context, including a cached one.
        var created = model.GetOperation(returned);
        while (created is IConversionOperation { Conversion.IsUserDefined: false } conversion)
            created = conversion.Operand;

        return created is IObjectCreationOperation { Type: { } createdType } && createdType.IsDbContext();
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
               IsDependencyInjectionScopeApi(scopeCreation.TargetMethod) &&
               scopeCreation.TargetMethod.ReturnType is { Name: "IServiceScope" or "AsyncServiceScope" } scopeType &&
               scopeType.ContainingNamespace?.ToDisplayString() == DependencyInjectionNamespace &&
               !IsLocalWrittenBeforeSaveExecution(loop.Body, saveOperation, executionOperation, scopeLocal);
    }

    /// <summary>
    /// <c>IServiceScopeFactory.CreateScope</c>/<c>CreateAsyncScope</c> or the
    /// <c>Microsoft.Extensions.DependencyInjection</c> extensions over it. A project method of the same name that
    /// returns an <c>IServiceScope</c> may hand back a cached scope.
    /// </summary>
    private static bool IsDependencyInjectionScopeApi(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return original.ContainingType?.ContainingNamespace?.ToDisplayString() == DependencyInjectionNamespace;
    }
}
