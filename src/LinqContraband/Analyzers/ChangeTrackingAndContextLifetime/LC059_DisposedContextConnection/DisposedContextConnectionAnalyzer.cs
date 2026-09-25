using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC059_DisposedContextConnection;

/// <summary>
/// Analyzes code that disposes the connection returned by <c>DatabaseFacade.GetDbConnection()</c>. Diagnostic ID: LC059
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> the <c>DbContext</c> owns that connection. Disposing it (a <c>using</c>, an
/// <c>await using</c>, or an explicit <c>Dispose</c>/<c>DisposeAsync</c>) resets it underneath EF Core: providers such
/// as SqlClient clear the connection string, so later queries and <c>SaveChanges</c> on the same context, or on the
/// next lease of a pooled context, fail. Close the connection if the code opened it, and let the context dispose it.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisposedContextConnectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC059";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Connection owned by the DbContext is disposed";

    private static readonly LocalizableString MessageFormat =
        "The connection returned by GetDbConnection() belongs to the DbContext; disposing it breaks later operations on the context";

    private static readonly LocalizableString Description =
        "DatabaseFacade.GetDbConnection() returns the connection the DbContext uses and disposes itself. Disposing it with using, await using, Dispose or DisposeAsync breaks later queries and SaveChanges on the context and on pooled contexts. Close it if you opened it, and leave disposal to the context.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC059_DisposedContextConnection.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeUsing, OperationKind.Using);
        context.RegisterOperationAction(AnalyzeUsingDeclaration, OperationKind.UsingDeclaration);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeUsing(OperationAnalysisContext context)
    {
        var resources = ((IUsingOperation)context.Operation).Resources;
        if (resources is IVariableDeclarationGroupOperation group)
        {
            ReportDeclarators(context, group);
            return;
        }

        if (IsContextConnection(resources))
            context.ReportDiagnostic(Diagnostic.Create(Rule, resources.Syntax.GetLocation()));
    }

    private static void AnalyzeUsingDeclaration(OperationAnalysisContext context)
    {
        ReportDeclarators(context, ((IUsingDeclarationOperation)context.Operation).DeclarationGroup);
    }

    private static void ReportDeclarators(OperationAnalysisContext context, IVariableDeclarationGroupOperation group)
    {
        foreach (var declaration in group.Declarations)
        {
            foreach (var declarator in declaration.Declarators)
            {
                var value = declarator.Initializer?.Value ?? declaration.Initializer?.Value;
                if (value != null && IsContextConnection(value))
                    context.ReportDiagnostic(Diagnostic.Create(Rule, value.Syntax.GetLocation()));
            }
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.IsStatic ||
            method.Parameters.Length != 0 ||
            method.Name is not ("Dispose" or "DisposeAsync") ||
            invocation.Instance == null)
        {
            return;
        }

        if (IsContextConnection(invocation.Instance))
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
    }

    /// <summary>
    /// True for a <c>GetDbConnection()</c> call, possibly cast, or a local whose only assignment is one.
    /// </summary>
    private static bool IsContextConnection(IOperation operation)
    {
        var current = operation.UnwrapConversions();
        if (current is ILocalReferenceOperation localReference)
        {
            var root = (IOperation)localReference;
            while (root.Parent != null)
                root = root.Parent;

            var assignments = LocalAssignmentCache.GetAssignments(root, localReference.Local);
            if (assignments.Count != 1 || localReference.Local.RefKind != RefKind.None)
                return false;

            current = assignments[0].Value.UnwrapConversions();
        }

        return current is IInvocationOperation invocation && IsGetDbConnection(invocation.TargetMethod);
    }

    private static bool IsGetDbConnection(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return original.Name == "GetDbConnection" &&
               original.ContainingType?.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }
}
