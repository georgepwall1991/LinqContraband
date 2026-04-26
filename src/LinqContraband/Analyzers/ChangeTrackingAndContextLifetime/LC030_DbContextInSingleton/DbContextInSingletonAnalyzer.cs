using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

/// <summary>
/// Analyzes class members to detect DbContext instances held in potentially singleton or long-lived services. Diagnostic ID: LC030
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DbContextInSingletonAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC030";
    private const string Category = "Architecture";
    private const string DetectionModeKey = "dotnet_code_quality.LC030.detection_mode";
    private const string LongLivedTypesKey = "dotnet_code_quality.LC030.long_lived_types";
    private static readonly LocalizableString Title = "Potential DbContext lifetime mismatch";

    private static readonly LocalizableString MessageFormat =
        "The {0} '{1}' uses a DbContext with a long-lived lifetime for '{2}' ({3}). Review the lifetime and prefer IDbContextFactory<TContext> or a scoped component.";

    private static readonly LocalizableString Description =
        "DbContext is not thread-safe and is intended to be short-lived. Storing it on a long-lived type can be risky, so review the service lifetime and prefer factories when the lifetime is uncertain.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC030_DbContextInSingleton.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var candidates = new ConcurrentBag<DbContextCandidate>();
            var longLivedTypes = new ConcurrentDictionary<INamedTypeSymbol, string>(NamedTypeSymbolComparer.Instance);

            compilationContext.RegisterSymbolAction(
                context => AnalyzeField(context, candidates, longLivedTypes),
                SymbolKind.Field);

            compilationContext.RegisterSymbolAction(
                context => AnalyzeProperty(context, candidates, longLivedTypes),
                SymbolKind.Property);

            compilationContext.RegisterSymbolAction(
                context => AnalyzeConstructor(context, candidates, longLivedTypes),
                SymbolKind.Method);

            compilationContext.RegisterOperationAction(
                context => AnalyzeInvocation(context, longLivedTypes),
                OperationKind.Invocation);

            compilationContext.RegisterCompilationEndAction(context =>
                ReportCandidateDiagnostics(context, candidates, longLivedTypes));
        });
    }

    private static void AnalyzeField(
        SymbolAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.IsStatic) return;

        if (field.Type.IsDbContext())
        {
            AddCandidate(context, candidates, longLivedTypes, field.ContainingType, field.Name, CandidateKind.Field, GetLocation(field));
        }
    }

    private static void AnalyzeProperty(
        SymbolAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property.IsStatic) return;

        if (property.Type.IsDbContext())
        {
            AddCandidate(context, candidates, longLivedTypes, property.ContainingType, property.Name, CandidateKind.Property, GetLocation(property));
        }
    }

    private static void AnalyzeConstructor(
        SymbolAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Constructor || method.IsStatic) return;

        foreach (var parameter in method.Parameters)
        {
            if (!parameter.Type.IsDbContext())
            {
                continue;
            }

            AddCandidate(
                context,
                candidates,
                longLivedTypes,
                method.ContainingType,
                parameter.Name,
                CandidateKind.ConstructorParameter,
                GetLocation(parameter));
        }
    }

    private static void AddCandidate(
        SymbolAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes,
        INamedTypeSymbol containingType,
        string name,
        CandidateKind kind,
        Location location)
    {
        if (containingType.TypeKind != TypeKind.Class) return;
        if (containingType.IsDbContext()) return;

        candidates.Add(new DbContextCandidate(containingType, name, kind, location));
        AddIntrinsicLongLivedEvidence(
            containingType,
            context.Compilation,
            context.Options.AnalyzerConfigOptionsProvider,
            location.SourceTree,
            longLivedTypes);
    }

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

    private static void ReportCandidateDiagnostics(
        CompilationAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var candidatesByType = new Dictionary<INamedTypeSymbol, List<DbContextCandidate>>(NamedTypeSymbolComparer.Instance);
        foreach (var candidate in candidates)
        {
            if (!candidatesByType.TryGetValue(candidate.ContainingType, out var typeCandidates))
            {
                typeCandidates = new List<DbContextCandidate>();
                candidatesByType.Add(candidate.ContainingType, typeCandidates);
            }

            typeCandidates.Add(candidate);
        }

        foreach (var pair in candidatesByType)
        {
            if (!longLivedTypes.TryGetValue(pair.Key, out var reason))
            {
                continue;
            }

            var storedCandidates = pair.Value
                .Where(candidate => candidate.Kind != CandidateKind.ConstructorParameter)
                .ToArray();
            var reportableCandidates = storedCandidates.Length > 0
                ? storedCandidates
                : pair.Value.Where(candidate => candidate.Kind == CandidateKind.ConstructorParameter).ToArray();

            foreach (var candidate in reportableCandidates)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    candidate.Location,
                    GetCandidateKindDisplayName(candidate.Kind),
                    candidate.Name,
                    candidate.ContainingType.Name,
                    reason));
            }
        }
    }

    private static void AddIntrinsicLongLivedEvidence(
        INamedTypeSymbol type,
        Compilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider,
        SyntaxTree? syntaxTree,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var options = GetOptions(optionsProvider, syntaxTree);
        if (TryGetConfiguredLongLivedReason(type, options, out var configuredReason))
        {
            longLivedTypes.TryAdd(type, configuredReason);
            return;
        }

        if (IsKnownScopedType(type, compilation))
        {
            return;
        }

        var hostedServiceType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.IHostedService");
        if (hostedServiceType != null && ImplementsInterface(type, hostedServiceType))
        {
            longLivedTypes.TryAdd(type, "implements IHostedService");
            return;
        }

        var backgroundServiceType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.BackgroundService");
        if (backgroundServiceType != null && InheritsFrom(type, backgroundServiceType))
        {
            longLivedTypes.TryAdd(type, "inherits BackgroundService");
            return;
        }

        if (HasConventionalMiddlewareSignature(type, compilation))
        {
            longLivedTypes.TryAdd(type, "has a conventional middleware Invoke/InvokeAsync signature");
            return;
        }

        if (options.ExpandedDetection && HasExpandedLongLivedName(type))
        {
            longLivedTypes.TryAdd(type, "matches expanded long-lived naming heuristics");
        }
    }

    private static Lc030Options GetOptions(AnalyzerConfigOptionsProvider provider, SyntaxTree? syntaxTree)
    {
        var expandedDetection = false;
        var longLivedTypes = new HashSet<string>(StringComparer.Ordinal);

        if (syntaxTree == null)
        {
            return new Lc030Options(expandedDetection, longLivedTypes);
        }

        var options = provider.GetOptions(syntaxTree);
        if (options.TryGetValue(DetectionModeKey, out var detectionMode) &&
            string.Equals(detectionMode.Trim(), "expanded", StringComparison.OrdinalIgnoreCase))
        {
            expandedDetection = true;
        }

        if (options.TryGetValue(LongLivedTypesKey, out var configuredTypes))
        {
            foreach (var configuredType in configuredTypes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = configuredType.Trim();
                if (trimmed.Length > 0)
                {
                    longLivedTypes.Add(trimmed);
                }
            }
        }

        return new Lc030Options(expandedDetection, longLivedTypes);
    }

    private static bool TryGetConfiguredLongLivedReason(INamedTypeSymbol type, Lc030Options options, out string reason)
    {
        reason = string.Empty;
        if (options.LongLivedTypes.Count == 0)
        {
            return false;
        }

        var current = type;
        while (current != null)
        {
            if (MatchesConfiguredName(current, options.LongLivedTypes))
            {
                reason = "matches configured long-lived type '" + GetDisplayName(current) + "'";
                return true;
            }

            current = current.BaseType;
        }

        foreach (var implementedInterface in type.AllInterfaces)
        {
            if (MatchesConfiguredName(implementedInterface, options.LongLivedTypes))
            {
                reason = "matches configured long-lived type '" + GetDisplayName(implementedInterface) + "'";
                return true;
            }
        }

        return false;
    }

    private static bool MatchesConfiguredName(INamedTypeSymbol type, HashSet<string> configuredNames)
    {
        return configuredNames.Contains(GetDisplayName(type)) ||
               configuredNames.Contains(GetDisplayName(type.OriginalDefinition));
    }

    private static bool HasExpandedLongLivedName(INamedTypeSymbol type)
    {
        return type.Name.IndexOf("Singleton", StringComparison.Ordinal) >= 0 ||
               type.Name.EndsWith("HostedService", StringComparison.Ordinal) ||
               type.Name.EndsWith("BackgroundWorker", StringComparison.Ordinal);
    }

    private static bool IsKnownScopedType(INamedTypeSymbol type, Compilation compilation)
    {
        var current = type;
        while (current != null)
        {
            var name = current.Name;

            // ASP.NET Core Controllers
            if (name.EndsWith("Controller", System.StringComparison.Ordinal) ||
                name.EndsWith("ViewComponent", System.StringComparison.Ordinal) ||
                name.EndsWith("PageModel", System.StringComparison.Ordinal))
                return true;

            current = current.BaseType;
        }

        var middlewareType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IMiddleware");
        if (middlewareType != null && ImplementsInterface(type, middlewareType))
        {
            return true;
        }

        return false;
    }

    private static bool HasConventionalMiddlewareSignature(INamedTypeSymbol type, Compilation compilation)
    {
        var httpContextType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.HttpContext");
        if (httpContextType == null)
        {
            return false;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (method.Name is not ("Invoke" or "InvokeAsync"))
            {
                continue;
            }

            if (method.IsStatic || method.Parameters.Length == 0)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, httpContextType))
            {
                return true;
            }
        }

        return false;
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

    private static IEnumerable<INamedTypeSymbol> GetRegisteredTypes(IInvocationOperation invocation)
    {
        var typeArguments = invocation.TargetMethod.TypeArguments
            .OfType<INamedTypeSymbol>()
            .ToArray();

        if (typeArguments.Length > 0)
        {
            yield return typeArguments[typeArguments.Length - 1];

            if (typeArguments.Length == 1 || typeArguments[0].IsDbContext())
            {
                yield break;
            }

            yield return typeArguments[0];
            yield break;
        }

        var typeOfArguments = invocation.Arguments
            .Select(argument => GetTypeOfOperand(argument.Value))
            .OfType<INamedTypeSymbol>()
            .ToArray();

        if (typeOfArguments.Length == 0)
        {
            yield break;
        }

        yield return typeOfArguments[typeOfArguments.Length - 1];

        if (typeOfArguments.Length > 1 && typeOfArguments[0].IsDbContext())
        {
            yield return typeOfArguments[0];
        }
    }

    private static ITypeSymbol? GetTypeOfOperand(IOperation operation)
    {
        operation = UnwrapConversion(operation);
        return operation is ITypeOfOperation typeOfOperation
            ? typeOfOperation.TypeOperand
            : null;
    }

    private static IOperation UnwrapConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool HasSingletonContextLifetimeArgument(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "contextLifetime" &&
                IsSingletonLifetime(argument.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSingletonLifetime(IOperation operation)
    {
        operation = UnwrapConversion(operation);
        if (operation is not IFieldReferenceOperation fieldReference)
        {
            return false;
        }

        return fieldReference.Field.Name == "Singleton" &&
               fieldReference.Field.ContainingType.Name == "ServiceLifetime" &&
               fieldReference.Field.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static bool IsDependencyInjectionMethod(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        return definition.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
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

    private static string GetDisplayName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static Location GetLocation(ISymbol symbol)
    {
        return symbol.Locations.FirstOrDefault(location => location.IsInSource) ??
               symbol.Locations.FirstOrDefault() ??
               Location.None;
    }

    private static string GetCandidateKindDisplayName(CandidateKind kind)
    {
        switch (kind)
        {
            case CandidateKind.Field:
                return "field";
            case CandidateKind.Property:
                return "property";
            case CandidateKind.ConstructorParameter:
                return "constructor parameter";
            default:
                return "member";
        }
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
    {
        foreach (var implementedInterface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implementedInterface.OriginalDefinition, interfaceType) ||
                SymbolEqualityComparer.Default.Equals(implementedInterface, interfaceType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType) ||
                SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private enum CandidateKind
    {
        Field,
        Property,
        ConstructorParameter
    }

    private sealed class DbContextCandidate
    {
        public DbContextCandidate(INamedTypeSymbol containingType, string name, CandidateKind kind, Location location)
        {
            ContainingType = containingType;
            Name = name;
            Kind = kind;
            Location = location;
        }

        public INamedTypeSymbol ContainingType { get; }
        public string Name { get; }
        public CandidateKind Kind { get; }
        public Location Location { get; }
    }

    private sealed class Lc030Options
    {
        public Lc030Options(bool expandedDetection, HashSet<string> longLivedTypes)
        {
            ExpandedDetection = expandedDetection;
            LongLivedTypes = longLivedTypes;
        }

        public bool ExpandedDetection { get; }
        public HashSet<string> LongLivedTypes { get; }
    }

    private sealed class NamedTypeSymbolComparer : IEqualityComparer<INamedTypeSymbol>
    {
        public static readonly NamedTypeSymbolComparer Instance = new();

        public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y)
        {
            return SymbolEqualityComparer.Default.Equals(x, y);
        }

        public int GetHashCode(INamedTypeSymbol obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj);
        }
    }
}
