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
        if (RunsOnce(compile))
            return false;

        if (IsInsideCacheFactory(compile, semanticModel, out var lazyCreation))
            return lazyCreation != null && LazyIsBuiltPerCall(lazyCreation);

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
                return InstanceStoreCompilesOnEveryCall(assignment, field, semanticModel);

            case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                return InstanceStoreCompilesOnEveryCall(assignment, property, semanticModel);

            default:
                // Static members, indexers (dictionary caches), parameters and anything else.
                return false;
        }
    }

    /// <summary>
    /// A store to a field or property of <c>this</c> from an ordinary method or accessor, not guarded by an
    /// <c>if</c> whose condition implies the member is null in the branch that holds the store. Constructors and
    /// <c>init</c> accessors are a non-goal: whether the instance lives long enough to reuse the delegate is not known.
    /// </summary>
    private static bool InstanceStoreCompilesOnEveryCall(
        AssignmentExpressionSyntax assignment,
        ISymbol memberSymbol,
        SemanticModel semanticModel)
    {
        var target = assignment.Left;
        if (target is MemberAccessExpressionSyntax access && access.Expression is not ThisExpressionSyntax)
            return false;
        if (target is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return false;

        var member = GetEnclosingMember(assignment);
        if (member is not (MethodDeclarationSyntax or AccessorDeclarationSyntax { RawKind: (int)SyntaxKind.GetAccessorDeclaration or (int)SyntaxKind.SetAccessorDeclaration }))
            return false;

        SyntaxNode previous = assignment;
        foreach (var ancestor in assignment.Ancestors())
        {
            if (ancestor is MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                break;

            if (ancestor is IfStatementSyntax ifStatement)
            {
                // `if (_q == null) _q = ...` or `if (_q != null) ... else _q = ...`.
                var inElse = ifStatement.Else != null && previous == ifStatement.Else;
                if (ImpliesNull(ifStatement.Condition, !inElse, memberSymbol, semanticModel))
                    return false;
            }

            previous = ancestor;
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="condition"/> evaluating to <paramref name="whenTrue"/> guarantees that
    /// <paramref name="member"/> is null. <c>A &amp;&amp; B</c> being true needs only one side to imply it, and being
    /// false needs both (<c>!A || !B</c>); <c>A || B</c> is the other way round. Members are matched by symbol, so a
    /// local of the same name does not count.
    /// </summary>
    private static bool ImpliesNull(ExpressionSyntax condition, bool whenTrue, ISymbol member, SemanticModel semanticModel)
    {
        switch (condition)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ImpliesNull(parenthesized.Expression, whenTrue, member, semanticModel);

            case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression):
                return ImpliesNull(unary.Operand, !whenTrue, member, semanticModel);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression):
            {
                var left = ImpliesNull(binary.Left, whenTrue, member, semanticModel);
                var right = ImpliesNull(binary.Right, whenTrue, member, semanticModel);
                var eitherSuffices = binary.IsKind(SyntaxKind.LogicalAndExpression) == whenTrue;
                return eitherSuffices ? left || right : left && right;
            }

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
            {
                var matches = IsMember(binary.Left, member, semanticModel) && IsNull(binary.Right) ||
                              IsMember(binary.Right, member, semanticModel) && IsNull(binary.Left);
                return matches && binary.IsKind(SyntaxKind.EqualsExpression) == whenTrue;
            }

            case IsPatternExpressionSyntax isPattern when IsMember(isPattern.Expression, member, semanticModel):
                return PatternTestsNull(isPattern.Pattern) is { } isNull && isNull == whenTrue;

            default:
                return false;
        }
    }

    /// <summary>True for <c>null</c>, false for <c>not null</c> and <c>{ }</c>, null for any other pattern.</summary>
    private static bool? PatternTestsNull(PatternSyntax pattern)
    {
        switch (pattern)
        {
            case ConstantPatternSyntax constant when IsNull(constant.Expression):
                return true;
            case UnaryPatternSyntax unary when unary.IsKind(SyntaxKind.NotPattern):
                return PatternTestsNull(unary.Pattern) is { } inner ? !inner : null;
            case RecursivePatternSyntax { Type: null, PositionalPatternClause: null, Designation: null } recursive
                when recursive.PropertyPatternClause is { Subpatterns.Count: 0 }:
                return false;
            case ParenthesizedPatternSyntax parenthesized:
                return PatternTestsNull(parenthesized.Pattern);
            default:
                return null;
        }
    }

    private static bool IsMember(ExpressionSyntax expression, ISymbol member, SemanticModel semanticModel)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return IsMember(parenthesized.Expression, member, semanticModel);
            case IdentifierNameSyntax:
            case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }:
                return SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(expression).Symbol, member);
            default:
                return false;
        }
    }

    private static bool IsNull(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression) ||
               expression is LiteralExpressionSyntax defaultLiteral && defaultLiteral.IsKind(SyntaxKind.DefaultLiteralExpression);
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

            // A call from a static initializer or static constructor compiles once.
            if (!RunsOnce(call))
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
    /// For a Lazy, <paramref name="lazyCreation"/> is the <c>new Lazy</c> expression, whose own lifetime decides.
    /// </summary>
    private static bool IsInsideCacheFactory(
        SyntaxNode node,
        SemanticModel semanticModel,
        out BaseObjectCreationExpressionSyntax? lazyCreation)
    {
        lazyCreation = null;
        var boundary = node.Ancestors()
            .FirstOrDefault(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
        if (boundary is not AnonymousFunctionExpressionSyntax lambda ||
            lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: { } owner } argumentList } argument)
        {
            return false;
        }

        switch (owner)
        {
            case InvocationExpressionSyntax call:
                return CacheFactoryMethods.Contains(GetInvokedName(call.Expression)) &&
                       IsAddFactory(argument, argumentList, semanticModel);

            case BaseObjectCreationExpressionSyntax creation
                when semanticModel.GetTypeInfo(creation).Type is INamedTypeSymbol { Name: "Lazy" or "AsyncLazy" } &&
                     IsAddFactory(argument, argumentList, semanticModel):
                lazyCreation = creation;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// True when a <c>Lazy&lt;T&gt;</c> is built and read on every call instead of kept: its <c>.Value</c> read
    /// straight away, or a local whose every use reads <c>.Value</c>. A Lazy stored in a field or property, passed
    /// on, returned or built in run-once code (a static initializer or constructor) stays quiet.
    /// </summary>
    private static bool LazyIsBuiltPerCall(BaseObjectCreationExpressionSyntax creation)
    {
        if (RunsOnce(creation))
            return false;

        var value = ClimbValue(creation);
        switch (value.Parent)
        {
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" } access when access.Expression == value:
                return true;

            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
                when declarator.Parent?.Parent is LocalDeclarationStatementSyntax:
                return IsOnlyReadForValue(declarator.Identifier.ValueText, declarator);

            default:
                return false;
        }
    }

    private static bool IsOnlyReadForValue(string name, SyntaxNode declaration)
    {
        var scope = GetEnclosingMember(declaration);
        if (scope == null)
            return false;

        var read = false;
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != name)
                continue;

            if (IsMemberName(identifier) ||
                identifier.Parent is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" } access ||
                access.Expression != identifier)
            {
                return false;
            }

            read = true;
        }

        return read;
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
