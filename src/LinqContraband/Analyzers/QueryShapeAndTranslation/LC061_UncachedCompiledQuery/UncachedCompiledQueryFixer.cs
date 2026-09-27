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
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC061_UncachedCompiledQuery;

/// <summary>
/// Provides code fixes for LC061. Moves the <c>EF.CompileQuery</c>/<c>EF.CompileAsyncQuery</c> call into a
/// <c>private static readonly</c> field declared as the first member of the type, so it is set before any other
/// static initializer runs (including ones that reach the member through other methods), spelled with the
/// delegate type the call returns, and uses the field where the call was.
/// </summary>
/// <remarks>
/// No fix is offered when the query reads a local, a parameter of the method or the instance (a static field cannot
/// see them), when it reads a static member of the type (its order against the new field is not known), when the
/// call does not sit in a class, struct or record member, when the member sits inside an <c>#if</c>, <c>#elif</c>
/// or <c>#else</c> region opened in the type before it, when another partial declaration of the type has a static
/// initializer or static constructor (the order across parts is not defined), when the first member's leading trivia
/// holds a preprocessor directive, when the member and the top of the type are in different nullable annotation
/// contexts, or when the rewritten document has more compiler errors than
/// before (for example a delegate type that uses a method type parameter).
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

    // Every fix inserts its field at the top of the type, so the batch fixer's merged edits would collide. Fix-all
    // applies the fixes one after another instead, each on the document the previous one produced.
    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create((fixAllContext, document, diagnostics) =>
            FixAllInDocumentAsync(document, diagnostics, fixAllContext.CancellationToken));

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

    private static async Task<Document?> FixAllInDocumentAsync(
        Document document,
        ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var annotated = new Dictionary<SyntaxNode, SyntaxAnnotation>();
        foreach (var diagnostic in diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start))
        {
            var compile = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (compile != null && !annotated.ContainsKey(compile))
                annotated.Add(compile, new SyntaxAnnotation("LinqContraband.LC061.FixAll"));
        }

        var annotations = annotated.Values.ToList();
        document = document.WithSyntaxRoot(root.ReplaceNodes(
            annotated.Keys,
            (original, rewritten) => rewritten.WithAdditionalAnnotations(annotated[original])));

        foreach (var annotation in annotations)
        {
            var currentRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (currentRoot?.GetAnnotatedNodes(annotation).OfType<InvocationExpressionSyntax>().FirstOrDefault() is not { } compile ||
                semanticModel == null ||
                semanticModel.GetOperation(compile, cancellationToken) is not IInvocationOperation invocation ||
                !UncachedCompiledQueryAnalyzer.IsCompileQuery(invocation.TargetMethod) ||
                Hoist(currentRoot, compile, invocation, semanticModel) is not { } newRoot)
            {
                continue;
            }

            var newDocument = document.WithSyntaxRoot(newRoot);
            var newModel = await newDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (newModel != null && CountErrors(newModel, cancellationToken) <= CountErrors(semanticModel, cancellationToken))
                document = newDocument;
        }

        return document;
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

        // The field goes to the top of the type, outside any `#if` region the member sits in.
        if (IsInsideConditionalRegion(type, member))
            return null;

        // The field goes first in the type declaration, so it is set before every other static initializer in this
        // part runs, whichever of them reaches the member (directly or through other methods). That only holds when
        // its own initializer reads nothing else of the type, and when no other part of a partial type has static
        // initialization, since the order across parts is not defined.
        var typeSymbol = semanticModel.GetDeclaredSymbol(type);
        if (typeSymbol == null ||
            ReadsStaticMembersOf(typeSymbol, lambda, semanticModel) ||
            OtherPartHasStaticInitialization(typeSymbol, type))
        {
            return null;
        }

        // The field goes in front of the first member's leading trivia, so a directive there (`#nullable`, `#if`,
        // `#pragma` and so on) would not apply to it; and its nullable context is the one before that trivia.
        var first = type.Members[0];
        if (first.GetLeadingTrivia().Any(trivia => trivia.IsDirective))
            return null;

        var nullableHere = semanticModel.GetNullableContext(member.SpanStart).AnnotationsEnabled();
        var nullableAtTop = semanticModel.GetNullableContext(first.GetFirstToken().FullSpan.Start).AnnotationsEnabled();
        if (nullableHere != nullableAtTop)
            return null;

        var name = ChooseName(type, member, compile, semanticModel);
        var typeName = delegateType.ToMinimalDisplayString(semanticModel, first.SpanStart, TypeFormat);
        if (SyntaxFactory.ParseMemberDeclaration(
                $"private static readonly {typeName} {name} = {compile.WithoutTrivia().ToFullString()};")
            is not FieldDeclarationSyntax field ||
            field.ContainsDiagnostics)
        {
            return null;
        }

        var endOfLine = member.GetDocumentEndOfLine();
        var indentation = GetIndentation(member.GetLeadingTrivia());
        var leading = new List<SyntaxTrivia>();
        if (indentation.IsKind(SyntaxKind.WhitespaceTrivia))
            leading.Add(indentation);

        var members = type.Members.Replace(member, member.ReplaceNode(compile, SyntaxFactory.IdentifierName(name).WithTriviaFrom(compile)));

        // After compiled-query fields already hoisted to the top (they depend on nothing), else first.
        var index = 0;
        while (index < members.Count - 1 && IsHoistedCompiledQuery(members[index]))
            index++;

        if (index > 0)
        {
            leading.Insert(0, endOfLine);
        }
        else
        {
            var next = members[0];
            members = members.Replace(next, next.WithLeadingTrivia(next.GetLeadingTrivia().Insert(0, endOfLine)));
        }

        field = field.WithLeadingTrivia(leading).WithTrailingTrivia(endOfLine);
        return root.ReplaceNode(type, type.WithMembers(members.Insert(index, field)));
    }

    private static bool IsHoistedCompiledQuery(MemberDeclarationSyntax candidate)
    {
        return candidate is FieldDeclarationSyntax field &&
               field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
               field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) &&
               field.Declaration.Variables.Count == 1 &&
               field.Declaration.Variables[0].Initializer?.Value is InvocationExpressionSyntax
               {
                   Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CompileQuery" or "CompileAsyncQuery" },
                   ArgumentList.Arguments: { Count: 1 } arguments
               } &&
               arguments[0].Expression is LambdaExpressionSyntax;
    }

    private static SyntaxTrivia GetIndentation(SyntaxTriviaList leading)
    {
        for (var i = leading.Count - 1; i >= 0; i--)
        {
            if (leading[i].IsKind(SyntaxKind.WhitespaceTrivia))
                return leading[i];
            if (!leading[i].IsKind(SyntaxKind.EndOfLineTrivia))
                break;
        }

        return default;
    }

    /// <summary>
    /// True when the query lambda reads a static field, property, event or method of the type (or a type nested in
    /// it), whose initialization order relative to the new first field is not known.
    /// </summary>
    private static bool ReadsStaticMembersOf(INamedTypeSymbol type, LambdaExpressionSyntax lambda, SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(lambda) is not { } operation)
            return true;

        foreach (var descendant in operation.DescendantsAndSelf())
        {
            ISymbol? target = descendant switch
            {
                IMemberReferenceOperation reference => reference.Member,
                IInvocationOperation call => call.TargetMethod,
                _ => null
            };

            if (target is { IsStatic: true } && IsWithin(target.ContainingType, type))
                return true;
        }

        return false;
    }

    private static bool IsWithin(INamedTypeSymbol? candidate, INamedTypeSymbol type)
    {
        for (var current = candidate; current != null; current = current.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, type.OriginalDefinition))
                return true;
        }

        return false;
    }

    /// <summary>True when another partial declaration of the type has a static field or property initializer or a static constructor.</summary>
    private static bool OtherPartHasStaticInitialization(INamedTypeSymbol type, TypeDeclarationSyntax declaration)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == declaration.SyntaxTree && reference.Span == declaration.Span)
                continue;

            if (reference.GetSyntax() is TypeDeclarationSyntax part && part.Members.Any(IsStaticInitialization))
                return true;
        }

        return false;
    }

    private static bool IsStaticInitialization(MemberDeclarationSyntax candidate)
    {
        return candidate switch
        {
            BaseFieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                                            field.Declaration.Variables.Any(variable => variable.Initializer != null),
            PropertyDeclarationSyntax { Initializer: not null } property => property.Modifiers.Any(SyntaxKind.StaticKeyword),
            ConstructorDeclarationSyntax constructor => constructor.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false
        };
    }

    /// <summary>
    /// True when an <c>#if</c> opened after the type's open brace is still open at <paramref name="member"/>. The
    /// field goes to the top of the type, outside that region, where it would stay active in configurations that
    /// leave out the member and whatever its query uses.
    /// </summary>
    private static bool IsInsideConditionalRegion(TypeDeclarationSyntax type, MemberDeclarationSyntax member)
    {
        var start = type.OpenBraceToken.Span.End;
        if (member.SpanStart <= start)
            return false;

        var depth = 0;
        foreach (var trivia in type.DescendantTrivia(TextSpan.FromBounds(start, member.SpanStart), descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                depth++;
            else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                depth--;
        }

        return depth != 0;
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
