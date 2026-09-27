using System.Collections.Immutable;
using System.Linq;
using System;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC061_UncachedCompiledQuery;

/// <summary>
/// Analyzes <c>EF.CompileQuery</c> and <c>EF.CompileAsyncQuery</c> calls whose delegate is built again on every call
/// instead of being stored once. Diagnostic ID: LC061
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> a compiled query only pays off when the delegate is created once and reused, which
/// is why the EF Core documentation stores it in a <c>static readonly</c> field. Compiling inside a method builds and
/// compiles the expression tree on every call, which costs more than an ordinary LINQ query (that at least hits EF
/// Core's query cache).</para>
/// <para>The analyzer is conservative: it reports only when the delegate is invoked straight away, kept in a local
/// that is only invoked, returned from an expression-bodied property or getter, returned from a private factory whose
/// every caller invokes the result straight away, or stored on <c>this</c> from an ordinary method without a null
/// guard. Static fields and properties (including values compiled and invoked in their initializers), instance field
/// and property initializers that store the delegate, constructors, <c>??=</c>, lazy and
/// dictionary caches, factory lambdas and delegates passed elsewhere stay quiet.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UncachedCompiledQueryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC061";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Compiled query is not cached";

    private static readonly LocalizableString MessageFormat =
        "EF.{0} builds and compiles the query again on every call; store the delegate in a static readonly field and reuse it";

    private static readonly LocalizableString Description =
        "EF.CompileQuery and EF.CompileAsyncQuery only save work when the delegate they return is created once and reused. Compiling on every call costs more than an ordinary LINQ query, which at least hits EF Core's query cache. Store the compiled delegate in a static readonly field.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC061_UncachedCompiledQuery.html");

    private static readonly ImmutableHashSet<string> CacheFactoryMethods = ImmutableHashSet.Create(
        "GetOrAdd", "AddOrUpdate", "GetOrCreate", "GetOrCreateAsync", "EnsureInitialized");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsCompileQuery(invocation.TargetMethod) ||
            invocation.Syntax is not InvocationExpressionSyntax syntax ||
            invocation.SemanticModel is not { } semanticModel)
        {
            return;
        }

        if (!CompilesOnEveryCall(syntax, semanticModel))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, syntax.Expression.GetLocation(), invocation.TargetMethod.Name));
    }

    internal static bool IsCompileQuery(IMethodSymbol method)
    {
        return method.Name is "CompileQuery" or "CompileAsyncQuery" &&
               method.ContainingType is { Name: "EF", IsStatic: true } type &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool CompilesOnEveryCall(InvocationExpressionSyntax compile, SemanticModel semanticModel)
    {
        if (RunsOnce(compile) || IsInsideCacheFactory(compile, semanticModel))
            return false;

        var value = ClimbValue(compile);
        switch (value.Parent)
        {
            case InvocationExpressionSyntax or MemberAccessExpressionSyntax when IsInvoked(value):
                return true;

            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
                when declarator.Parent?.Parent is LocalDeclarationStatementSyntax:
                return IsOnlyInvoked(declarator.Identifier.ValueText, declarator, null);

            case AssignmentExpressionSyntax assignment when assignment.Right == value:
                return AssignmentCompilesOnEveryCall(assignment, semanticModel);

            case ArrowExpressionClauseSyntax arrow:
                return ReturnCompilesOnEveryCall(arrow, semanticModel);

            case ReturnStatementSyntax returnStatement:
                return ReturnCompilesOnEveryCall(returnStatement, semanticModel);

            default:
                return false;
        }
    }

    /// <summary>
    /// Walks up from <paramref name="expression"/> through parentheses, casts, <c>!</c>, <c>?:</c> branches and
    /// <c>??</c> to the expression whose value is the compiled delegate.
    /// </summary>
    private static ExpressionSyntax ClimbValue(ExpressionSyntax expression)
    {
        var current = expression;
        while (true)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized;
                    continue;
                case CastExpressionSyntax cast when cast.Expression == current:
                    current = cast;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    current = postfix;
                    continue;
                case ConditionalExpressionSyntax conditional when conditional.Condition != current:
                    current = conditional;
                    continue;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression):
                    current = binary;
                    continue;
                default:
                    return current;
            }
        }
    }

    /// <summary>True when the delegate <paramref name="value"/> is called right away: <c>value(...)</c> or <c>value.Invoke(...)</c>.</summary>
    private static bool IsInvoked(ExpressionSyntax value)
    {
        return value.Parent switch
        {
            InvocationExpressionSyntax call => call.Expression == value,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } access =>
                access.Expression == value && access.Parent is InvocationExpressionSyntax call && call.Expression == access,
            _ => false
        };
    }

    /// <summary>
    /// True when every use of the local named <paramref name="name"/> in its enclosing member invokes it, and there
    /// is at least one. The match is by name, so any other use of the same name (a store, a return, an argument, a
    /// reassignment) counts as the delegate escaping.
    /// </summary>
    private static bool IsOnlyInvoked(string name, SyntaxNode declaration, SyntaxNode? excluded)
    {
        var scope = GetEnclosingMember(declaration);
        if (scope == null)
            return false;

        var invoked = false;
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != name || identifier == excluded)
                continue;

            if (IsMemberName(identifier) || !IsInvoked(identifier))
                return false;

            invoked = true;
        }

        return invoked;
    }

    private static bool AssignmentCompilesOnEveryCall(AssignmentExpressionSyntax assignment, SemanticModel semanticModel)
    {
        // `_query ??= EF.CompileQuery(...)` is a lazy cache.
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return false;

        switch (semanticModel.GetSymbolInfo(assignment.Left).Symbol)
        {
            case ILocalSymbol local:
                return IsOnlyInvoked(local.Name, assignment, assignment.Left);

            case IFieldSymbol { IsStatic: false } field:
                return InstanceStoreCompilesOnEveryCall(assignment, field.Name);

            case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                return InstanceStoreCompilesOnEveryCall(assignment, property.Name);

            default:
                // Static members, indexers (dictionary caches), parameters and anything else.
                return false;
        }
    }

    /// <summary>
    /// A store to a field or property of <c>this</c> from an ordinary method or accessor, not guarded by an
    /// <c>if</c> that tests the member. Constructors and <c>init</c> accessors are a non-goal: whether the instance
    /// lives long enough to reuse the delegate is not known.
    /// </summary>
    private static bool InstanceStoreCompilesOnEveryCall(AssignmentExpressionSyntax assignment, string memberName)
    {
        var target = assignment.Left;
        if (target is MemberAccessExpressionSyntax access && access.Expression is not ThisExpressionSyntax)
            return false;
        if (target is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return false;

        var member = GetEnclosingMember(assignment);
        if (member is not (MethodDeclarationSyntax or AccessorDeclarationSyntax { RawKind: (int)SyntaxKind.GetAccessorDeclaration or (int)SyntaxKind.SetAccessorDeclaration }))
            return false;

        foreach (var ifStatement in assignment.Ancestors().OfType<IfStatementSyntax>())
        {
            if (ifStatement.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                .Any(identifier => identifier.Identifier.ValueText == memberName))
                return false;
        }

        return true;
    }

    /// <summary>
    /// A compiled delegate returned to the caller. Lambdas are factories whose caller decides the lifetime, so they
    /// stay quiet. Getters and expression-bodied properties compile on every read. Methods and local functions report
    /// only when they are private (or local) and every reference to them invokes the returned delegate straight away.
    /// </summary>
    private static bool ReturnCompilesOnEveryCall(SyntaxNode returnNode, SemanticModel semanticModel)
    {
        foreach (var ancestor in returnNode.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax:
                    return false;

                case LocalFunctionStatementSyntax localFunction:
                    return semanticModel.GetDeclaredSymbol(localFunction) is IMethodSymbol localSymbol &&
                           IsFactoryOnlyInvoked(localSymbol, GetEnclosingMember(localFunction), semanticModel);

                case AccessorDeclarationSyntax accessor:
                    return accessor.IsKind(SyntaxKind.GetAccessorDeclaration);

                case PropertyDeclarationSyntax or IndexerDeclarationSyntax:
                    return true;

                case MethodDeclarationSyntax method:
                    return semanticModel.GetDeclaredSymbol(method) is IMethodSymbol symbol &&
                           PrivateFactoryIsOnlyInvoked(symbol, semanticModel);

                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool PrivateFactoryIsOnlyInvoked(IMethodSymbol method, SemanticModel semanticModel)
    {
        if (method.DeclaredAccessibility != Accessibility.Private || method.ContainingType == null)
            return false;

        var referenced = false;
        foreach (var reference in method.ContainingType.DeclaringSyntaxReferences)
        {
            var tree = reference.SyntaxTree;
            SemanticModel? model = tree == semanticModel.SyntaxTree
                ? semanticModel
                : semanticModel.Compilation.TryGetOwnedSemanticModel(tree, out var owned) ? owned : null;
            if (model == null ||
                !TryCheckFactoryReferences(method, reference.GetSyntax(), model, ref referenced))
            {
                return false;
            }
        }

        return referenced;
    }

    private static bool IsFactoryOnlyInvoked(IMethodSymbol localFunction, SyntaxNode? scope, SemanticModel semanticModel)
    {
        if (scope == null)
            return false;

        var referenced = false;
        return TryCheckFactoryReferences(localFunction, scope, semanticModel, ref referenced) && referenced;
    }

    /// <summary>
    /// False when a reference to <paramref name="factory"/> under <paramref name="scope"/> does anything other than
    /// call it and invoke the result straight away: <c>Build()(...)</c> or <c>Build().Invoke(...)</c>. A reference
    /// stored in a field, passed on or used as a method group keeps the rule quiet. References are matched by
    /// symbol, so a call to another overload of the same name is ignored; a same-named reference that does not
    /// bind keeps the rule quiet.
    /// </summary>
    private static bool TryCheckFactoryReferences(
        IMethodSymbol factory,
        SyntaxNode scope,
        SemanticModel semanticModel,
        ref bool referenced)
    {
        foreach (var node in scope.DescendantNodes())
        {
            if (node is not SimpleNameSyntax simpleName || simpleName.Identifier.ValueText != factory.Name)
                continue;

            var symbolInfo = semanticModel.GetSymbolInfo(simpleName);
            if (symbolInfo.Symbol is { } bound)
            {
                if (!SymbolEqualityComparer.Default.Equals(bound.OriginalDefinition, factory.OriginalDefinition))
                    continue;
            }
            else if (symbolInfo.CandidateSymbols.IsEmpty ||
                     symbolInfo.CandidateSymbols.Any(candidate =>
                         SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, factory.OriginalDefinition)))
            {
                return false;
            }
            else
            {
                continue;
            }

            ExpressionSyntax reference = simpleName;
            if (simpleName.Parent is MemberAccessExpressionSyntax access && access.Name == simpleName)
                reference = access;
            else if (IsMemberName(simpleName))
                return false;

            if (reference.Parent is not InvocationExpressionSyntax call || call.Expression != reference)
                return false;

            if (!IsInvoked(ClimbValue(call)))
                return false;

            referenced = true;
        }

        return true;
    }

    private static bool IsMemberName(SimpleNameSyntax name)
    {
        return name.Parent is MemberAccessExpressionSyntax access && access.Name == name ||
               name.Parent is MemberBindingExpressionSyntax;
    }

    /// <summary>
    /// True for code that runs once per program or type: a static constructor, a static field or static
    /// auto-property initializer, or top-level statements, outside any lambda or local function. Instance field
    /// initializers run once per instance, so they are not included.
    /// </summary>
    private static bool RunsOnce(SyntaxNode node)
    {
        SyntaxNode? previous = null;
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax:
                    return false;
                case ConstructorDeclarationSyntax constructor:
                    return constructor.Modifiers.Any(SyntaxKind.StaticKeyword);
                case BaseFieldDeclarationSyntax field:
                    return field.Modifiers.Any(SyntaxKind.StaticKeyword);
                case PropertyDeclarationSyntax property:
                    // Only the `= ...` initializer runs once; getters and `=>` bodies run on every read.
                    return property.Initializer != null && previous == property.Initializer &&
                           property.Modifiers.Any(SyntaxKind.StaticKeyword);
                case GlobalStatementSyntax:
                    return true;
                case MemberDeclarationSyntax:
                    return false;
            }

            previous = ancestor;
        }

        return false;
    }

    /// <summary>
    /// True when the nearest enclosing lambda is the one handed to a cache that runs it once (per key):
    /// <c>GetOrAdd</c>, the add-value factory of <c>AddOrUpdate</c> (its update factory runs every time the key
    /// exists), <c>GetOrCreate</c>, <c>LazyInitializer.EnsureInitialized</c> or a <c>Lazy&lt;T&gt;</c> constructor. A delegate nested inside the factory (<c>_ =&gt; (c, id) =&gt; ...</c>) is
    /// what gets cached, and it compiles again on every call, so the search stops at the first function boundary.
    /// </summary>
    private static bool IsInsideCacheFactory(SyntaxNode node, SemanticModel semanticModel)
    {
        var boundary = node.Ancestors()
            .FirstOrDefault(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
        if (boundary is not AnonymousFunctionExpressionSyntax lambda ||
            lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: { } owner } argumentList } argument)
        {
            return false;
        }

        return owner switch
        {
            InvocationExpressionSyntax call =>
                CacheFactoryMethods.Contains(GetInvokedName(call.Expression)) &&
                IsAddFactory(argument, argumentList, semanticModel),
            BaseObjectCreationExpressionSyntax creation =>
                semanticModel.GetTypeInfo(creation).Type is INamedTypeSymbol { Name: "Lazy" or "AsyncLazy" } &&
                IsAddFactory(argument, argumentList, semanticModel),
            _ => false
        };
    }

    /// <summary>
    /// True when <paramref name="argument"/> binds to the factory the cache runs once to add a value
    /// (<c>valueFactory</c>, <c>addValueFactory</c>, <c>factory</c>). The <c>updateValueFactory</c> of
    /// <c>AddOrUpdate</c> runs on every call for an existing key, and a lambda passed as the value itself
    /// (<c>GetOrAdd(key, value)</c>, <c>AddOrUpdate(key, addValue, ...)</c>) is what gets cached, so neither counts.
    /// When the parameter does not resolve, only the second argument of <c>AddOrUpdate</c> counts, and any argument
    /// of the other caches.
    /// </summary>
    private static bool IsAddFactory(ArgumentSyntax argument, ArgumentListSyntax argumentList, SemanticModel semanticModel)
    {
        var name = (semanticModel.GetOperation(argument) as IArgumentOperation)?.Parameter?.Name ??
                   argument.NameColon?.Name.Identifier.ValueText;
        if (name != null)
        {
            return name.IndexOf("factory", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   name.IndexOf("update", StringComparison.OrdinalIgnoreCase) < 0;
        }

        return argumentList.Parent is not InvocationExpressionSyntax call ||
               GetInvokedName(call.Expression) != "AddOrUpdate" ||
               argumentList.Arguments.IndexOf(argument) == 1;
    }

    private static string GetInvokedName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            SimpleNameSyntax name => name.Identifier.ValueText,
            _ => string.Empty
        };
    }

    /// <summary>The method, constructor, accessor, property or other member declaration that holds <paramref name="node"/>.</summary>
    private static SyntaxNode? GetEnclosingMember(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is GlobalStatementSyntax)
                return ancestor.Parent;
            if (ancestor is AccessorDeclarationSyntax or BaseMethodDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax)
                return ancestor;
            if (ancestor is MemberDeclarationSyntax)
                return null;
        }

        return null;
    }
}
