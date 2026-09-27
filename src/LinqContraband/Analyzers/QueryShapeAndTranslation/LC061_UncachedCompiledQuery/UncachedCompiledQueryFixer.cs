using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC061_UncachedCompiledQuery;

/// <summary>
/// Provides code fixes for LC061. Moves the <c>EF.CompileQuery</c>/<c>EF.CompileAsyncQuery</c> call into a
/// <c>private static readonly</c> field declared just above the member, spelled with the delegate type the call
/// returns, and uses the field where the call was.
/// </summary>
/// <remarks>
/// No fix is offered when the query reads a local, a parameter of the method or the instance (a static field cannot
/// see them), when the call does not sit in a class, struct or record member, when the member's leading trivia holds
/// an <c>#if</c>, <c>#elif</c>, <c>#else</c> or <c>#endif</c> (the field could land in the wrong branch), or when the
/// rewritten document has more
/// compiler errors than before (for example a delegate type that uses a method type parameter).
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UncachedCompiledQueryFixer))]
[Shared]
public sealed class UncachedCompiledQueryFixer : CodeFixProvider
{
    private const string Title = "Store the compiled query in a static readonly field";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UncachedCompiledQueryAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        int? errorsBefore = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var compile = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (compile == null ||
                semanticModel.GetOperation(compile, cancellationToken) is not IInvocationOperation invocation ||
                !UncachedCompiledQueryAnalyzer.IsCompileQuery(invocation.TargetMethod))
            {
                continue;
            }

            var newRoot = Hoist(root, compile, invocation, semanticModel);
            if (newRoot == null) continue;

            var newDocument = document.WithSyntaxRoot(newRoot);
            errorsBefore ??= CountErrors(semanticModel, cancellationToken);
            var newModel = await newDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (newModel == null || CountErrors(newModel, cancellationToken) > errorsBefore.Value) continue;

            context.RegisterCodeFix(
                CodeAction.Create(Title, _ => Task.FromResult(newDocument), nameof(UncachedCompiledQueryFixer)),
                diagnostic);
        }
    }

    private static SyntaxNode? Hoist(
        SyntaxNode root,
        InvocationExpressionSyntax compile,
        IInvocationOperation invocation,
        SemanticModel semanticModel)
    {
        if (invocation.Type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType ||
            ContainsUnspellableType(delegateType) ||
            compile.ArgumentList.Arguments.Count != 1 ||
            compile.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda ||
            ReadsOuterState(lambda, semanticModel))
        {
            return null;
        }

        var member = compile.Ancestors().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(candidate => candidate.Parent is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax);
        if (member?.Parent is not TypeDeclarationSyntax type)
            return null;

        // A field inserted next to a member inside `#if`/`#else`/`#endif` could land in the wrong branch.
        if (member.GetLeadingTrivia().Any(IsConditionalDirective))
            return null;

        var name = ChooseName(type, member, compile, semanticModel);
        var typeName = delegateType.ToMinimalDisplayString(semanticModel, member.SpanStart, TypeFormat);
        if (SyntaxFactory.ParseMemberDeclaration(
                $"private static readonly {typeName} {name} = {compile.WithoutTrivia().ToFullString()};")
            is not FieldDeclarationSyntax field ||
            field.ContainsDiagnostics)
        {
            return null;
        }

        var endOfLine = member.GetDocumentEndOfLine();
        SplitLeadingTrivia(member.GetLeadingTrivia(), endOfLine, out var fieldLeading, out var memberLeading);
        field = field.WithLeadingTrivia(fieldLeading).WithTrailingTrivia(endOfLine);

        var newMember = member
            .ReplaceNode(compile, SyntaxFactory.IdentifierName(name).WithTriviaFrom(compile))
            .WithLeadingTrivia(memberLeading);
        var index = type.Members.IndexOf(member);
        var members = type.Members.Replace(member, newMember).Insert(index, field);
        return root.ReplaceNode(type, type.WithMembers(members));
    }

    private static bool IsConditionalDirective(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.IfDirectiveTrivia) ||
               trivia.IsKind(SyntaxKind.ElifDirectiveTrivia) ||
               trivia.IsKind(SyntaxKind.ElseDirectiveTrivia) ||
               trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia) ||
               trivia.IsKind(SyntaxKind.DisabledTextTrivia);
    }

    /// <summary>
    /// The field takes the member's leading blank lines, directives and indentation; comments (and documentation
    /// comments) stay on the member, which gets a blank line to separate it from the field.
    /// </summary>
    private static void SplitLeadingTrivia(
        SyntaxTriviaList leading,
        SyntaxTrivia endOfLine,
        out SyntaxTriviaList fieldLeading,
        out SyntaxTriviaList memberLeading)
    {
        var split = leading.Count;
        for (var i = 0; i < leading.Count; i++)
        {
            if (leading[i].IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                leading[i].IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                leading[i].IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                leading[i].IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                split = i;
                break;
            }
        }

        var before = leading.Take(split).ToList();
        var indentation = before.Count > 0 && before[before.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia)
            ? before[before.Count - 1]
            : default;

        fieldLeading = SyntaxFactory.TriviaList(before);
        var rest = new List<SyntaxTrivia> { endOfLine };
        if (indentation.IsKind(SyntaxKind.WhitespaceTrivia))
            rest.Add(indentation);
        rest.AddRange(leading.Skip(split));
        memberLeading = SyntaxFactory.TriviaList(rest);
    }

    /// <summary>
    /// <c>{Member}Query</c>, numbered by the call's position among the compile calls in members of the same name so
    /// that Fix All gives each call its own field, and bumped past names the type already uses.
    /// </summary>
    private static string ChooseName(
        TypeDeclarationSyntax type,
        MemberDeclarationSyntax member,
        InvocationExpressionSyntax compile,
        SemanticModel semanticModel)
    {
        var memberName = GetMemberName(member, type);
        var ordinal = 1;
        foreach (var other in type.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (other.SpanStart >= compile.SpanStart)
                break;

            if (other.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CompileQuery" or "CompileAsyncQuery" } &&
                other.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault(candidate => candidate.Parent == type) is { } otherMember &&
                GetMemberName(otherMember, type) == memberName)
            {
                ordinal++;
            }
        }

        var containingType = semanticModel.GetDeclaredSymbol(type);
        var used = new HashSet<string>(containingType?.MemberNames ?? Enumerable.Empty<string>())
        {
            type.Identifier.ValueText
        };

        var baseName = memberName + "Query";
        var name = ordinal == 1 ? baseName : baseName + ordinal;
        while (used.Contains(name))
            name = baseName + ++ordinal;

        return name;
    }

    private static string GetMemberName(MemberDeclarationSyntax member, TypeDeclarationSyntax type)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax @event => @event.Identifier.ValueText,
            ConstructorDeclarationSyntax => type.Identifier.ValueText,
            _ => "Compiled"
        };
    }

    /// <summary>True when the query lambda reads the instance, or a local or parameter declared outside it.</summary>
    private static bool ReadsOuterState(LambdaExpressionSyntax lambda, SemanticModel semanticModel)
    {
        var operation = semanticModel.GetOperation(lambda);
        if (operation == null)
            return true;

        foreach (var descendant in operation.DescendantsAndSelf())
        {
            switch (descendant)
            {
                case IInstanceReferenceOperation:
                    return true;
                case ILocalReferenceOperation local when IsDeclaredOutside(local.Local, lambda):
                    return true;
                case IParameterReferenceOperation parameter when IsDeclaredOutside(parameter.Parameter, lambda):
                    return true;
            }
        }

        return false;
    }

    private static bool IsDeclaredOutside(ISymbol symbol, SyntaxNode lambda)
    {
        return symbol.DeclaringSyntaxReferences.All(reference =>
            reference.SyntaxTree != lambda.SyntaxTree || !lambda.Span.Contains(reference.Span));
    }

    private static bool ContainsUnspellableType(ITypeSymbol type)
    {
        return type switch
        {
            { IsAnonymousType: true } => true,
            IErrorTypeSymbol => true,
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsUnspellableType),
            IArrayTypeSymbol array => ContainsUnspellableType(array.ElementType),
            _ => false
        };
    }

    private static int CountErrors(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
