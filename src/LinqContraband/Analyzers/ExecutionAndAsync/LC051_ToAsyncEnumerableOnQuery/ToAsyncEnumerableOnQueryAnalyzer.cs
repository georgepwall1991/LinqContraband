using System;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC051_ToAsyncEnumerableOnQuery;

/// <summary>
/// Reports <c>ToAsyncEnumerable()</c> on an EF Core query. The BCL's <c>System.Linq.AsyncEnumerable</c> (built into
/// .NET 10, and the System.Linq.Async package before it) treats the query as a plain <c>IEnumerable&lt;T&gt;</c>, so EF
/// Core runs it synchronously and blocks a thread for every row. <c>AsAsyncEnumerable()</c> streams it asynchronously.
/// EF Core 11 ships its own check (EF1004), so the rule stays quiet when EF Core 11 or later is referenced.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ToAsyncEnumerableOnQueryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC051";
    private const string Category = "Performance";

    private static readonly LocalizableString Title = "ToAsyncEnumerable() runs an EF Core query synchronously";

    private static readonly LocalizableString MessageFormat =
        "'ToAsyncEnumerable' enumerates this EF Core query synchronously and blocks a thread per row; use 'AsAsyncEnumerable' instead";

    private static readonly LocalizableString Description =
        "System.Linq.AsyncEnumerable.ToAsyncEnumerable() wraps the query as a plain IEnumerable<T>, so EF Core executes it with synchronous I/O. EF Core's AsAsyncEnumerable() streams the same results asynchronously.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC051_ToAsyncEnumerableOnQuery.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (ReferencesEfCore11OrLater(compilationContext.Compilation))
                return;

            compilationContext.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static bool ReferencesEfCore11OrLater(Compilation compilation)
    {
        return compilation.ReferencedAssemblyNames.Any(identity =>
            string.Equals(identity.Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal) &&
            identity.Version.Major >= 11);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "ToAsyncEnumerable" ||
            method.ContainingType is not { Name: "AsyncEnumerable" } containingType ||
            containingType.ContainingNamespace?.ToString() != "System.Linq" ||
            invocation.Arguments.Length != 1 ||
            !IsEnumerableOfT(method.Parameters[0].Type))
        {
            return;
        }

        var source = invocation.Arguments[0].Value.UnwrapConversions();
        if (!IsEfQuery(source))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, GetLocation(invocation)));
    }

    private static bool IsEnumerableOfT(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { Name: "IEnumerable", IsGenericType: true } named &&
               named.ContainingNamespace?.ToString() == "System.Collections.Generic";
    }

    /// <summary>
    /// True when the source is a <c>DbSet</c>, <c>DbContext.Set&lt;T&gt;()</c>, or a chain of LINQ and EF Core query
    /// operators over one. Anything else (a local, an in-memory <c>AsQueryable()</c>, a helper) stays quiet, because
    /// <c>AsAsyncEnumerable()</c> throws on a queryable that EF Core does not back.
    /// </summary>
    private static bool IsEfQuery(IOperation operation)
    {
        var current = operation;
        while (true)
        {
            current = current.UnwrapConversions();
            if (current is ITranslatedQueryOperation translatedQuery)
            {
                current = translatedQuery.Operation;
                continue;
            }

            if (current.Type.IsDbSet())
                return true;

            if (current is not IInvocationOperation invocation)
                return false;

            var method = invocation.TargetMethod;
            if (method.Name == "Set" && method.IsGenericMethod && method.ContainingType.IsDbContext())
                return true;

            if (!IsQueryOperator(method))
                return false;

            var receiver = invocation.GetInvocationReceiver();
            if (receiver == null)
                return false;

            current = receiver;
        }
    }

    private static bool IsQueryOperator(IMethodSymbol method)
    {
        if (!method.ReturnType.IsIQueryable())
            return false;

        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        if (containingType.Name == "Queryable" && containingType.ContainingNamespace?.ToString() == "System.Linq")
            return true;

        return containingType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private static Location GetLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
            return memberAccess.Name.GetLocation();

        return invocation.Syntax.GetLocation();
    }
}
