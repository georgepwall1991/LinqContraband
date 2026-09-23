using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC054_MigrateInsideTransaction;

/// <summary>A transaction started on the migrating context that is still open when Migrate runs.</summary>
internal sealed class OpenTransaction
{
    public OpenTransaction(IOperation start, IInvocationOperation begin, ILocalSymbol? local)
    {
        Start = start;
        Begin = begin;
        Local = local;
    }

    /// <summary>The statement in the enclosing block that begins the transaction, or the using statement that owns it.</summary>
    public IOperation Start { get; }

    public IInvocationOperation Begin { get; }

    /// <summary>The local that holds the transaction, when there is one.</summary>
    public ILocalSymbol? Local { get; }
}

/// <summary>
/// Shared by the LC054 analyzer and fixer: finds a transaction begun on the same context, in the same method body,
/// before the Migrate call and not yet committed, rolled back, disposed, or handed to other code.
/// </summary>
internal static class MigrateInsideTransactionScope
{
    public const string RelationalEventIdTypeName = "Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId";
    public const string WarningName = "MigrationsUserTransactionWarning";

    private const string EfNamespace = "Microsoft.EntityFrameworkCore";
    private const string FacadeExtensions = "RelationalDatabaseFacadeExtensions";

    public static bool TryGetMigrateFacade(IInvocationOperation invocation, out IOperation facade)
    {
        facade = null!;
        var method = invocation.TargetMethod;
        if (method.Name is not ("Migrate" or "MigrateAsync") ||
            method.ContainingType?.Name != FacadeExtensions ||
            method.ContainingType.ContainingNamespace?.ToDisplayString() != EfNamespace)
        {
            return false;
        }

        return TryGetExtensionReceiver(invocation, out facade);
    }

    /// <summary>
    /// Returns the symbol that identifies the context behind <c>ctx.Database</c>: a local, parameter, field,
    /// auto-property, or the containing type for <c>this</c>. Anything else is not provably one context.
    /// </summary>
    public static ISymbol? GetContextRoot(IOperation facade)
    {
        if (facade.UnwrapConversions() is not IPropertyReferenceOperation { Property.Name: "Database" } database ||
            database.Instance == null ||
            !database.Instance.Type.IsDbContext())
        {
            return null;
        }

        return GetStableSymbol(database.Instance);
    }

    public static OpenTransaction? FindOpenTransaction(IInvocationOperation migrate, ISymbol contextRoot)
    {
        IOperation child = migrate;
        var parent = migrate.Parent;
        ILoopOperation? outermostLoop = null;

        while (parent != null)
        {
            // A transaction outside a lambda or local function may be gone by the time it runs.
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation or IMethodBodyBaseOperation)
                return null;

            if (parent is ILoopOperation loop)
                outermostLoop = loop;

            if (parent is IBlockOperation block)
            {
                var index = block.Operations.IndexOf(child);
                for (var i = index - 1; i >= 0; i--)
                {
                    var statement = block.Operations[i];
                    if (!TryMatchStatementStart(statement, contextRoot, out var begin, out var local))
                        continue;

                    return IsStillOpen(block, statement.Syntax.Span.End, migrate, outermostLoop, contextRoot, local)
                        ? new OpenTransaction(statement, begin, local)
                        : null;
                }
            }
            else if (parent is IUsingOperation usingOperation &&
                     ReferenceEquals(child, usingOperation.Body) &&
                     TryMatchResource(usingOperation.Resources, contextRoot, out var begin, out var local))
            {
                return IsStillOpen(usingOperation.Body, usingOperation.Resources.Syntax.Span.End, migrate, outermostLoop, contextRoot, local)
                    ? new OpenTransaction(usingOperation, begin, local)
                    : null;
            }

            child = parent;
            parent = parent.Parent;
        }

        return null;
    }

    /// <summary>
    /// True when the project downgrades the EF Core warning, for example
    /// <c>ConfigureWarnings(w =&gt; w.Ignore(RelationalEventId.MigrationsUserTransactionWarning))</c>.
    /// </summary>
    public static bool IsWarningDowngraded(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tree.GetText(cancellationToken).ToString().IndexOf(WarningName, System.StringComparison.Ordinal) < 0 ||
                !compilation.TryGetOwnedSemanticModel(tree, out var semanticModel))
            {
                continue;
            }

            foreach (var name in tree.GetRoot(cancellationToken).DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (name.Identifier.ValueText != WarningName ||
                    semanticModel.GetSymbolInfo(name, cancellationToken).Symbol is not IFieldSymbol field ||
                    field.ContainingType?.ToDisplayString() != RelationalEventIdTypeName)
                {
                    continue;
                }

                var argument = name.FirstAncestorOrSelf<ArgumentSyntax>();
                if (argument?.Parent?.Parent is InvocationExpressionSyntax invocation &&
                    semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol { Name: "Ignore" or "Log" })
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsCommitOf(IOperation statement, OpenTransaction transaction, ISymbol contextRoot)
    {
        if (statement is not IExpressionStatementOperation expressionStatement ||
            expressionStatement.Operation.UnwrapConversions() is not IInvocationOperation invocation)
        {
            return false;
        }

        if (transaction.Local != null)
        {
            return invocation.TargetMethod.Name is "Commit" or "CommitAsync" &&
                   invocation.Instance?.UnwrapConversions() is ILocalReferenceOperation reference &&
                   SymbolEqualityComparer.Default.Equals(reference.Local, transaction.Local);
        }

        return invocation.TargetMethod.Name is "CommitTransaction" or "CommitTransactionAsync" &&
               invocation.Instance != null &&
               SymbolEqualityComparer.Default.Equals(GetContextRoot(invocation.Instance), contextRoot);
    }

    private static bool TryMatchStatementStart(
        IOperation statement,
        ISymbol contextRoot,
        out IInvocationOperation begin,
        out ILocalSymbol? local)
    {
        switch (statement)
        {
            case IUsingDeclarationOperation usingDeclaration:
                return TryMatchDeclarations(usingDeclaration.DeclarationGroup, contextRoot, out begin, out local);
            case IVariableDeclarationGroupOperation declarationGroup:
                return TryMatchDeclarations(declarationGroup, contextRoot, out begin, out local);
            case IExpressionStatementOperation { Operation: ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment }
                when TryGetBeginTransaction(assignment.Value, contextRoot, out begin):
                local = target.Local;
                return true;
            case IExpressionStatementOperation expressionStatement
                when TryGetBeginTransaction(expressionStatement.Operation, contextRoot, out begin):
                local = null;
                return true;
            default:
                begin = null!;
                local = null;
                return false;
        }
    }

    private static bool TryMatchResource(
        IOperation resources,
        ISymbol contextRoot,
        out IInvocationOperation begin,
        out ILocalSymbol? local)
    {
        if (resources is IVariableDeclarationGroupOperation declarationGroup)
            return TryMatchDeclarations(declarationGroup, contextRoot, out begin, out local);

        local = null;
        return TryGetBeginTransaction(resources, contextRoot, out begin);
    }

    private static bool TryMatchDeclarations(
        IVariableDeclarationGroupOperation declarationGroup,
        ISymbol contextRoot,
        out IInvocationOperation begin,
        out ILocalSymbol? local)
    {
        foreach (var declaration in declarationGroup.Declarations)
        {
            foreach (var declarator in declaration.Declarators)
            {
                var initializer = declarator.Initializer ?? declaration.Initializer;
                if (initializer != null && TryGetBeginTransaction(initializer.Value, contextRoot, out begin))
                {
                    local = declarator.Symbol;
                    return true;
                }
            }
        }

        begin = null!;
        local = null;
        return false;
    }

    private static bool TryGetBeginTransaction(IOperation operation, ISymbol contextRoot, out IInvocationOperation begin)
    {
        begin = null!;
        if (operation.UnwrapConversions() is not IInvocationOperation invocation ||
            invocation.TargetMethod.Name is not ("BeginTransaction" or "BeginTransactionAsync"))
        {
            return false;
        }

        var containingType = invocation.TargetMethod.ContainingType;
        IOperation? facade;
        if (containingType?.Name == "DatabaseFacade" &&
            containingType.ContainingNamespace?.ToDisplayString() == EfNamespace + ".Infrastructure")
        {
            facade = invocation.Instance;
        }
        else if (containingType?.Name == FacadeExtensions &&
                 containingType.ContainingNamespace?.ToDisplayString() == EfNamespace &&
                 TryGetExtensionReceiver(invocation, out var receiver))
        {
            facade = receiver;
        }
        else
        {
            return false;
        }

        if (facade == null || !SymbolEqualityComparer.Default.Equals(GetContextRoot(facade), contextRoot))
            return false;

        begin = invocation;
        return true;
    }

    /// <summary>
    /// Scans the code that runs after the transaction starts and before Migrate (the whole loop body when Migrate is
    /// in a loop inside the scope) for anything that could end the transaction or swap the context.
    /// </summary>
    private static bool IsStillOpen(
        IOperation scope,
        int scanStart,
        IInvocationOperation migrate,
        ILoopOperation? loop,
        ISymbol contextRoot,
        ILocalSymbol? local)
    {
        var scanEnd = migrate.Syntax.SpanStart;
        var loopSpan = loop?.Syntax.Span;

        foreach (var operation in scope.Descendants())
        {
            var position = operation.Syntax.SpanStart;
            var inRange = (position >= scanStart && position < scanEnd) ||
                          (loopSpan.HasValue && loopSpan.Value.Contains(position));
            if (!inRange || operation.IsImplicit)
                continue;

            switch (operation)
            {
                case ILocalReferenceOperation reference
                    when local != null && SymbolEqualityComparer.Default.Equals(reference.Local, local):
                    return false;
                case IInvocationOperation invocation
                    when invocation.TargetMethod.Name is "CommitTransaction" or "CommitTransactionAsync" or
                        "RollbackTransaction" or "RollbackTransactionAsync" or "UseTransaction" or "UseTransactionAsync":
                    return false;
                case IPropertyReferenceOperation { Property.Name: "CurrentTransaction" }:
                    return false;
                case IAssignmentOperation assignment
                    when SymbolEqualityComparer.Default.Equals(GetStableSymbol(assignment.Target), contextRoot):
                    return false;
            }
        }

        return true;
    }

    private static ISymbol? GetStableSymbol(IOperation operation)
    {
        switch (operation.UnwrapConversions())
        {
            case ILocalReferenceOperation local:
                return local.Local;
            case IParameterReferenceOperation parameter:
                return parameter.Parameter;
            case IFieldReferenceOperation field when field.Instance is null or IInstanceReferenceOperation:
                return field.Field;
            case IPropertyReferenceOperation property
                when property.Instance is null or IInstanceReferenceOperation &&
                     property.Arguments.Length == 0 &&
                     IsAutoProperty(property.Property):
                return property.Property;
            case IInstanceReferenceOperation instance:
                return instance.Type;
            default:
                return null;
        }
    }

    // A computed property can hand back a new context on every read, so only auto-properties count as one context.
    private static bool IsAutoProperty(IPropertySymbol property)
    {
        foreach (var member in property.ContainingType.GetMembers())
        {
            if (member is IFieldSymbol field && SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property))
                return true;
        }

        return false;
    }

    private static bool TryGetExtensionReceiver(IInvocationOperation invocation, out IOperation receiver)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
            {
                receiver = argument.Value;
                return true;
            }
        }

        receiver = null!;
        return false;
    }
}
