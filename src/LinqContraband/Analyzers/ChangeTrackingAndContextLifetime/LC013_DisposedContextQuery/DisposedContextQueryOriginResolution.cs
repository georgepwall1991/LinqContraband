using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC013_DisposedContextQuery;

public sealed partial class DisposedContextQueryAnalyzer
{
    private static bool TryResolveDisposedContextOrigin(
        IOperation operation,
        IOperation executableRoot,
        HashSet<ILocalSymbol> visitedLocals,
        out ILocalSymbol dbContextLocal)
    {
        operation = operation.UnwrapConversions();
        dbContextLocal = null!;

        switch (operation)
        {
            case ILocalReferenceOperation localReference:
                if (IsDisposedDbContextLocal(localReference.Local, executableRoot))
                {
                    dbContextLocal = localReference.Local;
                    return true;
                }

                if (!visitedLocals.Add(localReference.Local))
                    return false;

                if (!TryResolveAssignedDisposedContextOrigin(
                        localReference.Local,
                        localReference.Syntax.SpanStart,
                        executableRoot,
                        visitedLocals,
                        out dbContextLocal))
                {
                    return false;
                }

                return true;

            case IInvocationOperation invocation when TryGetQueryChainReceiver(invocation, out var receiver):
                return TryResolveDisposedContextOrigin(receiver, executableRoot, visitedLocals, out dbContextLocal);

            case IMemberReferenceOperation memberReference when memberReference.Instance != null:
                return TryResolveDisposedContextOrigin(memberReference.Instance, executableRoot, visitedLocals,
                    out dbContextLocal);

            case IArgumentOperation argument:
                return TryResolveDisposedContextOrigin(argument.Value, executableRoot, visitedLocals, out dbContextLocal);

            default:
                return false;
        }
    }

    private static bool TryGetQueryChainReceiver(IInvocationOperation invocation, out IOperation receiver)
    {
        receiver = null!;

        if (invocation.Instance != null)
        {
            receiver = invocation.Instance;
            return true;
        }

        if (invocation.Arguments.Length > 0 && IsKnownTransparentQueryExtension(invocation.TargetMethod))
        {
            receiver = invocation.Arguments[0].Value;
            return true;
        }

        return false;
    }

    private static bool IsKnownTransparentQueryExtension(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        var containingType = definition.ContainingType;
        var containingNamespace = containingType?.ContainingNamespace?.ToDisplayString();

        if (containingNamespace == "System.Linq" &&
            containingType?.Name is "Queryable" or "Enumerable")
        {
            return !IsMaterializingLinqOperator(definition.Name);
        }

        return containingNamespace == "Microsoft.EntityFrameworkCore" &&
               containingType?.Name == "EntityFrameworkQueryableExtensions";
    }

    private static bool IsMaterializingLinqOperator(string methodName)
    {
        return methodName is
            "ToArray" or
            "ToDictionary" or
            "ToHashSet" or
            "ToList" or
            "ToLookup";
    }

    private static bool IsDisposedDbContextLocal(ILocalSymbol local, IOperation executableRoot)
    {
        if (!local.Type.IsDbContext())
            return false;

        foreach (var syntaxRef in local.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax.SyntaxTree != executableRoot.Syntax.SyntaxTree ||
                !executableRoot.Syntax.Span.Contains(syntax.Span))
            {
                continue;
            }

            if (syntax is VariableDeclaratorSyntax declarator)
            {
                if (declarator.Parent is VariableDeclarationSyntax declaration &&
                    declaration.Parent is LocalDeclarationStatementSyntax localDecl)
                    if (!localDecl.UsingKeyword.IsKind(SyntaxKind.None) ||
                        !localDecl.AwaitKeyword.IsKind(SyntaxKind.None))
                        return true;

                if (declarator.Parent is VariableDeclarationSyntax declaration2 &&
                    declaration2.Parent is UsingStatementSyntax)
                    return true;
            }
        }

        return false;
    }
}
