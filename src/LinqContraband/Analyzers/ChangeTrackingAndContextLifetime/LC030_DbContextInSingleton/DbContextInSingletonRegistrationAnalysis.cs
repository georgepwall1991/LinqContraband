using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!IsDependencyInjectionMethod(method))
        {
            return;
        }

        switch (method.Name)
        {
            case "AddHostedService":
                AddGenericTypeEvidence(method, longLivedTypes, "registered with AddHostedService<T>()");
                break;

            case "AddSingleton":
                AnalyzeAddSingleton(context, invocation, longLivedTypes);
                break;

            case "AddDbContext":
                AnalyzeAddDbContext(context, invocation);
                break;
        }
    }

    private static void AnalyzeAddSingleton(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var seenTypes = new HashSet<INamedTypeSymbol>(NamedTypeSymbolComparer.Instance);
        foreach (var type in GetRegisteredTypes(invocation))
        {
            if (!seenTypes.Add(type))
            {
                continue;
            }

            if (type.IsDbContext())
            {
                ReportRegistrationDiagnostic(context, type, GetRegistrationName(invocation), "DbContext is registered as Singleton");
                continue;
            }

            longLivedTypes.AddOrUpdate(
                type,
                "registered with AddSingleton(...)",
                static (_, _) => "registered with AddSingleton(...)");
        }
    }

    private static void AnalyzeAddDbContext(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        if (!HasSingletonContextLifetimeArgument(invocation))
        {
            return;
        }

        foreach (var typeArgument in invocation.TargetMethod.TypeArguments)
        {
            if (typeArgument is INamedTypeSymbol namedType && namedType.IsDbContext())
            {
                ReportRegistrationDiagnostic(context, namedType, GetRegistrationName(invocation), "DbContext lifetime is configured as Singleton");
                return;
            }
        }
    }

    private static void AddGenericTypeEvidence(
        IMethodSymbol method,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes,
        string reason)
    {
        foreach (var typeArgument in method.TypeArguments)
        {
            if (typeArgument is INamedTypeSymbol namedType)
            {
                longLivedTypes.AddOrUpdate(
                    namedType,
                    reason,
                    static (_, currentReason) => currentReason);
            }
        }
    }

    private static bool IsDependencyInjectionMethod(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        return definition.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" &&
               definition.Parameters.Length > 0 &&
               IsServiceCollection(definition.Parameters[0].Type);
    }

    private static bool IsServiceCollection(ITypeSymbol? type)
    {
        return type?.Name == "IServiceCollection" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static void ReportRegistrationDiagnostic(
        OperationAnalysisContext context,
        INamedTypeSymbol dbContextType,
        string registrationName,
        string reason)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            context.Operation.Syntax.GetLocation(),
            "registration",
            registrationName,
            dbContextType.Name,
            reason));
    }

    private static string GetRegistrationName(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.TypeArguments.Length == 0)
        {
            return invocation.TargetMethod.Name;
        }

        return invocation.TargetMethod.Name + "<" +
               string.Join(", ", invocation.TargetMethod.TypeArguments.Select(GetDisplayName)) +
               ">";
    }
}
